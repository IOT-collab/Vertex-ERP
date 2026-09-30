using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;
using VertexERP.Services;

void Check(bool result, string name) { if (!result) throw new Exception(name); Console.WriteLine("PASS: " + name); }
using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=localhost;Database=unused").Options);
var actor = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "42"), new Claim(ClaimTypes.Name, "Manager Test"), new Claim(ClaimTypes.Role, "Manager") }, "test"));
var task = new WorkTask { Id = 12, Title = "Prepare report", ManagerId = 1, AssigneeId = 2 };
db.Add(task);
var log = AuditLogFactory.Create(db.Entry(task), actor)!;
Check(log.Action == "Task assigned" && log.ActorId == "42" && log.Detail.Contains("employee #2"), "Task assignment records actor and assignee");
db.Entry(task).State = EntityState.Unchanged;
task.Status = "In Progress";
db.ChangeTracker.DetectChanges();
log = AuditLogFactory.Create(db.Entry(task), actor)!;
Check(log.Action == "Task status changed" && log.Detail.Contains("To Do → In Progress"), "Status history preserves old and new values");
db.Entry(task).State = EntityState.Unchanged;
task.AssigneeId = 3;
db.ChangeTracker.DetectChanges();
Check(AuditLogFactory.Create(db.Entry(task), actor)!.Detail.Contains("employee #2 → #3"), "Reassignment identifies both assignees");
db.Entry(task).State = EntityState.Unchanged;
Check(AuditLogFactory.Create(db.Entry(task), actor) == null, "Reads do not create activity");
var audit = new AuditLog(); db.Add(audit);
Check(AuditLogFactory.Create(db.Entry(audit), actor) == null, "Audit entries do not recursively audit themselves");
db.Entry(task).State = EntityState.Deleted;
Check(AuditLogFactory.Create(db.Entry(task), null)!.ActorName == "System", "Background changes have a system actor");
var access = (AuthorizeAttribute)Attribute.GetCustomAttribute(typeof(AuditController), typeof(AuthorizeAttribute))!;
Check(access.Roles == "Admin", "Audit page and live endpoint require Admin");
Check(db.Model.FindEntityType(typeof(AuditLog)) != null, "Audit history is included in database model");
var auditController = new AuditController(db);
Check(await auditController.Delete(null, null, null, 0, default) is Microsoft.AspNetCore.Mvc.BadRequestObjectResult, "Unbounded bulk deletion is rejected");
Check(await auditController.Delete(null, new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 14), 1, default) is Microsoft.AspNetCore.Mvc.BadRequestObjectResult, "Reversed deletion dates are rejected");
Check(await auditController.Download(new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 14), default) is Microsoft.AspNetCore.Mvc.BadRequestObjectResult, "PDF rejects reversed dates");
var deleteMethod = typeof(AuditController).GetMethod("Delete")!;
Check(Attribute.IsDefined(deleteMethod, typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute)) && Attribute.IsDefined(deleteMethod, typeof(Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute)), "Deletion requires POST and anti-forgery validation");
var deleteAllMethod = typeof(AuditController).GetMethod("DeleteAll")!;
Check(Attribute.IsDefined(deleteAllMethod, typeof(Microsoft.AspNetCore.Mvc.HttpPostAttribute)) && Attribute.IsDefined(deleteAllMethod, typeof(Microsoft.AspNetCore.Mvc.ValidateAntiForgeryTokenAttribute)), "Delete All requires POST and anti-forgery validation");

// Every business entity participates in the central add/update/delete audit path.
foreach (var entityType in db.Model.GetEntityTypes().Where(type => type.ClrType != typeof(AuditLog) && type.ClrType != typeof(PasswordResetToken)))
{
    db.ChangeTracker.Clear();
    var entity = Activator.CreateInstance(entityType.ClrType)!;
    var entry = db.Entry(entity);
    foreach (var key in entityType.FindPrimaryKey()!.Properties)
        entry.Property(key.Name).CurrentValue = key.ClrType == typeof(int) ? (object)901
            : key.ClrType == typeof(long) ? 901L : key.ClrType == typeof(string) ? "audit-check" : Activator.CreateInstance(key.ClrType);
    foreach (var state in new[] { EntityState.Added, EntityState.Modified, EntityState.Deleted })
    {
        entry.State = state;
        var result = AuditLogFactory.Create(entry, actor);
        Check(result != null && result.ActorId == "42" && result.EntityType == entityType.ClrType.Name,
            $"{entityType.ClrType.Name}: {state} records actor and entity");
    }
}
db.ChangeTracker.Clear();
var account = new AppUser { Id = 90, Username = "audit-test", PasswordHash = "SECRET-HASH-DO-NOT-LOG" };
db.Add(account);
Check(!AuditLogFactory.Create(db.Entry(account), actor)!.Detail.Contains(account.PasswordHash), "Account audit never copies password hashes");
var bulkEvent = AuditLogFactory.CreateEvent("ProjectEmployee", "Project employee assigned", "Project #7 · Employee #9", actor);
Check(bulkEvent.ActorId == "42" && bulkEvent.ActorRole == "Manager" && bulkEvent.Detail.Contains("Project #7"), "Bulk changes retain actor and affected record");
var employeeRecord = new Employee { EmployeeCode = "EMP-9", FullName = "Audit Employee" };
db.Add(employeeRecord);
Check(AuditLogFactory.Create(db.Entry(employeeRecord), actor)!.Detail.Contains("EMP-9"), "New employee audit identifies the employee before database ID generation");
