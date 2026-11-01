using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Persistence.EntityFrameworkCore;
using Elsa.Connections.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

if (args.Length is not (2 or 3) || args[0] is not ("write" or "read") || args.Length == 3 && args[2] != "--consumer-context") return 2;
var consumerContext = args.Length == 3;
var services = new ServiceCollection();
services.AddLogging();
var connectionString = "Data Source=" + args[1] + ";Pooling=False";
if (consumerContext)
{
    services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
    services.AddConnectionsRepository<ApplicationDbContext>();
}
else services.AddConnectionsEntityFrameworkCore(new() { ConnectionString = connectionString });
await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
await using var scope = provider.CreateAsyncScope();
var repository = scope.ServiceProvider.GetRequiredService<IConnectionRepository>();
DbContext context = consumerContext ? scope.ServiceProvider.GetRequiredService<ApplicationDbContext>() : scope.ServiceProvider.GetRequiredService<ConnectionsDbContext>();
if (!(await repository.GetCapabilitiesAsync()).IsDurable) return 3;
if (args[0] == "write")
{
    if (consumerContext)
    {
        await context.Database.EnsureCreatedAsync();
        context.Set<ApplicationItem>().Add(new() { Id = "uncommitted" });
    }
    else await context.Database.MigrateAsync();
    var document = new ConnectionDocument
    {
        Definition = new()
        {
            TenantId = "restart-tenant", Name = "restart-model", DisplayName = "restart model", TypeName = "http", AuthScheme = "bearer",
            SecretFields = new Dictionary<string, ConnectionSecretField> { ["token"] = new() { SecretName = "reference-only" } }
        },
        State = new() { CreatedAt = DateTimeOffset.UnixEpoch }
    };
    if (!await repository.TryAddAsync(document)) return 4;
    document.State.Status = ConnectionStatuses.Revoked;
    await repository.SaveAsync(document);
    if (consumerContext && !context.ChangeTracker.HasChanges()) return 8;
}
else
{
    var document = await repository.FindAsync("restart-tenant", "RESTART-MODEL");
    if (document?.State.Version != 1 || document.State.Status != ConnectionStatuses.Revoked) return 5;
    if (!await repository.IsSecretReferencedAsync("restart-tenant", "REFERENCE-ONLY")) return 6;
    if (await repository.FindAsync("Restart-Tenant", "restart-model") is not null) return 7;
    if (consumerContext && await context.Set<ApplicationItem>().AnyAsync()) return 9;
}
Console.WriteLine(args[0] == "write" ? "metadata-committed" : "metadata-restored");
Console.WriteLine("process-id=" + Environment.ProcessId);
return 0;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationItem>().HasKey(x => x.Id);
        modelBuilder.AddConnectionsModel(Database.ProviderName!);
    }
}

public sealed class ApplicationItem { public string Id { get; set; } = ""; }
