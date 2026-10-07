# IoT Device Monitoring

Gateway -> Mosquitto -> gateway cloud forwarder -> IoT Hub -> Service Bus -> backend.

Two applications run: `Device.Gateway` simulates the fridge, publishes Sparkplug messages and forwards them to Azure. `Application.Api` handles queue messages and serves the API. `IoT.Contracts` is their shared message library.

Install .NET 8 SDK, Docker Desktop and Git for Windows. Create root `appsettings.json` (ignored by Git):

```json
{
  "Mqtt": {
    "Host": "127.0.0.1", "Port": 8883, "ClientId": "gateway-01",
    "UseTls": true, "BrokerCertificatePath": "certs/broker.crt"
  },
  "Sparkplug": { "GroupId": "warehouse-01", "EdgeNodeId": "gateway-01" },
  "Device": { "DeviceId": "fridge-01", "GpioPin": 17, "ReadingIntervalSeconds": 5 },
  "IoTHub": { "DeviceConnectionString": "" },
  "ServiceBus": { "ConnectionString": "", "QueueName": "device-messages" },
  "Urls": "http://localhost:5114"
}
```

Add gateway-01 IoT Hub credentials and Service Bus Listen credentials when using Azure. IoT Hub must route gateway messages to the queue. Blank credentials disable cloud forwarding/queue consumption.

From this folder:

```powershell
./scripts/New-LocalBrokerCertificate.ps1
./scripts/Start-IoT.ps1
```

Generate the broker certificate once. The startup script builds and starts the Docker broker, backend and gateway. Gateway commands: `offline`, `online`, `stop`. Changes take effect on the next 5-second interval. Logs are in `logs/`.

The gateway subscribes before publishing births. Normal shutdown publishes device/node deaths and waits up to 15 seconds for Azure forwarding. The launcher then gives the backend up to 15 seconds to process NDEATH.

To run manually, use separate terminals after `docker compose up -d`:

```powershell
dotnet run --project src/Application.Api
dotnet run --project src/Device.Gateway
```

The gateway also runs without the backend. With Azure configured, messages reach the Service Bus queue while the backend is stopped.

MQTTX: TLS on `127.0.0.1:8883`, trust `broker/certs/broker.crt`, subscribe to `spBv1.0/warehouse-01/#` before gateway startup. Decode using `schemas/sparkplug_b.proto`, type `org.eclipse.tahu.protobuf.Payload`.

API: `http://localhost:5114/gateways`, `/devices`, `/devices/fridge-01`.

Fridge messages carry `GpioPin` and `GpioValue`. `GpioMessageService` derives door status (`1` = open, `0` = closed), checks readings and manages gateway/device lifecycle state. GPIO input is simulated.

Checks: `dotnet run --project tests/IoT.Checks`.

Settings, generated files, logs and broker certificates are not uploaded. Backend state and the gateway's 200-event forwarding buffer are in memory. Backend restart recovery is deferred: queued data can require births that the restarted backend no longer has. After a broker reconnect or sequence gap, restart the gateway if births were missed. Restarts/full buffers/QoS 0 can lose events. A forced gateway stop publishes the broker's NDEATH Will locally; forwarding that Will to Azure is deferred because the forwarder stops with the gateway. This application uses simulated hardware and named metrics, without Rebirth/command support.

Stop broker: `docker compose stop`.
