# IoT Project

Requires .NET 8 SDK, Docker Desktop and Git for Windows.

Add your settings and Azure connection strings to root `appsettings.json` (not included in Git).

Run from the project folder:

```powershell
./scripts/New-LocalBrokerCertificate.ps1 # first run only
./scripts/Start-IoT.ps1
```
