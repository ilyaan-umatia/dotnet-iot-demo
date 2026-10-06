# Local MQTT Demo

Requires .NET 8 SDK, Docker Desktop and Git for Windows (includes OpenSSL). Start Docker Desktop, then run from this folder:

```powershell
./scripts/New-LocalBrokerCertificate.ps1
docker compose up -d
```

Start the subscriber and publisher in separate terminals:

```powershell
dotnet run --project src/Subscriber.Worker
dotnet run --project src/Publisher.Api --launch-profile http
```

Send a POST request to `http://localhost:5113/publish`:

```json
{
  "deviceId": "fridge-01",
  "temperature": 9.2,
  "humidity": 65,
  "recordedAt": "2026-10-01T10:00:00Z"
}
```

Check the subscriber terminal for the received reading. Broker settings are in each project's `appsettings.json`. Port `8883` must be free.

To stop the broker:

```powershell
docker compose down
```
