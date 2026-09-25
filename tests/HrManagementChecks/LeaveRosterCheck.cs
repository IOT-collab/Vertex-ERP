using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VertexERP.Data;

static class LeaveRosterCheck
{
    public static async Task Run(string settingsPath)
    {
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settingsPath)).Build();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(config.GetConnectionString("DefaultConnection")).Options);
        var employees = await db.Employees.AsNoTracking().OrderBy(e => e.FullName)
            .Select(e => new { e.Id, e.EmployeeCode, e.FullName, e.Department, e.IsActive }).ToListAsync();
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(employees));
    }
}
