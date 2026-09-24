using CShells.Configuration;
using CShells.Features;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

[assembly: EfModule(
    "Acme.ResourceProbe",
    typeof(ResourceProbeDbContext),
    HistoryModule = "AcmeResourceProbe",
    Sqlite = typeof(ResourceProbeDbContext),
    PostMigration = [typeof(ResourceProbePostMigrationAction)])]
[assembly: EfToolingShellDefaults(typeof(ResourceProbeShellDefaults))]

return 0;

[ShellFeature(name: "ResourceProbe", DisplayName = "Resource probe")]
[UsesEfModule("Acme.ResourceProbe")]
[EfPersistenceResourceParticipant]
public sealed class ResourceProbeFeature : IShellFeature
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionName { get; set; }
    public string? ConnectionString { get; set; }

    public void ConfigureServices(IServiceCollection services)
    {
    }
}

public sealed class ResourceProbeDbContext : DbContext
{
    public ResourceProbeDbContext(DbContextOptions<ResourceProbeDbContext> options) : base(options)
    {
        if (Environment.GetEnvironmentVariable("ELSA_RESOURCE_PROBE_CONTEXT_MARKER") is { Length: > 0 } path)
            File.WriteAllText(path, "constructed");
    }
}

public sealed class ResourceProbePostMigrationAction : IEfPostMigrationAction
{
    public ResourceProbePostMigrationAction()
    {
        if (Environment.GetEnvironmentVariable("ELSA_RESOURCE_PROBE_ACTION_MARKER") is { Length: > 0 } path)
            File.WriteAllText(path, "constructed");
    }

    public string Id => "resource-probe";
    public string Kind => "test";
    public string RequiredWhen => "never";
    public string Audit => "none";
    public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task RunAsync(DbContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class ResourceProbeShellDefaults : IEfToolingShellDefaults
{
    public void Configure(ShellBuilder builder, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configuration);
    }
}
