using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Npgsql;
using VertexERP.Controllers;
using VertexERP.Data;
using VertexERP.Models;

static class AssetAcceptanceChecks
{
    static void Check(bool ok, string name)
    {
        if (!ok) throw new Exception(name);
        Console.WriteLine("PASS " + name);
    }

    public static async Task Run(string settingsPath)
    {
        foreach (var quantity in new[] { -1, 0, 1, 5 })
            Check(Validator.TryValidateProperty(quantity,
                new ValidationContext(new IssueAssetViewModel()) { MemberName = "Quantity" },
                new List<ValidationResult>()) == (quantity > 0), "Quantity validation: " + quantity);
        var config = new ConfigurationBuilder().AddJsonFile(Path.GetFullPath(settingsPath)).Build();
        var connection = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"));
        var schema = "asset_check_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync();
        await new NpgsqlCommand($"CREATE SCHEMA {schema}", admin).ExecuteNonQueryAsync();
        try
        {
            connection.SearchPath = schema;
            await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(connection.ConnectionString).Options);
            await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            var owner = new Employee { EmployeeCode = "ASSET-OWNER", FirstName = "Asset", FullName = "Asset Owner", Email = "owner@example.invalid", PhoneNumber = "123", Department = "Test", Designation = "Test" };
            db.Employees.Add(owner);
            await db.SaveChangesAsync();
            var user = new AppUser { Username = "asset-owner", NormalizedUsername = "ASSET-OWNER", Role = "Employee", EmployeeId = owner.Id, FullName = owner.FullName };
            db.AppUsers.Add(user);
            var legacy = new EmployeeAsset { EmployeeId = owner.Id, AssetTag = "LEGACY", AssetName = "Laptop", Category = "Laptop", IssueDate = DateOnly.FromDateTime(DateTime.Today) };
            db.EmployeeAssets.Add(legacy);
            await db.SaveChangesAsync();
            // Recreate the old shape and verify migration preserves existing issued assets.
            await db.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_EmployeeAssets_AssetTag\"; ALTER TABLE \"EmployeeAssets\" DROP COLUMN \"Status\", DROP COLUMN \"Quantity\", DROP COLUMN \"RespondedAtUtc\"; CREATE UNIQUE INDEX \"IX_EmployeeAssets_AssetTag\" ON \"EmployeeAssets\" (\"AssetTag\");");
            var assembly = db.GetService<IMigrationsAssembly>();
            var migration = assembly.CreateMigration(assembly.Migrations.Single(m => m.Key.EndsWith("_AddAssetEmployeeAcceptance")).Value, db.Database.ProviderName!);
            foreach (var command in db.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
            db.ChangeTracker.Clear();
            var old = await db.EmployeeAssets.SingleAsync();
            Check(old.Status == "Issued" && old.Quantity == 1, "Migration keeps legacy assets issued with quantity 1");
            var pending = new EmployeeAsset { EmployeeId = owner.Id, AssetTag = "NEW", AssetName = "Uniform", Category = "Uniform", Quantity = 3, IssueDate = DateOnly.FromDateTime(DateTime.Today) };
            db.EmployeeAssets.Add(pending);
            await db.SaveChangesAsync();
            Check(pending.Status == "Pending", "New allocations start pending");
            MainController Controller(int userId)
            {
                var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, "Employee") }, "test")) };
                return new MainController(db, null!, null!, null!, null!, null!) { ControllerContext = new ControllerContext { HttpContext = http }, TempData = new TempDataDictionary(http, new EmptyTempData()) };
            }
            var controller = Controller(user.Id);
            Check(await controller.RespondToAsset(pending.Id, "invalid") is BadRequestResult, "Invalid decisions rejected");
            Check(await Controller(0).RespondToAsset(pending.Id, "Accept") is ForbidResult, "Unlinked account cannot respond");
            var outsider = new Employee { EmployeeCode = "OUTSIDER", FirstName = "Other", FullName = "Other", Email = "other@example.invalid", PhoneNumber = "456", Department = "Test", Designation = "Test" };
            db.Employees.Add(outsider);
            await db.SaveChangesAsync();
            var foreignAsset = new EmployeeAsset { EmployeeId = outsider.Id, AssetTag = "OTHER", AssetName = "Phone", Category = "Phone" };
            db.EmployeeAssets.Add(foreignAsset);
            await db.SaveChangesAsync();
            await controller.RespondToAsset(foreignAsset.Id, "Accept");
            Check((await db.EmployeeAssets.AsNoTracking().SingleAsync(a => a.Id == foreignAsset.Id)).Status == "Pending", "Employee cannot accept another employee's asset");
            await controller.RespondToAsset(pending.Id, "Accept");
            var accepted = await db.EmployeeAssets.AsNoTracking().SingleAsync(a => a.Id == pending.Id);
            Check(accepted.Status == "Issued" && accepted.Quantity == 3 && accepted.RespondedAtUtc.HasValue, "Acceptance persists issuance, quantity and response time");
            await controller.RespondToAsset(pending.Id, "Decline");
            Check((await db.EmployeeAssets.AsNoTracking().SingleAsync(a => a.Id == pending.Id)).Status == "Issued", "Repeated response cannot reverse acceptance");
            var declined = new EmployeeAsset { EmployeeId = owner.Id, AssetTag = "DECLINE", AssetName = "Shoes", Category = "Shoes" };
            db.EmployeeAssets.Add(declined);
            await db.SaveChangesAsync();
            await controller.RespondToAsset(declined.Id, "Decline");
            Check((await db.EmployeeAssets.AsNoTracking().SingleAsync(a => a.Id == declined.Id)).Status == "Declined", "Decline retained for HR without issuance");
            db.EmployeeAssets.Add(new EmployeeAsset { EmployeeId = owner.Id, AssetTag = "DECLINE", AssetName = "Shoes", Category = "Shoes" });
            await db.SaveChangesAsync();
            Check(await db.EmployeeAssets.CountAsync(a => a.AssetTag == "DECLINE") == 2, "Declined tag can be reassigned while retaining history");
        }
        finally { await new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", admin).ExecuteNonQueryAsync(); }
    }

    sealed class EmptyTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
