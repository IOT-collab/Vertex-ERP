# WDMS enrollment mapping

WDMS labels `emp_code` as Employee ID. In this ERP it is the **Enroll Code**, stored as text with leading zeroes preserved. WDMS `id` is an API record identifier and is never used to identify an ERP employee. The ERP's `EmployeeCode` remains independent.

## Data flow

1. Each configured attendance sync reads all pages of `personnel/api/employees/`.
2. `BiometricEnrollments` stores the enroll code, reference name, WDMS name, linked ERP employee, review status and last-seen UTC timestamp.
3. The 64 code/name pairs supplied in `ERP employee updated sheet (1).xlsx` are seeded once by `AddWdmsEnrollments`. Their reference names take precedence; the current WDMS spelling is retained separately. Missing entries are not deleted when absent from the WDMS directory.
4. A clear existing enrollment/name match reuses its ERP profile. An existing pending biometric placeholder receives the real name. A new unique source identity creates a named pending ERP profile with a separate generated ERP code. No login credentials, payroll or bank details are created.
5. Transactions from `iclock/api/transactions/` retain `emp_code` as `AttendanceLog.DeviceUserId`. Reconciliation links them through the verified enrollment and the device mapping. It does not match punches by name or sheet row position.
6. Repeated syncs reuse identities and transaction hashes. HR edits to completed profiles, inactive mappings and deletion exclusions are preserved.

## Conflicts and HR review

An existing completed profile with a different name, multiple employees using one code, or an employee already linked to a different identity enters **Needs review**. Unassigned punches remain unassigned until verified. Previously assigned attendance is never rewritten by this feature.

Open **Employee Directory → WDMS Enroll Codes / Review**. Compare the sheet name, WDMS name and existing ERP link. Select the verified employee or explicitly create a new pending profile, tick the verification checkbox, then save. A competing code on the same device must be resolved first. Disabled links and HR-deleted enrollments are not reactivated by this action. Verification is recorded in the audit log.

Employee Directory displays the separate Enroll Code below the ERP Employee ID. Search and onboarding support enroll codes even before the first punch or device mapping exists.

Attendance Management and its daily/monthly exports display the verified numeric Enroll Code in `Emp ID`, not `EmployeeCode`. They use the WDMS name exactly as received (reference name only when a WDMS name is not yet available), including on absent rows. Leading zeroes are retained in the application and monthly workbook. IDs remain blank for missing, nonnumeric, unresolved or multiple enrollment links; ERP prefixes are never stripped to manufacture a WDMS ID. Employee records and historical attendance ownership are unchanged. Attendance search uses the displayed enroll code and source name. CSV readers such as Excel may infer numeric types; import the daily CSV's Emp ID column as text when leading zeroes matter.

## Deployment and validation

Read-only verification on 9 October 2026 confirmed that all 64 spreadsheet code/name pairs exactly match the migration. The live WDMS directory returned 60 employees, all matching spreadsheet enroll codes. Codes 16 (Gaurav Anand), 133 (Shreya Raj), 138 (Karuna Rai), and 141 (Priya Srivastava) were absent from that directory snapshot and remain in the reference registry. This is not evidence that the employees should be deleted.

The directory is refreshed on each attendance polling cycle (`PollIntervalSeconds`, default 60 seconds after a cycle completes). A new unique code and valid name can create a pending profile before its first punch. HR completes employment, salary, bank and login details separately. Review saves run through the same retry-enabled database execution strategy used in production.

Deploy the ERP and any separately running BiometricReceiver together: both must use the updated importer and reconciliation code. Apply the included migrations through normal application startup before using the new pages. An older receiver must not keep importing concurrently after this mapping change.

Enrollment checks use a temporary isolated PostgreSQL schema and a WDMS-shaped HTTP fixture. They exercise the sheet migration, reference-name precedence, existing-profile reuse, new enrollments before punches, leading zeroes, conflict review, exclusions, preservation of historical assignments, paginated directory import and duplicate-punch prevention. The schema is removed after the checks.

Run from the repository root:

```powershell
dotnet run --project tests/HrManagementChecks/HrManagementChecks.csproj -c EnrollmentCheck -- --enrollments "Vertex ERP/appsettings.json"
```

The settings file supplies only the test connection. Existing application records are not modified by these tests.

Attendance also accepts existing active numeric device links when no registry identity exists for that employee or code. Repeated links to the same code on multiple devices are counted once. Competing owners, multiple codes, disabled links and registry conflicts are not used as fallbacks. Imported sheet names take precedence over WDMS spelling.
