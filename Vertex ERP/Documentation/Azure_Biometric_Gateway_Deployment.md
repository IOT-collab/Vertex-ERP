# Azure biometric deployment

The biometric machine is on the office LAN, while Azure App Service is on the public internet. Therefore the `BiometricReceiver` must stay running on a computer on the same LAN as the machine. Do **not** deploy the receiver as an Azure WebJob: it cannot listen on the office LAN or reach a machine using a private IP address.

## Data flow

`Biometric machine -> office computer:8082 -> Azure Vertex ERP API -> Azure PostgreSQL`

The receiver saves every accepted ADMS payload in a local durable outbox before it is sent to Azure. If the internet or Azure is unavailable, it retries automatically after the configured interval. Azure de-duplicates already received punches.

## One-time Azure configuration

1. Publish the **Vertex ERP** project to Azure.
2. In the Azure App Service **Configuration > Application settings**, add `BiometricIngress__ApiKey`. Give it a long random secret (at least 32 characters) and save/restart the app. Do not put this secret in source control.
3. Make sure every biometric device is registered and active in the Azure ERP database, with its exact serial number.

## Office receiver configuration

1. Publish **BiometricReceiver** to a fixed folder on the office computer (the computer already receiving the attendance data), not to Azure.
2. In that published folder, update `remoteattendance.json` (this file is kept
   separate so an ERP republish cannot overwrite the forwarding settings):

```json
"CloudAttendanceForwarding": {
  "Enabled": true,
  "Endpoint": "https://vertex-erp-app-b6eec2c3fthbgbaq.indiasouthcentral-01.azurewebsites.net/api/biometric/v1/adms",
  "ApiKey": "the-same-secret-set-in-Azure",
  "RetryIntervalSeconds": 30
}
```

   Keep `CloudAttendanceForwarding:Enabled` as `false` in source control. Set it
   to `true` only in the published receiver's local `remoteattendance.json`.

3. Configure the ZKTeco/biometric device ADMS server as the office computer's LAN IP and port `8082`. It must not be the Azure URL.
4. Start the receiver and allow inbound TCP `8082` in the office computer firewall for the local network.

## Verification

- On the office computer, open `http://localhost:8082/health`; it should report `running`.
- Make one punch, then check the ERP attendance page in Azure. If Azure is temporarily unavailable, inspect `App_Data/CloudAttendanceOutbox` beside the receiver; files remain there until successfully delivered.
