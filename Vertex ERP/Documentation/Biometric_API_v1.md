# Vertex ERP Biometric API v1

This vendor-neutral API accepts normalized attendance punches from biometric machines, vendor gateways, or small protocol adapters on the same private network.

## Health check

`GET http://SERVER-IP:8082/api/biometric/v1/health`

## Submit punches

`POST http://SERVER-IP:8082/api/biometric/v1/punches`

```json
{
  "deviceSerialNumber": "DEVICE-SERIAL-001",
  "punches": [
    {
      "deviceUserId": "15",
      "punchTime": "2026-08-13T12:45:00",
      "punchState": "IN",
      "verificationMode": "Fingerprint",
      "workCode": null,
      "eventId": "optional-vendor-event-id"
    }
  ]
}
```

The device serial must first be registered and active in Vertex ERP. Unknown device user IDs are stored safely and appear under Employee Mappings. Maximum batch size is 1,000 punches and maximum request size is 1 MB.

ZKTeco ADMS machines continue to use `/iclock/cdata`; they do not need to call this JSON endpoint. A proprietary SDK/pull connector should translate vendor records into the JSON contract above.
