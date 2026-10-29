# Employee status and attendance

Current-day attendance, dashboard status lists and counts exclude linked inactive employees, including corrected punches. Present/Absent/Late/Leave and Blue/White Collar breakdowns all use the same filtered rows. Re-activating a linked WDMS employee restores their current attendance participation.

Past-date attendance and day/month exports preserve currently inactive employees only on dates with real punches. They do not generate absent, leave or holiday rows for an inactive employee without punches. Range exports also preserve inactive punches when the former WDMS directory entry is missing, using the employee relationship on saved punch records. Original logs are neither changed nor deleted. Individual history uses the same punch-only rule for currently inactive employees. Current-day export intentionally retains actual punches even when that employee has just been deactivated, while live counts exclude them.

Status is the current Employee.IsActive value; this change does not introduce an employment-status timeline or infer historical active periods. Previously active absences for a currently inactive employee are not synthesized in punch-only reports. No database migration is required.

The master dashboard cards and today status lists use the existing visible-page 15-second refresh, as does the Attendance screen. A newly loaded page reads the current database state immediately; background refresh skips active form inputs and hidden tabs.

Validation: AttendanceHoursChecks covers inactive current counts/search/filter, corrected punches, category totals, active restoration, historical/day/month export preservation, no fabricated inactive absences, inactive individual history and missing-directory history, plus existing shift, hours, grace, overtime, holiday and correction tests. These are offline repository/controller checks, not live-server verification.
