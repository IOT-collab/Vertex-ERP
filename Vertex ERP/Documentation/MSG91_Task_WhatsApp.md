# MSG91 task assignment WhatsApp

## Current state

The user confirmed the approved template body and variable order on 29 October 2026. `Msg91WhatsAppOptions.ConfirmedTemplateBody` and `appsettings.Msg91.example.json` contain that exact text. Body 1 = EmployeeName, 2 = TaskTitle, 3 = AssignedBy (the task's manager), 4 = TaskDeadline. Default TemplateMappingVerified is true. SendingEnabled remains false: confirmation of template content is separate from approval to send live messages. WebhooksEnabled defaults false too.

The exact confirmed POST endpoint remains https://api.msg91.com/api/v5/whatsapp/whatsapp-outbound-message/bulk/ . The user-supplied nested payload is unchanged: integrated_number, content_type, payload.messaging_product/type/template, template.name/language/namespace/to_and_components, recipient and body_1..body_4 text components. No new outgoing API fields are added.

## Task save and queue

ApplicationDbContext stages one WhatsAppTaskNotification per Added WorkTask inside the same EF save transaction. Both TaskManagementController.CreateTask and ProjectWorkspaceController.SaveTask are covered without changing responses or validation. No provider request happens in the task request. Provider outages therefore cannot make an otherwise valid task assignment fail. A database failure rolls back task and outbox together. Existing-task edits/status changes do not send a second new-task message. A unique TaskId index prevents duplicate queue rows.

The worker claims committed rows using PostgreSQL FOR UPDATE SKIP LOCKED, then commits Processing before HTTP. Current PostgreSQL records supply employee FullName/PhoneNumber, task Title/DueDate and Manager.FullName. There is no separate WhatsApp-number column in this ERP; the registered employee PhoneNumber is used. Ten-digit local numbers default to India (91); explicit international prefixes are retained. Invalid/missing phones fail without calling MSG91. Deleted, reassigned or inactive employees are skipped. Current manager data is used, even if HR/Admin originally entered the task.

## Send outcomes and retries

Typed HttpClientFactory/DI service uses a 30-second timeout and no redirects. Authkey is redacted in HttpClient logs. Logs retain queue ID, sanitized error code, HTTP status, submission timestamp, provider identifier and an allow-listed sanitized response object (`status`, `hasError`, provider ID only). They do not log authkeys, payloads, raw errors or employee phone/content.

Accepted means provider acceptance only. The bulk request_id is stored in ProviderMessageId; it is not a fabricated Meta message ID. Response message_id is supported if present. DNS errors, 429 and explicitly rejected 5xx requests retry with exponential backoff, jitter and Retry-After, up to five attempts. Permanent rejections stop. Uncertain network errors, timeouts or malformed/identifier-less successes become NeedsReview. A crashed Processing item becomes NeedsReview after five minutes. These are not automatically resent: the confirmed API has no idempotency guarantee, so exactly-once delivery across uncertain responses cannot be guaranteed. Reconcile provider logs before an operator retries such a row.

## Delivery reports

Callback: POST `/api/integrations/msg91/whatsapp/delivery` with header `X-Vertex-Msg91-Webhook-Token`. This is an ERP callback authentication header, not an invented MSG91 send API field. Configure the same independent random secret (at least 32 characters) in MSG91's webhook header settings and the ERP environment. Auth compares hashed tokens in constant time; no public unauthenticated status updates are accepted.

Configure MSG91 Webhook (New), WhatsApp, On Outbound Report Received, using the following documented JSON fields: requestId, customerNumber, eventName, integratedNumber, direction, templateName, templateLanguage, uuid, ts, reason. Include explicit-offset ISO timestamp `ts`, direction `1`, language `en`, and the confirmed number/template. Default handler accepts one JSON event object per POST; malformed or unrelated events return 400, unauthorized events 401. Payload limit 64KB.

Sent / Delivered / Read / Failed are stored separately from send status, with UTC times, sanitized failure code and Meta uuid. Events are durably inserted with ON CONFLICT idempotency before returning 200. The worker reconciles receipts by exact provider request ID plus recipient hash under row locks. Early-arriving receipts wait in the inbox; duplicate or out-of-order reports cannot regress Delivered/Read. Unmatched reports remain unapplied for investigation rather than being matched by employee name or guessed IDs. A live callback sample must confirm requestId correlation when the integration is later approved; no live provider call was performed.

Only the recipient hash and normalized report metadata are stored, never raw callback content or complete failure reason. Report processing never retries sending a delivered message.

Sources: https://msg91.com/help/webhook-new/how-to-receive-whatsapp-delivery-reports-via-webhook-new and https://msg91.com/help/whatsapp/webengage-whatsapp-integration (custom callback headers).

## Configuration / later activation

`Msg91WhatsAppOptions` in `Services/Msg91WhatsAppService.cs` is bound from the `Msg91WhatsApp` section in `Program.cs`. `appsettings.json` contains non-secret confirmed settings; `AuthKey` stays empty and `SendingEnabled` stays false. `ApiUrl` and `LanguageCode` are the configuration property names. Readiness reports safe reason codes (`sending_disabled`, `auth_key_missing_or_invalid`, etc.) and never includes credential values. `appsettings.Msg91.example.json` remains a reference copy.

- `Msg91WhatsApp__AuthKey`: secure MSG91 authkey, never commit.
- `Msg91WhatsApp__SendingEnabled`: false; true requires explicit live-send approval.
- `ApiUrl`: `https://api.msg91.com/api/v5/whatsapp/whatsapp-outbound-message/bulk/`.
- `TemplateMappingVerified`: true for the confirmed template; `ApprovedTemplateText` defaults to the confirmed body, while `ParameterOrder` must be set to the confirmed four-item order.
- IntegratedNumber = 919217614338, TemplateName = vertex_task_assigned, LanguageCode = en, Namespace = cdc730df_d16e_48b9_94b9_a057d00e67a1.
- `Msg91WhatsApp__WebhookToken`: independent secure random token, at least 32 characters.
- `Msg91WhatsApp__WebhooksEnabled`: false until the callback has been configured and approved.

Before future activation, review Pending rows to avoid sending obsolete/test assignments. This implementation does not configure the MSG91 dashboard or secret values for you, deploy the app, apply migrations or send messages. No template text is missing anymore.

### Local User Secrets

`Vertex ERP.csproj` already has a UserSecretsId. From the repository root, store the key locally with:

```powershell
dotnet user-secrets set "Msg91WhatsApp:AuthKey" "<MSG91_AUTH_KEY>" --project "Vertex ERP/Vertex ERP.csproj"
```

Keep sending disabled until the explicit test approval:

```powershell
dotnet user-secrets set "Msg91WhatsApp:SendingEnabled" "false" --project "Vertex ERP/Vertex ERP.csproj"
```

After approval, enable only the test environment, assign one approved test task, then disable sending again:

```powershell
dotnet user-secrets set "Msg91WhatsApp:SendingEnabled" "true" --project "Vertex ERP/Vertex ERP.csproj"
dotnet user-secrets set "Msg91WhatsApp:SendingEnabled" "false" --project "Vertex ERP/Vertex ERP.csproj"
```

Replace the placeholder locally; never paste the key into committed files or share command output containing it. User Secrets are for local development and are not encrypted production storage.

### Controlled live test procedure

1. Select one authorized test employee and verify their registered WhatsApp number in Employee Master; do not put the number into logs or test source.
2. Confirm that the number is WhatsApp-enabled and eligible for the approved template in the MSG91 account.
3. Confirm template name, language, namespace, approved content and body variables 1–4 in MSG91. The application configuration matches the confirmed mapping.
4. Inspect `WhatsAppTaskNotifications` and resolve or hold every unintended old `Pending`/`Retry` row before enabling the worker; it will process eligible rows automatically.
5. In local Development, confirm the secret is present and run the readiness diagnostics. Only after explicit approval, set `SendingEnabled` to `true` in that test environment.
6. Assign exactly one new test task to the selected employee.
7. Confirm its queue row is claimed and reaches a terminal/known state. Review logs for notification ID, UTC submission timestamp, HTTP status, provider ID, safe response metadata and error category.
8. `Accepted` means MSG91 accepted the request, not that WhatsApp delivered it. Confirm delivery through the configured authenticated callback or MSG91 delivery report.
9. If the result is unsuccessful or uncertain, set `SendingEnabled` back to `false`, stop before assigning another test task, and reconcile the provider request ID to avoid duplicates.
10. No live test is performed by the automated test suite; it uses a fake HTTP handler.

### Azure Ubuntu VM / systemd / Key Vault

`Program.cs` reads `AZURE_KEY_VAULT_NAME` and fetches only the exact `Msg91-AuthKey` secret with `SecretClient` and `DefaultAzureCredential`. In the Ubuntu VM it uses the system-assigned Managed Identity. On success, the value is added in-memory under `Msg91WhatsApp:AuthKey`; it is never logged. This targeted `GetSecret` call does not enumerate/list other vault secrets. If loading fails, the application keeps that key empty, logs only a safe status category, and the worker remains gated by `Ready`. The VM's App Service/IIS settings do not apply to this host.

After an approved deployment window, configure the systemd service without placing the credential in its environment:

```bash
sudo systemctl edit erp-app
```

Add this drop-in:

```ini
[Service]
Environment="AZURE_KEY_VAULT_NAME=vertexerp-msg91-kv"
Environment="Msg91WhatsApp__SendingEnabled=false"
```

Do not add `Msg91WhatsApp__AuthKey`; the only production source is Key Vault. Keep the existing secret value managed in `vertexerp-msg91-kv` under `Msg91-AuthKey`. The VM identity needs permission to get that secret; the already confirmed `Key Vault Secrets User` assignment is the expected data role. Never print the secret in a shell command or log. Environment setting changes require a process restart to take effect; do not restart production as part of this change.

Before a future restart, capture deployment/version evidence (`systemctl cat erp-app`, `systemctl show erp-app -p FragmentPath -p ExecStart -p User -p Restart`, and the deployed DLL timestamp/version) and inspect the startup lines `MSG91 Key Vault ...` / `MSG91 sendingReady=...`. The startup diagnostic reports secret presence and safe readiness reason codes, never the key. Check outbound DNS/TCP 443 to `vertexerp-msg91-kv.vault.azure.net` and `api.msg91.com`; a non-message `curl -I --max-time 10 https://api.msg91.com/` can check basic HTTPS reachability but does not validate the POST endpoint or send a WhatsApp message. A successful build cannot establish which release or settings are currently live.

### Read-only production verification SQL

Run only after connecting to the confirmed production database with a read-only database role. These statements do not expose phone numbers or employee names and do not modify records:

```sql
BEGIN TRANSACTION READ ONLY;

SELECT "Status", "DeliveryStatus", count(*) AS "Count"
FROM "WhatsAppTaskNotifications"
GROUP BY "Status", "DeliveryStatus"
ORDER BY "Status", "DeliveryStatus";

SELECT "Id", "TaskId", "AssigneeId", "Status", "DeliveryStatus", "Attempts",
       "CreatedAtUtc", "NextAttemptAtUtc", "LastAttemptAtUtc", "HttpStatus",
       "ProviderMessageId", "ErrorCode", "DeliveryErrorCode"
FROM "WhatsAppTaskNotifications"
WHERE "Status" IN ('Pending', 'Retry', 'Processing', 'NeedsReview')
ORDER BY "CreatedAtUtc", "Id";

SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";

COMMIT;
```

`WhatsAppTaskQueue.ClaimAsync` claims due `Pending`/`Retry` rows using `FOR UPDATE SKIP LOCKED` and commits `Processing` plus an incremented attempt before sending. Stale `Processing` rows become `NeedsReview` after five minutes because the provider outcome may be uncertain; they are not auto-resent. A unique queue row per TaskId prevents duplicate task enqueue. Inspect all old due `Pending`/`Retry` rows and reconcile `NeedsReview` provider IDs before any approved test; do not clean them up by editing production records.

`Program.cs` now gates EF migration and development-user seeding to the Development environment. Production relies on separately reviewed/applied migrations. Review the migration history and confirm the queue and delivery tables/columns before deployment; no migration or production query was run from this workspace. The systemd worker runs inside the web process, so verify `Restart=`/service health and logs after an approved restart; unlike IIS, there is no app-pool idle timeout to configure.

### Future deployment and rollback (only after approval)

1. Build/publish the release locally and retain the resulting artifact:

   ```powershell
   dotnet publish "Vertex ERP/Vertex ERP.csproj" -c Release -o "publish/VertexERP" --no-restore
   ```

2. Before transferring, inspect `sudo systemctl cat erp-app` and `sudo systemctl show erp-app -p ExecStart -p User -p Restart` on the VM to confirm the unit still targets `/home/azureuser/erp-app/Vertex ERP.dll`. Review pending EF migrations and apply only an approved script through the database change process; Production startup skips `Database.Migrate()` and development-user seeding.
3. Create a protected code/config backup (including the existing appsettings and App_Data files), stage the artifact under `/tmp/vertexerp-release`, and copy the published application files to `/home/azureuser/erp-app`. From PowerShell, after approval:

   ```powershell
   ssh azureuser@vertexerp.net "mkdir -p /tmp/vertexerp-release"
   scp -r "publish/VertexERP/." azureuser@vertexerp.net:/tmp/vertexerp-release/
   ssh azureuser@vertexerp.net 'sudo install -d -m 700 /home/azureuser/backups && sudo tar -C /home/azureuser -czf /home/azureuser/backups/erp-app-before-msg91-$(date -u +%Y%m%dT%H%M%SZ).tar.gz erp-app'
   ssh azureuser@vertexerp.net "sudo rsync -a --exclude=/appsettings.json --exclude=/App_Data/ /tmp/vertexerp-release/ /home/azureuser/erp-app/"
   ```

   Preserve the server's `appsettings.json` and `App_Data`; do not transfer credentials in the artifact. Configure the systemd drop-in above and verify the Key Vault role before restart.
4. After explicit approval, run `sudo systemctl daemon-reload && sudo systemctl restart erp-app`, then check `sudo systemctl status erp-app --no-pager` and `sudo journalctl -u erp-app --since "10 minutes ago" --no-pager`. Confirm the live version, Key Vault status, `sendingReady=false` while sending is disabled, and an HTTPS health/page response. Do not infer delivery from process health.
5. Roll back by stopping the service, extracting the saved application tree to a temporary directory, restoring its files over the release path while preserving current `appsettings.json` and `App_Data`, then starting the service and checking the same health/log evidence. For example, replace `<backup-file>` with the exact archive name printed by the previous step:

   ```bash
   sudo systemctl stop erp-app
   mkdir -p /tmp/erp-app-rollback
   sudo tar -xzf "/home/azureuser/backups/<backup-file>" -C /tmp/erp-app-rollback
   sudo rsync -a --exclude=/appsettings.json --exclude=/App_Data/ /tmp/erp-app-rollback/erp-app/ /home/azureuser/erp-app/
   sudo systemctl start erp-app
   sudo systemctl status erp-app --no-pager
   ```

   Keep the backup until the release is accepted. If the release applied EF migrations, application rollback alone does not reverse the database schema; use a separately reviewed database recovery plan.

The VM's actual systemd unit contents, current release directory layout, deployed version and migration history were not available to this local code change. Verify those facts in step 2 before choosing the file-copy commands; no service stop, restart, deployment or database change was performed here.

### Controlled one-employee production test (after explicit approval)

1. Confirm the deployed build/version, Key Vault startup status, database migration state, worker health, outbound HTTPS, approved template, and one selected employee's registered WhatsApp eligibility through authorized admin screens. Never put the full number in logs.
2. Run the read-only SQL above. Reconcile/hold any unintended old due rows and every `NeedsReview` item before testing.
3. Confirm the four approved parameters remain Employee Name, Task Title, Assigned By, Deadline in that order. The application-side payload checks cover the exact body mappings, but only MSG91 can confirm template/account eligibility.
4. Keep the global worker paused while pending rows are reviewed. Only after approval, temporarily set `Msg91WhatsApp__SendingEnabled=true` in the isolated test environment; do not enable it for production unless production is explicitly selected and approved.
5. Assign one new test task to the authorized employee and record its notification event ID. Verify one queue row advances, inspect the sanitized response/HTTP status/provider request ID and UTC submission timestamp, then disable sending immediately.
6. Treat `Accepted` as provider acceptance only. Verify final delivery in MSG91 delivery reporting or the authenticated delivery callback. Do not repeat an uncertain submission until its provider ID is reconciled.

No live WhatsApp send, production deployment, process restart or production database operation is performed by this change.

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

The source contains migrations, but their applied status is environment-specific and must be checked in `__EFMigrationsHistory`. Production startup does not apply migrations; use a reviewed migration script and separate approval for any schema change.

## Tests

Run `dotnet run --project tests/Msg91WhatsAppChecks/Msg91WhatsAppChecks.csproj -c EmployeeCategory --no-restore -p:UseAppHost=false`.
HTTP uses a fake HttpMessageHandler; database-save probes suppress actual database writes. Migration SQL tests generate PostgreSQL SQL offline. Tests cover exact JSON/order, switches, retry limits, duplicate prevention, task-save behavior, webhook auth/parsing/deduplication, monotonic delivery transitions, UTC dates and schema generation. They do not establish live delivery, callback registration or actual PostgreSQL concurrency behavior.
