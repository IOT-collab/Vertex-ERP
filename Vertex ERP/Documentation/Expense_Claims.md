# Expense claim review

Employee -> assigned reporting manager -> HR -> explicitly forwarded to Yuvraj -> final response to the original employee.
Manager approve/reject decisions both route to HR for final review; the original manager
decision stays visible. HR approval/rejection is stored separately. Only after HR review can
HR/Admin forward the claim. Yuvraj can then approve/reject with a mandatory reason and
PDF/JPG/PNG attachment up to 10 MB. The completed claim shows the response, timestamp and
protected open/download links to the original employee. Manager and HR decisions remain
unchanged. Inbox history retains completed responses; final totals use Yuvraj's decision. Existing advance payment and employee receipt confirmation flows remain available.

The recipient is a uniquely matched active Yuvraj login, persisted by employee ID using
CanReceiveExpenseClaims. It is independent of manual attendance access. Employees see their
own claims; managers see assigned claims; HR sees claims after manager review; recipients
see only claims explicitly forwarded to them. Receipt permissions follow the same ownership
and assignment rules. Self approval, early forwarding and repeated/concurrent decisions are
blocked. WorkflowStage is an EF concurrency token; review forms require POST and antiforgery.

PDF/JPG/PNG receipts are limited to 10 MB, checked by MIME/extension and file header, stored
with random filenames outside wwwroot and served through an authorized endpoint. Failed
saves clean up uploaded files. The redesigned form includes preview, reviewer, status and
separate manager/HR decisions with light/dark theme variables and responsive layout.

The cross-controller Expense view explicitly resolves ~/Views/Hr/_ExpenseAdvances.cshtml;
relative partial lookup previously depended on the calling controller's view directory.
The migration preserves historical final decisions and assigns pending claims to their
existing manager/HR lane; receipt paths are not rewritten. Deploy code and run startup
migrations before serving these views. No production database was used during checks.

Validation: HrManagementChecks --expense-workflow (workflow guards, migration SQL and four
full Razor page renders); --expense-advances (receipt signatures and existing payments).

Recipient responses use their own private App_Data/ExpenseResponses folder and metadata.
Attachment authorization is shared with the claim owner/assigned reviewers. Unrelated users,
revoked recipients, self-review and repeated final responses are denied. Concurrent responses
are protected by WorkflowStage; a failed save removes the uploaded response file. The response
migration copies existing HR outcomes into HrDecision before final decisions can replace Status.
