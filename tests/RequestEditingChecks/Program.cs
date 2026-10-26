using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VertexERP.Models;
using VertexERP.Services;
using Vertex_ERP.Controllers;

void Check(bool ok, string label) { if (!ok) throw new Exception(label); Console.WriteLine("PASS: " + label); }
var employee = new Employee { Id = 7, ReportingManagerId = 10 };
var date = new DateOnly(2026, 10, 21);
var applied = DateTime.UtcNow.AddDays(-1);
var leave = new LeaveRequest { Id = 2, EmployeeId = 7, Status = "Pending", AppliedAtUtc = applied, ManagerDecision = "Approved", ManagerDecidedByUserId = 5, ManagerDecidedAtUtc = DateTime.UtcNow, ManagerDecidedByName = "Manager", ManagerDecisionNote = "Old dates", ApprovalLevel = "HR/Admin" };
Check(!EmployeeRequestChanges.CanChangeLeave(leave, 8), "Another employee cannot change a leave");
EmployeeRequestChanges.UpdateLeave(leave, employee, false, " cl ", date, date, " Corrected reason ", true);
Check(leave.EmployeeId == 7 && leave.Id == 2 && leave.AppliedAtUtc == applied, "Edit preserves ownership, record ID and application history");
Check(leave.LeaveType == "CL" && leave.Reason == "Corrected reason" && leave.IsHalfDay, "Corrected leave fields saved");
Check(leave.ApprovalLevel == "Manager" && leave.AssignedApproverEmployeeId == 10 && leave.ManagerDecision == null && leave.ManagerDecidedByUserId == null && leave.ManagerDecidedAtUtc == null && leave.ManagerDecidedByName == null && leave.ManagerDecisionNote == null, "Old manager approval cannot authorize edited leave");
EmployeeRequestChanges.UpdateLeave(leave, employee, true, "EL", date, date.AddDays(1), "Senior application", false);
Check(leave.ApprovalLevel == "HR/Admin" && leave.AssignedApproverEmployeeId == null, "Manager and HR applications return to final approval");
foreach (var status in new[] { "Approved", "Rejected", "Cancelled" }) { leave.Status = status; Check(!EmployeeRequestChanges.CanChangeLeave(leave, 7), status + " leave protected"); }
leave.Status = "Pending";
try { EmployeeRequestChanges.UpdateLeave(leave, employee, false, "CL", date, date.AddDays(1), "Half day", true); throw new Exception("Invalid half day accepted"); } catch (InvalidOperationException) { }
Check(!LeaveApprovalWorkflow.ValidApplication("CL", date, date, new string('x', 501), false), "Leave reason length enforced");
var ticket = new QueryTicket { EmployeeId = 7, Status = "Open" };
Check(EmployeeRequestChanges.CanEditQuery(ticket, 7) && !EmployeeRequestChanges.CanEditQuery(ticket, 8), "Only owner can edit open query");
ticket.Resolution = "HR reply"; Check(!EmployeeRequestChanges.CanEditQuery(ticket, 7), "Replied query content protected");
ticket.Resolution = null; ticket.Status = "Closed"; Check(!EmployeeRequestChanges.CanEditQuery(ticket, 7), "Closed query protected");
Check(ModuleCatalog.ForRoute("MyRequests", "EditLeave") == "leave", "Module deactivation also blocks new actions");
Check(Attribute.IsDefined(typeof(MyRequestsController), typeof(AuthorizeAttribute)), "Authentication required");
foreach (var action in typeof(MyRequestsController).GetMethods().Where(m => Attribute.IsDefined(m, typeof(HttpPostAttribute))))
    Check(Attribute.IsDefined(action, typeof(ValidateAntiForgeryTokenAttribute)), action.Name + " requires CSRF validation");
