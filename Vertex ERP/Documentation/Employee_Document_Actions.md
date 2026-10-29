# Employee document actions

Scope: HR/Admin employee document View, Update, Delete and supporting upload handling. Attendance/export behavior was not changed for this request.

View opens a readable stream before returning a response, using known extension-based content types and nosniff. PDF/images open inline; Word/unknown formats download. Missing, inaccessible or invalid stored file names redirect to the employee document list with a recovery message instead of returning a raw missing-file page. The record remains available for a replacement upload. Files missing from this server cannot be recovered from database metadata alone.

Update permits metadata-only changes when the original file exists. Missing original files require a replacement, clearly shown in the form. A replacement gets a private generated storage name; the old file is removed only after the database save succeeds. Expected storage/database-save failures return an actionable form message. Interrupted file copies remove their partial new file. File uploads retain the existing 3 MB limit and PDF/JPG/PNG/DOC/DOCX types, with content-header checks and bounded original filenames.

Delete saves record removal before file cleanup. Missing/already-deleted records and files are handled without a raw missing-record page; database failures keep the record and display a retry message. Cleanup failures are logged without exposing paths or crashing the document list. All paths are confined to App_Data/EmployeeDocuments. Existing HR/Admin authorization and POST/antiforgery protection remain intact. Action links preserve employee context; stale employee selections return to the directory.

Validation: --employee-documents runs 27 file/storage/security/action checks and three compiled Razor renders (normal update, missing-file replacement, repository actions). No live database or logged-in server action was exercised. No database schema migration is required for this change. The updated application must be published/restarted to use the changes on a running deployment.
