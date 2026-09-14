namespace VertexERP.Models;

public sealed record TaskHealthSummary(int CompletedTasks, int OpenTasks, int OverdueTasks, int Progress)
{
    public static TaskHealthSummary From(IEnumerable<WorkTask> tasks, DateOnly today)
    {
        var rows = tasks.ToList();
        static bool Closed(WorkTask task) => task.Status.Equals("Completed", StringComparison.OrdinalIgnoreCase) || task.Status.Equals("Done", StringComparison.OrdinalIgnoreCase);
        var closed = rows.Count(Closed);
        var progress = rows.Count == 0 ? 0 : (int)Math.Round(rows.Average(task => Closed(task) ? 100 : task.Status == "In Review" ? 75 : task.Status == "In Progress" ? 40 : 0));
        return new(closed, rows.Count - closed, rows.Count(task => !Closed(task) && task.DueDate < today), progress);
    }
}
