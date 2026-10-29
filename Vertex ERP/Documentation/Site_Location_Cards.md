# Site employee location cards

/Main/LocationTracking returns only active employees with field attendance in the selected India calendar date. Attendance membership is filtered with EXISTS before employee profiles are loaded. Both Field device communication mode and the server FieldVerificationMode marker are recognized. Normal office biometric records do not populate the site list. Manager scope is applied server-side to the assigned team before the employeeId selection; HR/Admin keep company-wide access. The page is no-store.

One employee card shows profile photo (initials fallback), name, employee code, department/designation, latest site and check-in/check-out times. The selected detail panel shows recorded site/GPS accuracy, map links and optional attendance selfies. Map coordinates are invariant-culture formatted. The latest recorded location is not presented as continuous live GPS tracking.

Cards are sorted by latest field activity. Multiple punches remain a single card; first check-in and latest check-out are shown. A later check-in updates on-site status and latest site instead of leaving an earlier checkout as the current state. Empty dates show a clear no-site-attendance message without unrelated employees.

The client polls every 15 seconds while visible, preserving date, selected employee, keyboard focus and sidebar scroll. New arrivals appear automatically; unchanged markup does not reload the map/images. Card selection loads authorized details without reloading the page. Network/session failures preserve existing content and display a retry message. Pending date changes are not overwritten. No database migration is required.

Validation: --site-location-cards (7 roster/data/query checks, 2 full Razor renders); node tests/site-location-refresh-checks.cjs (10 polling/update/failure-state checks); --manager-access (10 access checks). These are local/offline checks; a production logged-in site punch was not submitted for verification. Publish/restart the application to apply to a running deployment.
