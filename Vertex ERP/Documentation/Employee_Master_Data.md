# Employee master data

HR-completed Employee records are authoritative for current name, department, designation,
category, contact details and assigned shift. Directory and attendance use WDMS names only
for pending imports or unlinked entries. WDMS enrollment codes remain stable linking keys;
HR name changes never rewrite device identities or break existing verified links.

SaveChanges synchronizes department renames to linked employee labels and exact-name legacy
records lacking a department ID, resolves changed assignments to their current department
master and synchronizes changed employee names to linked login accounts. Both synchronous
and asynchronous save paths share this logic. Designation and other profile fields are read
from current Employee records by self-service, manager/team screens and report generation.
Cookie validation refreshes names from the linked employee on each request, without logout.
Authenticated dynamic responses disable browser/proxy caching. Refresh/open a page after saving
to see current details. Existing downloaded exports and issued documents are historical files;
new downloads use current master data.

Migration SynchronizeEmployeeMasterLabels repairs existing department/account label copies.
No device names, biometric identity links, financial snapshots or issued documents are rewritten.
Validation: AttendanceHoursChecks covers edited names/departments, search/filter and daily/monthly
exports; HrManagementChecks --employee-master covers name precedence and migration SQL.
