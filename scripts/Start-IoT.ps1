param([switch]$NoBuild)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$logDirectory = Join-Path $workspace 'logs'
$backend = $null
$gatewayStarted = $false
Push-Location $workspace
try {
    if (-not (Test-Path -LiteralPath (Join-Path $workspace 'appsettings.json'))) {
        throw 'Create root appsettings.json using the settings in README.md.'
    }
    $settings = Get-Content -LiteralPath (Join-Path $workspace 'appsettings.json') -Raw | ConvertFrom-Json
    if (-not $NoBuild) {
        dotnet build IoT.sln --nologo --verbosity quiet
        if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    }
    docker compose up -d
    if ($LASTEXITCODE -ne 0) { throw 'Broker startup failed.' }
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $assembly = Join-Path $workspace 'src/Application.Api/bin/Debug/net8.0/Application.Api.dll'
    $backendLog = Join-Path $logDirectory 'Application.Api.log'
    $backend = Start-Process -FilePath (Get-Command dotnet).Source `
        -ArgumentList @('"' + $assembly + '"') -WorkingDirectory $workspace `
        -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $backendLog `
        -RedirectStandardError (Join-Path $logDirectory 'Application.Api.error.log')
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    $apiUrl = if ($settings.Urls) { $settings.Urls.Split(';')[0].TrimEnd('/') } else { 'http://localhost:5114' }
    while ($true) {
        if ($backend.HasExited) { throw 'Backend stopped. Check logs/.' }
        try {
            Invoke-WebRequest -Uri ($apiUrl + '/') -UseBasicParsing -TimeoutSec 1 -ErrorAction Stop | Out-Null
            break
        } catch {
            if ([DateTime]::UtcNow -gt $deadline) { throw 'Backend API did not start. Check logs/.' }
        }
        Start-Sleep -Milliseconds 200
    }
    Write-Output "Application started. API: $apiUrl | service logs: logs/"
    Write-Output 'Gateway commands: offline, online, stop.'
    $gatewayStarted = $true
    $gatewayLog = Join-Path $logDirectory 'Device.Gateway.log'
    dotnet (Join-Path $workspace 'src/Device.Gateway/bin/Debug/net8.0/Device.Gateway.dll') |
        Tee-Object -FilePath $gatewayLog
    if ($LASTEXITCODE -ne 0) { throw 'Gateway stopped with an error.' }
}
finally {
    # Leave the Docker broker running; stop only this launcher's backend.
    if ($null -ne $backend) {
        if ($gatewayStarted -and $settings.IoTHub.DeviceConnectionString -and $settings.ServiceBus.ConnectionString) {
            $death = Select-String -LiteralPath $gatewayLog -Pattern '^Forwarded NDEATH: ([a-f0-9]+)$' |
                Select-Object -Last 1
            $unconfirmed = Select-String -LiteralPath $gatewayLog -Pattern 'Final NDEATH was not confirmed' -Quiet
            if ($null -ne $death -and -not $unconfirmed) {
                $deathPattern = 'Completed queue message ' + $death.Matches[0].Groups[1].Value
                $deadline = [DateTime]::UtcNow.AddSeconds(15)
                while (-not $backend.HasExited -and
                    -not (Select-String -LiteralPath $backendLog -Pattern $deathPattern -Quiet)) {
                    if ([DateTime]::UtcNow -gt $deadline) {
                        Write-Warning 'Final NDEATH was not processed. Check the backend log.'
                        break
                    }
                    Start-Sleep -Milliseconds 200
                }
            }
        }
        if (-not $backend.HasExited) { Stop-Process -Id $backend.Id -Force -ErrorAction SilentlyContinue }
    }
    Pop-Location
}
