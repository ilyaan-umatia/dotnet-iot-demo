$ErrorActionPreference = 'Stop'

$openssl = @(
    (Get-Command openssl -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -First 1),
    'C:\Program Files\Git\usr\bin\openssl.exe',
    'C:\Program Files\Git\mingw64\bin\openssl.exe'
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1

if (-not $openssl) {
    throw 'OpenSSL was not found. Install Git for Windows with OpenSSL or provide openssl.exe on PATH.'
}

$certificateDirectory = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\broker\certs'))
New-Item -ItemType Directory -Path $certificateDirectory -Force | Out-Null

$brokerKey = Join-Path $certificateDirectory 'broker.key'
$brokerCertificate = Join-Path $certificateDirectory 'broker.crt'

if ((Test-Path -LiteralPath $brokerKey) -and (Test-Path -LiteralPath $brokerCertificate)) {
    Write-Output "Self-signed broker certificate already exists in $certificateDirectory"
    return
}
if ((Test-Path -LiteralPath $brokerKey) -or (Test-Path -LiteralPath $brokerCertificate)) {
    throw "Only one broker certificate file exists in $certificateDirectory. Resolve that partial set before generating a new one."
}

& $openssl req -x509 -newkey rsa:2048 -noenc -keyout $brokerKey -out $brokerCertificate -days 365 -sha256 -subj '/CN=localhost' -addext 'subjectAltName=DNS:localhost,IP:127.0.0.1' -addext 'basicConstraints=critical,CA:FALSE' -addext 'keyUsage=critical,digitalSignature,keyEncipherment' -addext 'extendedKeyUsage=serverAuth'
if ($LASTEXITCODE -ne 0) { throw 'Could not create the self-signed broker certificate.' }

Write-Output "Created one self-signed broker certificate and its private key in $certificateDirectory"
