using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using VertexERP.Models;

namespace VertexERP.Services;

public static class AuditLogFactory
{
    public static AuditLog? Create(EntityEntry entry, ClaimsPrincipal? user)
    {
        if (entry.Entity is AuditLog or PasswordResetToken or EmployeeNotificationDismissal
            || entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)) return null;
        var type = entry.Metadata.ClrType.Name;
        var action = entry.State switch { EntityState.Added => "added", EntityState.Deleted => "deleted", _ => "updated" };
        // Never copy arbitrary field values: records can contain credentials, bank details or documents.
        var detail = entry.IsKeySet
            ? $"{type} #{string.Join(",", entry.Properties.Where(p => p.Metadata.IsPrimaryKey()).Select(p => p.CurrentValue))}"
            : $"New {type}";
        if (entry.Entity is ModuleState module)
        {
            action = module.IsActive ? "activated" : "deactivated";
            detail = $"Module: {module.Id} · {(module.IsActive ? "Active" : "Inactive")}";
        }
        else if (entry.Entity is WorkTask task)
        {
            detail = $"Task: {task.Title} · Assigned to employee #{task.AssigneeId} · Manager #{task.ManagerId}";
            action = entry.State == EntityState.Added ? "assigned" : action;
            var status = entry.Property(nameof(WorkTask.Status));
            if (entry.State == EntityState.Modified && status.IsModified && !Equals(status.OriginalValue, status.CurrentValue))
            {
                action = "status changed";
                detail += $" · {status.OriginalValue} → {status.CurrentValue}";
            }
            else detail += $" · Status: {task.Status}";
            var assignee = entry.Property(nameof(WorkTask.AssigneeId));
            if (entry.State == EntityState.Modified && assignee.IsModified && !Equals(assignee.OriginalValue, assignee.CurrentValue))
                detail += $" · Reassigned: employee #{assignee.OriginalValue} → #{assignee.CurrentValue}";
        }
        else if (entry.State == EntityState.Modified)
        {
            detail += " · Fields: " + string.Join(", ", entry.Properties.Where(p => p.IsModified).Select(p => p.Metadata.Name));
        }
        return new AuditLog
        {
            ActorId = user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
            ActorName = user?.Identity?.Name ?? "System",
            ActorRole = user?.FindFirstValue(ClaimTypes.Role) ?? "System",
            EntityType = type, Action = $"{(entry.Entity is WorkTask ? "Task" : type)} {action}", Detail = detail
        };
    }
}
