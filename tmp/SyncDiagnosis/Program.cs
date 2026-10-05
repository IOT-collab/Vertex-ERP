using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VertexERP.Data;
var config = new ConfigurationBuilder().SetBasePath(Path.GetFullPath("Vertex ERP")).AddJsonFile("appsettings.json").AddJsonFile("appsettings.Development.json", true).AddJsonFile("App_Data/remoteattendance.json", true).AddEnvironmentVariables().Build();
var options = config.GetSection("RemoteAttendance").Get<RemoteAttendanceOptions>()!;
var services = new ServiceCollection();
services.AddDbContext<ApplicationDbContext>(o => o.UseNpgsql(config.GetConnectionString("DefaultConnection") + ";Options=-c default_transaction_read_only=on").AddInterceptors(new NoWrites()));
services.AddHttpClient(RemoteAttendanceImportService.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { UseCookies = true, AllowAutoRedirect = true });
using var provider = services.BuildServiceProvider();
using var service = new RemoteAttendanceImportService(provider.GetRequiredService<IServiceScopeFactory>(), provider.GetRequiredService<IHttpClientFactory>(), Options.Create(options), NullLogger<RemoteAttendanceImportService>.Instance);
try {
 await (Task)typeof(RemoteAttendanceImportService).GetMethod("ImportAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [CancellationToken.None])!;
 Console.WriteLine("Read-only importer diagnostic completed.");
} catch(Exception e) {
 Console.WriteLine("Stage: " + service.Status.Message);
 Console.WriteLine(e.GetType().Name + ": " + e.Message);
 Console.WriteLine(e.StackTrace);
}
sealed class NoWrites : SaveChangesInterceptor {
 public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken token = default) {
  Console.WriteLine("Suppressed database save: " + data.Context!.ChangeTracker.Entries().Count(e => e.State != EntityState.Unchanged) + " changes");
  return ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0));
 }
}
