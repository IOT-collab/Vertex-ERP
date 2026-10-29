# Feedback forms and returned documents

HR/Admin publish a title, instructions and one to five PDF/JPG/PNG files. Each file is limited
to 10 MB and the combined request to 25 MB. Published forms appear for all active linked
employee accounts, including newly added employees. Employees download, complete/sign offline,
then upload their response files with an optional comment.

Employee identity comes from the active login; name, employee code, department and designation
are read from current employee profiles. HR/Admin see Submitted/Pending totals, a document
selector, department/search/status filters and all uploaded versions. Current active staff
plus any prior inactive submitters are included, preserving access to older submissions.
Employees and managers can see only their own response files; source documents are shared.

A unique (DocumentId, EmployeeId) index maintains one submission identity. Revision is a
concurrency token; resubmissions append versioned files instead of deleting previous uploads.
Concurrent/failed saves clean up newly uploaded files. Storage is under private App_Data,
uses generated filenames, validates MIME/extension/file signatures and requires authorization
for every open/download. No public static upload directory is used.

Feedback/Index redirects to the document workspace; Feedback/Inbox opens the latest form's
response tracker. Existing text feedback remains available under Feedback/Message and
Feedback/MessageInbox, with its original records intact. Publish, submit and downloads are
covered by existing login authorization; mutations require antiforgery tokens.

Deploy FeedbackDocumentExchange and allow startup migrations before using the feature.
Checks: HrManagementChecks --feedback-documents covers validation, access, schema, private
storage, cancellation cleanup and full HR/employee Razor renders. FeedbackChecks protects
the previous text-feedback flow. No live company database is used by these checks.
