# Manager reference dashboard

The manager workspace uses the supplied reference layout: persistent desktop navigation,
searchable header, live India greeting and date, five summary cards, team member directory,
workload donut, recent task/approval tabs and query tabs. The existing account light/dark
palette applies to all surfaces; greeting time changes only a subtle hero tint.

Data comes from the existing manager-scoped database queries. The current viewer's name and
photo are loaded from their linked employee. Own Tasks is separately scoped to the viewer.
Completed This Month uses the completed task's latest update timestamp (creation when absent),
as the current task schema has no separate completion timestamp. Workload period filters use
creation dates in India. Completed, active in-progress, pending and overdue segments are mutually
exclusive; overdue work is never counted twice. Zero datasets show genuine empty states.

The greeting uses Asia/Kolkata: Morning before 12:00, Afternoon 12:00-16:59, Evening from 17:00.
The open page rechecks every 30 seconds and on focus/visibility changes. At India midnight it
reloads to refresh day/month-dependent values. New pending notifications also refresh the page.
Task/approval and query tabs, global/team search, fullscreen and profile menus are functional.
Broken photo URLs fall back to employee initials. All write actions remain on the existing pages.

Existing authorization is retained: Manager sees View Team; HR/Admin see Add Employee.
An overloaded notification GET action was renamed ManagerNotificationUpdates so the count and
new-notification polling endpoints are unambiguous. Other layouts/navigation are unchanged.

Validation: HrManagementChecks --manager-dashboard, --manager-access; node tests/ThemeChecks/
manager-dashboard-clock-checks.cjs; existing theme/contrast/coverage checks and project build.

## Hero asset

Saved asset: wwwroot/images/manager/welcome-illustration.png
Generated with the built-in ImageGen tool, transparent PNG. Final prompt:

Create a production website hero illustration, isolated on a truly transparent background.
Wide composition, friendly polished 3D clay-render style like a premium business dashboard
illustration. A smiling Indian male manager in his thirties, dark neatly styled hair, wearing
a royal-blue suit and white shirt, seated behind an open silver laptop. A small green potted
plant on each side and two floating blue translucent dashboard panels behind him with simple
abstract bar chart and checklist shapes. Soft studio lighting, rounded forms, crisp refined
edges, blue-and-silver palette. Entire arrangement fully visible, centered, suitable for placement
inside a short pale-blue horizontal welcome banner, generous transparent margin. No text, no
letters, no logos, no watermark, no backdrop. Save as PNG.
