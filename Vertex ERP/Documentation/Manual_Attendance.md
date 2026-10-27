# Attendance display and exports

Attendance screens, live summaries and reports use the neutral Punch source label for office
punches; Field Attendance remains separate. Day/month export remarks and employee details do
not include correction actor, reason, method or previous-time payloads. The monthly export
heading is Attendance Remarks. Effective timestamps, statuses, worked hours and report-only
late duration retain their existing calculation. Internal verification markers remain required
for endpoint resolution and import deduplication; they are not relabeled as biometric evidence.

# Current manual attendance behavior

KeepOnlyYuvrajManualAttendance restricts entry access to uniquely identified active Yuvraj Singh
and revokes Deepa Katariya and other operators. Permission remains linked to employee ID.
Direct marking stores only effective punch data. It creates no AttendanceRequest/history record,
no separate application audit entry, and no actor/reason/previous-time payload. Correction
metadata is excluded from display/export remarks. Existing historical records are retained;
this change does not delete prior logs or disable hosting/server diagnostic logging.
WorkCode, verification mode, received time and hash remain operational attendance data needed
to resolve corrected endpoints, avoid duplicate imports and produce attendance/Excel reports.

# Manual Attendance operators

Manual attendance entry, employee time lookup and punch correction are restricted by the
ManualAttendanceOperators policy to active linked employee accounts explicitly granted
Employee.CanManageManualAttendance. HR is denied even when a flag is present. Admin/Manager
roles alone do not grant entry rights. Admin can still review historic pending requests.
The sidebar, attendance page button, manager page button, correction form and direct URLs
use the same access service. Operators can select any active employee and save immediately.

Migration RestrictManualAttendanceOperators grants the flag once to unique active linked
accounts for Yuvraj Singh and Deepa Katariya, matching existing employee/account names or
verified WDMS links. Ambiguous or missing matches receive no access. Permission is stored by
employee identity, survives HR profile edits, and defaults to false for everyone else.
The migration must run on the deployed database before these permissions are active.
No role, password or ordinary employee self-service permission is changed.


HR/Admin: open Mark Manual Attendance, select department and employee, date, actual check-in,
actual check-out and a reason. Saving directly creates approved attendance; Manager entries
remain correction requests requiring Admin review. Correction history remains available via
the second navigation tab. Employee name/code appear in the employee dropdown.

HR/Admin can choose Only Check-In, Only Check-Out or Both. The form fetches the current
employee/date endpoints. Selected original AttendanceLog rows are updated in place; an endpoint
is inserted only when none exists. The unselected time is retained. Original deduplication hashes
are retained to prevent the same WDMS punch from being reimported. Selected times become the
authoritative endpoints even when intermediate punches exist. Old/new times, actor and reason
are recorded in payload and approved request history. The shared effective-punch resolver feeds
attendance screens, employee history, AI summaries, daily CSV and monthly Excel exports.
Manager requests retain their existing Admin approval workflow and validation.
A global PostgreSQL transaction lock serializes direct marks and approvals; the operations use
the configured retry strategy. Marked-by identity, timestamp and reason remain in attendance
remarks, employee history, daily CSV and monthly Excel detail/pivot exports.

Manually marked employees are included even without a WDMS enrollment. The rest of the daily
roster continues to come from WDMS. Present counts include late arrivals with actual check-in.
Working duration is the exact elapsed check-in/check-out interval, with no automatic break
deduction. Office default is 09:45-18:30 (8h45); exactly 09:45 is on time and later is late.
Early checkout is shown in remarks. Existing individually assigned custom shifts are retained.
Migration SetOfficeStart0945 changes only the database default for new records; existing assigned shifts are preserved.

Validation: tests/WdmsRosterChecks verifies missing punches, duplicates, biometric protection,
manual employees outside WDMS, employee history, exact duration, early exit and daily/monthly
exports. tests/AttendanceHoursChecks covers shift calculations and export regression checks.
The database-backed --manual-approval checks require a separately supplied test connection.

The 09:30 office shift allows arrivals through 09:45; later punches are late and late duration
is measured after 09:45 (09:50 is 5 minutes late). Other assigned shifts use their exact start
time, including 11:00 shifts. Late duration is shown in attendance and export remarks. Actual worked hours stay
based on punches; early exit and enabled overtime use that employee's assigned end/duration.
Manual attendance supports next-day checkout and after-midnight arrivals on assigned night shifts.
The form displays the selected employee's shift and previews overnight elapsed hours.
