# Module controls

Admin Panel stores global module access in PostgreSQL `ModuleStates`. The startup migration seeds all existing modules active. Restart the application after building to load the new code and apply the migration.

Only Admin can submit the anti-forgery-protected POST. A transaction saves the state and audit entry together. A PostgreSQL transaction advisory lock serializes module changes across instances; revision numbers reject stale forms. Deactivation never removes business records.

Payroll requires HR, Attendance and Leave. Disable Payroll before disabling those modules. Enable the dependencies before enabling Payroll.

The global resource filter blocks mapped MVC pages, mutations and exports before controller execution. The anchor tag helper removes disabled module links on the next page render. Already open pages are not pushed updates, but subsequent requests are checked. Add new module routes to ModuleCatalog and new module rows through a migration.

Login, account administration, Admin Panel and audit history remain available for recovery. Biometric ingestion and background collection intentionally continue to preserve device punches; user attendance pages and device administration are disabled. Shared dashboard summaries are not erased by module deactivation.

Verification: `dotnet run --project tests/ModuleControlChecks/ModuleControlChecks.csproj --configuration Release` creates an isolated temporary PostgreSQL database using the configured server, applies migrations, verifies state transitions, dependency rules, route blocking, link hiding, audit attribution, data retention and stale revisions, then drops only that temporary database. The database account needs temporary database creation permission.
