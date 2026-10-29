# Management notification inbox

`/Notifications` provides a common inbox for HR, Admin, Manager, Employee and legacy User accounts. The shared top bar, dashboard bell and manager top bar link to it. Badges refresh every 30 seconds while the page is visible. A failed refresh preserves navigation and does not display a false count.

Sources: leave (including half day/short leave), queries, expense claims and their approval-stage changes, updated tasks/work reports, written feedback, feedback form responses/revisions, employee asset acceptance/rejection, uploaded employee documents and bank change requests. No manual attendance activity is included. Existing source records are visible on first use; unread does not mean still awaiting approval. The inbox shows the latest activity/state of each source, not an immutable event history. A newer source update is a new unread event.

Recipient scope follows existing screens. Managers see assigned leave, their team queries/expenses and updates to tasks they manage, excluding self. HR/Admin see management submissions; HR expenses start after the manager stage. Confidential feedback/form uploads/assets/documents remain HR/Admin only. Bank changes remain HR only, matching the existing bank review endpoint. No approval privileges or file access permissions are granted by a notification.

Opening a notification is an antiforgery-protected POST which revalidates the key against the current account inbox and redirects only to a server-generated local URL. Notification text is HTML encoded. Read receipts are unique per user/event; marking one account read does not affect another. Bulk marking uses a single idempotent PostgreSQL INSERT / ON CONFLICT operation. New reply revisions and workflow updates get new keys.

Migration: `20261028053211_UnifiedNotificationInbox` adds only NotificationReadReceipts, referencing Users. Startup's existing migration step applies it after publishing/restarting the application. No live database was changed during implementation.

Validation: `dotnet run --project tests/HrManagementChecks/HrManagementChecks.csproj -c EmployeeCategory --no-restore -p:UseAppHost=false -- --notifications`. Tests cover recipient boundaries, HR expense stage, private scope, PostgreSQL translation, migration FK/index, unauthorized access, POST/antiforgery and full Razor rendering with read/unread state and HTML escaping. Live logged-in delivery and PostgreSQL read writes require deployment verification.

## Personal notifications

Employee/User accounts have only personal scope: new assigned tasks, their own leave decisions (manager and final), query replies, expense review/final responses, asset allocations, published HR forms, generated/updated salary slips, reviewed bank change requests and expense advances. A flagged recipient also sees expense claims forwarded specifically to them. HR and Manager accounts with an active employee profile receive these personal notifications alongside their management inbox. Admin remains management-only because employee self-service destinations do not allow the Admin role. One account cannot see another employee's personal notifications or mark their events read.

Both shared layouts include exactly one bell in their header, with the same polling script and inbox. The main dashboard does not duplicate it. The legacy /Main/EmployeeNotifications route redirects to the common inbox. An outdated notification key (source updated/deleted between page load and click) returns to the latest inbox with a message instead of a broken page. No new schema migration is needed for this extension; the existing read-receipt migration remains required.

## Root URL routing fix

The application default route uses action `Start`, not `Index`. A plain `/Notifications` link therefore previously failed to match the inbox. `ErpRouteConfiguration.MapErpRoutes` now registers an explicit `Notifications/{action=Index}/{id?}` route before the existing routes. Program and HTTP regression tests use this same route configuration. Local real HTTP checks verify `/Notifications`, `/Notifications/Index` and `/Notifications/Count`: anonymous requests reach the authentication challenge; authenticated HR/Admin/Manager/Employee/User test identities without an account ID reach the controller account guard, without connecting to a database. This verifies routing and authorization boundaries, not live account data delivery. Publish the updated application assembly and restart the hosted app for the fix to take effect on vertexerp.net.

## Leave sequencing

Leave notifications now match the manager-first policy: initial employee applications go to the assigned reporting manager; HR receives the approval queue/notification after either manager decision. Admin retains view-only visibility. Personal decision updates remain visible to the employee. The internal HR/Admin stage is displayed as HR; only HR can finalize leave. See Leave_Approval_Flow.md.
