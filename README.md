# .NET MQTT Demo

A .NET API that publishes device readings to a local Mosquitto broker, with a background Worker that receives and logs them. MQTT connections use TLS with a self-signed certificate.

## Projects

- **Publisher.Api** — accepts device data through `POST /publish` and publishes it to MQTT.
- **Subscriber.Worker** — receives device messages and logs a warning when temperature exceeds 8°C.
- **Mqtt.Common** — shared settings and device message model.

## Requirements

- .NET 8 SDK or later
- Docker Desktop (Linux containers), or a local Mosquitto installation
- OpenSSL (the setup script also supports OpenSSL included with Git for Windows)

## Run locally

Run these commands from the project root.

Generate the local certificate once:

```powershell
./scripts/New-LocalBrokerCertificate.ps1
```

Start the broker with Docker Desktop running:

```powershell
docker compose up -d
```

To view broker logs or stop it:

```powershell
docker compose logs -f mosquitto
docker compose down
```

The container uses `broker/mosquitto-docker.conf` and the local certificate files. The API and Worker still run with .NET on your computer.

Alternatively, start the locally installed broker. Run only one broker on port `8883` at a time:

```powershell
& 'C:\Program Files\Mosquitto\mosquitto.exe' -c broker/mosquitto-tls.conf -v
```

In a second terminal, start the subscriber:

```powershell
dotnet run --project src/Subscriber.Worker
```

In a third terminal, start the API:

```powershell
dotnet run --project src/Publisher.Api --launch-profile http
```

Broker settings are in each application's `appsettings.json`. The broker runs locally on port `8883`. Generated certificates and private keys are excluded from Git.

## Test with Postman

Send a `POST` request to `http://localhost:5113/publish` with **Body → raw → JSON**:

```json
{
  "deviceId": "fridge-01",
  "temperature": 9.2,
  "humidity": 65,
  "recordedAt": "2026-10-01T10:00:00Z"
}
```

The API returns `200 OK` with `status: published`. The Worker logs the readings and a high-temperature warning for this example. Temperature is in Celsius and humidity is a percentage.

Messages are published to `devices/{deviceId}/messages`. The Worker subscribes to `devices/+/messages` to receive readings from multiple devices. Try `fridge-02` with temperature `4.5` for a reading without a warning.

Invalid device IDs and humidity outside 0–100 return `400 Bad Request`. This local broker uses TLS and allows connections without client authentication.
