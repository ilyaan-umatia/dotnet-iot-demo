# .NET MQTT Demo

This is a learning project with two separate .NET applications and a local Mosquitto broker.

```text
HTTP caller -> Publisher.Api -> Mosquitto TLS (localhost:8883) -> Subscriber.Worker
```

## Projects

- `src/Publisher.Api`: ASP.NET Core API with a `POST /publish` endpoint.
- `src/Subscriber.Worker`: subscribes to device topics, reads JSON telemetry, and logs a warning when temperature exceeds the demo threshold of 8 C.
- `src/Mqtt.Common`: configuration and device payload types shared by both applications.

Both applications have their own `appsettings.json` because they are separate processes. The `Mqtt` section tells each application **where to connect** and **which topic to use**:

```json
"Mqtt": {
  "Host": "localhost",
  "Port": 8883,
  "Topic": "devices/{deviceId}/messages",
  "UseTls": true,
  "BrokerCertificatePath": "../../broker/certs/broker.crt"
}
```

The publisher replaces `{deviceId}` with the payload's device ID (for example `devices/fridge-01/messages`). The subscriber replaces it with `+` and listens to `devices/+/messages`, so it can receive messages from multiple devices.

Mosquitto is separate from the .NET apps. The existing Windows service still listens on port `1883` without TLS. For this exercise, `broker/mosquitto-tls.conf` starts a **second local broker process** on `127.0.0.1:8883`; both .NET apps now connect to that TLS broker. Messages do not cross between these two brokers.

`BrokerCertificatePath` points to the broker's public certificate. Each app uses that exact certificate to verify the TLS connection. The private key stays with Mosquitto in `broker/certs/broker.key`. Both generated files are ignored by Git.

## Certificates and TLS broker

Install a .NET SDK capable of building .NET 8 projects, Mosquitto, and OpenSSL (the certificate script can use OpenSSL bundled with Git for Windows). Run this once from the repository root to create **one self-signed `localhost` certificate and one private key**. The script does not change the Windows trust store. Certificates are generated locally and are not included in this repository:

```powershell
& ./scripts/New-LocalBrokerCertificate.ps1
```

In its own PowerShell terminal, start the TLS broker from the repository root:

```powershell
& 'C:\Program Files\Mosquitto\mosquitto.exe' -c broker/mosquitto-tls.conf -v
```

Wait for `Opening ipv4 listen socket on port 8883`. Keep this terminal open. The broker config is local-only (`127.0.0.1`) and allows anonymous MQTT clients for this exercise. TLS encrypts traffic and lets clients verify the broker; this setup does not yet authenticate individual clients.

## Run and test

After the TLS broker is running, stop any old .NET processes with Ctrl+C so they load the updated configuration. From the repository root, open two more PowerShell terminals.

Terminal 1: start the subscriber and wait for `Subscribed to devices/+/messages`:

```powershell
dotnet run --project src/Subscriber.Worker
```

Terminal 2: start the publisher API:

```powershell
dotnet run --project src/Publisher.Api --launch-profile http
```

The URL `http://localhost:5113/` is a simple status page. Opening it does **not** publish a message. In another PowerShell terminal, call the POST endpoint:

```powershell
Invoke-RestMethod -Method Post -Uri 'http://localhost:5113/publish' -ContentType 'application/json' -Body '{"deviceId":"fridge-01","temperature":9.2,"humidity":65,"recordedAt":"2026-10-01T10:00:00Z"}'
```

The API returns `status: published` and the device ID. The Worker logs the readings and topic `devices/fridge-01/messages`, followed by a high-temperature warning for this example. Temperature is in Celsius and humidity is a percentage. `5113` is the API's HTTP port; `8883` is the TLS broker's MQTT port. The API creates a short-lived MQTT connection for each request to keep this first example easy to follow. The worker keeps its connection open and retries if the broker is unavailable.

### Test with Postman

1. Create a new HTTP request and select **POST**.
2. Enter `http://localhost:5113/publish`.
3. Select **Body** → **raw** → **JSON**. This sets `Content-Type: application/json`.
4. Enter the device payload below and click **Send**.
5. Check for a `200 OK` response with `status: published`, then look for the matching device readings and topic in the Worker terminal.

```json
{
  "deviceId": "fridge-01",
  "temperature": 9.2,
  "humidity": 65,
  "recordedAt": "2026-10-01T10:00:00Z"
}
```

Try `fridge-02` with temperature `4.5` to verify a second device topic without a high-temperature warning. Empty or invalid device IDs and humidity outside 0–100 return `400 Bad Request`.

Postman talks HTTP to the .NET API. The API talks MQTT over TLS to Mosquitto. The Postman URL remains `http://localhost:5113` because this step secures the **MQTT connection**, not the API's HTTP connection.

## Next steps

1. Try a few payloads through Postman and inspect the API response, Worker logs, and broker logs.
2. Add client authentication (username/password or client certificates) if you want to continue toward a real deployment.

## Why Docker was mentioned

Docker could run Mosquitto in an isolated container with its configuration mounted from this project. It is optional. The native Windows service is already running, so no Docker setup is required for this project.
