# Yearly leave calculator

HR/Admin: Employee Leave Balances → select year → Calculate Leave on the employee row.
Enter January–December working days, additional manual usage, and EW earnings.
EL = floor(annual working days / 20); CL and ML = floor(annual working days / 40).
EL/CL/ML/EW opening balances are reviewed snapshots of the preceding year's available balance.
Repeated saves replace these snapshots; they never accumulate them again. Refresh is explicit
when the previous year changes. Negative previous closing balances do not become leave credits.

Approved requests are read live, grouped by employee/category, clipped to the selected year,
and deduplicated by calendar date. A half-day request requires the same start/end date.
Pending and rejected requests do not consume leave. HR/Admin can cancel an approved request
from Leave Status with a reason. That status change immediately restores its usage on reload.
No separate balance-write side effect is needed during approval, so a status cannot commit
without its associated balance effect. Calculator saves cannot overwrite approved usage.

Stored ManualLeaveBalance.UsedDays now means **additional HR usage**. Displayed total used
is that value plus current approved usage. Save Calculation persists all four category totals
and monthly entries atomically. Revision checks reject stale calculator/manual edits.
Manual Fill/Edit remains for years without a saved calculator; its inputs are additional usage
and total entitlement. Calculator-managed years must be edited through the calculator.
Calculation changes, monthly values and actor details are recorded through existing audit logs.

## Existing data reconciliation

Migration AddYearlyLeaveCalculator preserves old manually entered UsedDays in LegacyUsedDays.
It assumes existing approved requests were included in that old total and subtracts their
distinct dates once to obtain additional usage (minimum zero). Total entitlement is unchanged.
If approvals exceed old manual usage, approvals take precedence and available balance decreases.
HR sees the old total and must review additional usage, especially where old entries excluded
approved requests. New calculator conversion shows existing balances and requires review before
replacement. This cannot infer the provenance of historic manually entered figures.

The migration creates LeaveCalculations and adds LeaveRequest.IsHalfDay (existing requests are
full days). Deploy with the migration before serving the updated code. No production database
was changed as part of implementation. Existing app startup migration policy applies.

Validation: `dotnet run --project tests/HrManagementChecks/HrManagementChecks.csproj -c LeaveCalculator -- --leave-calculator`.
Checks cover earning boundaries, annual accumulation, category/year/employee isolation,
half days, pending/rejected/cancelled requests, idempotency, access control and EF constraints.

Migration AddClMlLeaveCarryForward adds CL and ML opening balances, defaulting existing calculations to zero. Available = opening + earned - approved usage - additional usage for each category.

## Monthly extra paid facilities

Employee dashboard and My Leave Balance include Apply Half Day and Apply Short Leave,
plus a current-month summary of limit, approved usage, pending reservations and available quota.
Each employee gets 2 half-day occasions and 2 short-leave occasions per calendar month.
Half-day requests select 1st Half / 2nd Half; short leave selects a single date and start/end
times with a positive duration of at most 2 hours. Each short request uses one occasion,
regardless of duration. The request date determines the month (India time for today's default).
No carry forward is allowed. Changing the date refreshes the form's month-specific quota.

New half-day requests, including older form entry points, are stored as extra paid facilities.
Neither facility consumes EL/CL/ML/EW or contributes to salary LOP. Existing ordinary leave
records retain their previous accounting; AddMonthlyExtraLeaveFacilities defaults their
IsExtraFacility flag to false. Short leave does not mark an entire attendance day as leave.

Pending and approved requests reserve quota; rejection, cancellation and deletion release it.
Editing excludes the current pending request when checking quota, validates the destination
month and clears earlier approvals. Per-employee PostgreSQL transaction locks serialize
applications, edits, manager/final decisions, cancellations and deletions to protect the quota
and prevent stale decisions authorizing edited requests. Duplicate half-day sessions and
overlapping short-leave slots are rejected. Different half sessions and adjacent short slots
are allowed within the monthly limit.

Both manager approval and rejection forward facilities to HR for the final decision. Manager
and HR decisions remain visible in request history. Admin can view but cannot finalize/cancel
facility requests. Regular full-day leave approval behavior remains unchanged.

Validation: run HrManagementChecks with --extra-facility, --back-date-leave and
--leave-calculator; also run tests/RequestEditingChecks. Database migration applies on app startup.
