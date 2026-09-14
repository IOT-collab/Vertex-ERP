namespace VertexERP.Models;

public sealed class AuditLogList
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public int Page { get; init; }
    public int Pages { get; init; }
    public int Total { get; init; }
    public long MaxId { get; init; }
    public IReadOnlyList<AuditLog> Logs { get; init; } = [];
}
