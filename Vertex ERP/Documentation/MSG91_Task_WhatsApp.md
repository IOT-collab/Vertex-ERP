# MSG91 task assignment WhatsApp

## Current state

The user confirmed the approved template body and variable order on 29 October 2026. `Msg91WhatsAppOptions.ConfirmedTemplateBody` and `appsettings.Msg91.example.json` contain that exact text. Body 1 = EmployeeName, 2 = TaskTitle, 3 = AssignedBy (the task's manager), 4 = TaskDeadline. Default TemplateMappingVerified is true. SendingEnabled remains false: confirmation of template content is separate from approval to send live messages. WebhooksEnabled defaults false too.

The exact confirmed POST endpoint remains https://api.msg91.com/api/v5/whatsapp/whatsapp-outbound-message/bulk/ . The user-supplied nested payload is unchanged: integrated_number, content_type, payload.messaging_product/type/template, template.name/language/namespace/to_and_components, recipient and body_1..body_4 text components. No new outgoing API fields are added.

## Task save and queue

ApplicationDbContext stages one WhatsAppTaskNotification per Added WorkTask inside the same EF save transaction. Both TaskManagementController.CreateTask and ProjectWorkspaceController.SaveTask are covered without changing responses or validation. No provider request happens in the task request. Provider outages therefore cannot make an otherwise valid task assignment fail. A database failure rolls back task and outbox together. Existing-task edits/status changes do not send a second new-task message. A unique TaskId index prevents duplicate queue rows.

The worker claims committed rows using PostgreSQL FOR UPDATE SKIP LOCKED, then commits Processing before HTTP. Current PostgreSQL records supply employee FullName/PhoneNumber, task Title/DueDate and Manager.FullName. There is no separate WhatsApp-number column in this ERP; the registered employee PhoneNumber is used. Ten-digit local numbers default to India (91); explicit international prefixes are retained. Invalid/missing phones fail without calling MSG91. Deleted, reassigned or inactive employees are skipped. Current manager data is used, even if HR/Admin originally entered the task.

## Send outcomes and retries

Typed HttpClientFactory/DI service uses a 30-second timeout and no redirects. Authkey is redacted in HttpClient logs. Logs retain queue ID, sanitized error code, HTTP status and provider identifier; they do not log authkeys, payloads, raw errors or employee phone/content.

Accepted means provider acceptance only. The bulk request_id is stored in ProviderMessageId; it is not a fabricated Meta message ID. Response message_id is supported if present. DNS errors, 429 and explicitly rejected 5xx requests retry with exponential backoff, jitter and Retry-After, up to five attempts. Permanent rejections stop. Uncertain network errors, timeouts or malformed/identifier-less successes become NeedsReview. A crashed Processing item becomes NeedsReview after five minutes. These are not automatically resent: the confirmed API has no idempotency guarantee, so exactly-once delivery across uncertain responses cannot be guaranteed. Reconcile provider logs before an operator retries such a row.

## Delivery reports

Callback: POST `/api/integrations/msg91/whatsapp/delivery` with header `X-Vertex-Msg91-Webhook-Token`. This is an ERP callback authentication header, not an invented MSG91 send API field. Configure the same independent random secret (at least 32 characters) in MSG91's webhook header settings and the ERP environment. Auth compares hashed tokens in constant time; no public unauthenticated status updates are accepted.

Configure MSG91 Webhook (New), WhatsApp, On Outbound Report Received, using the following documented JSON fields: requestId, customerNumber, eventName, integratedNumber, direction, templateName, templateLanguage, uuid, ts, reason. Include explicit-offset ISO timestamp `ts`, direction `1`, language `en`, and the confirmed number/template. Default handler accepts one JSON event object per POST; malformed or unrelated events return 400, unauthorized events 401. Payload limit 64KB.

Sent / Delivered / Read / Failed are stored separately from send status, with UTC times, sanitized failure code and Meta uuid. Events are durably inserted with ON CONFLICT idempotency before returning 200. The worker reconciles receipts by exact provider request ID plus recipient hash under row locks. Early-arriving receipts wait in the inbox; duplicate or out-of-order reports cannot regress Delivered/Read. Unmatched reports remain unapplied for investigation rather than being matched by employee name or guessed IDs. A live callback sample must confirm requestId correlation when the integration is later approved; no live provider call was performed.

Only the recipient hash and normalized report metadata are stored, never raw callback content or complete failure reason. Report processing never retries sending a delivered message.

Sources: https://msg91.com/help/webhook-new/how-to-receive-whatsapp-delivery-reports-via-webhook-new and https://msg91.com/help/whatsapp/webengage-whatsapp-integration (custom callback headers).

## Configuration / later activation

`appsettings.Msg91.example.json` is a secret-free reference, not automatically loaded. The existing environment/user-secrets configuration pipeline binds `Msg91WhatsApp`.

- `Msg91WhatsApp__AuthKey`: secure MSG91 authkey, never commit.
- `Msg91WhatsApp__SendingEnabled`: false; true requires explicit live-send approval.
- `TemplateMappingVerified`: true for the confirmed template; `ApprovedTemplateText` and ParameterOrder default to the confirmed values.
- IntegratedNumber = 919217614338, TemplateName = vertex_task_assigned, Language = en, Namespace = cdc730df_d16e_48b9_94b9_a057d00e67a1.
- `Msg91WhatsApp__WebhookToken`: independent secure random token, at least 32 characters.
- `Msg91WhatsApp__WebhooksEnabled`: false until the callback has been configured and approved.

Before future activation, review Pending rows to avoid sending obsolete/test assignments. This implementation does not configure the MSG91 dashboard or secret values for you, deploy the app, apply migrations or send messages. No template text is missing anymore.

## Database and modified files

- Services/Msg91WhatsAppService.cs: confirmed template/options, exact sender/payload and result classification.
- Services/WhatsAppTaskQueue.cs: durable queue, dispatcher and worker.
- Models/WhatsAppTaskNotification.cs: queue, API outcome and independent delivery state.
- Services/Msg91DeliveryService.cs, Models/WhatsAppDeliveryEvent.cs, Controllers/Msg91DeliveryController.cs: authenticated durable receipt inbox and reconciliation.
- Data/ApplicationDbContext.cs and Program.cs: save hook, constraints, DI, background worker.
- Migrations/*AddWhatsAppTaskNotificationQueue*: original queue table, unique TaskId and Tasks foreign key.
- Migrations/*AddMsg91DeliveryTracking*: adds delivery fields/recipient hash and WhatsAppDeliveryEvents with unique EventKey. No existing employee/task data is rewritten.
- Migrations/ApplicationDbContextModelSnapshot.cs: updated model.
- appsettings.Msg91.example.json and this guide.
- tests/Msg91WhatsAppChecks/Program.cs and DeliveryChecks.cs: offline/mocked integration checks.

Migrations have been generated, not applied. Existing application startup already applies migrations, so an approved deployment/startup will create the schema.

## Tests

Run `dotnet run --project tests/Msg91WhatsAppChecks/Msg91WhatsAppChecks.csproj -c EmployeeCategory --no-restore -p:UseAppHost=false`.
HTTP uses a fake HttpMessageHandler; database-save probes suppress actual database writes. Migration SQL tests generate PostgreSQL SQL offline. Tests cover exact JSON/order, switches, retry limits, duplicate prevention, task-save behavior, webhook auth/parsing/deduplication, monotonic delivery transitions, UTC dates and schema generation. They do not establish live delivery, callback registration or actual PostgreSQL concurrency behavior.
