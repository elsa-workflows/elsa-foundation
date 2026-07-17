using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.Unified;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

public sealed class GroundworkShellSchemaActivationTests
{
    private const string ShellName = "groundwork-schema-selection";

    [Fact]
    public async Task Identity_feature_selects_and_admits_the_identity_schema_through_real_shell_activation()
    {
        await using var database = new TemporarySqliteDatabase();
        await ApplySchemaAsync<GroundworkAllFeaturesWithIdentityDeploymentSchema>(database.ConnectionString);
        await using var root = BuildRoot(database.ConnectionString, includeIdentity: true);

        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();

        Assert.IsType<GroundworkAllFeaturesWithIdentityDeploymentSchema>(
            scope.ServiceProvider.GetRequiredService<IPhysicalSchemaManifestSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public async Task Bare_provider_shell_selects_and_admits_the_default_schema_without_identity()
    {
        await using var database = new TemporarySqliteDatabase();
        await ApplySchemaAsync<GroundworkAllFeaturesDeploymentSchema>(database.ConnectionString);
        await using var root = BuildRoot(database.ConnectionString, includeIdentity: false);

        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();

        Assert.IsType<GroundworkAllFeaturesDeploymentSchema>(
            scope.ServiceProvider.GetRequiredService<IPhysicalSchemaManifestSource>());
        Assert.Null(scope.ServiceProvider.GetService<IUserStore>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public async Task Identity_shell_fails_readiness_when_only_the_default_schema_was_applied()
    {
        await using var database = new TemporarySqliteDatabase();
        await ApplySchemaAsync<GroundworkAllFeaturesDeploymentSchema>(database.ConnectionString);
        await using var root = BuildRoot(database.ConnectionString, includeIdentity: true);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));
        var message = Flatten(exception);

        Assert.Contains("schema", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("admission failed", message, StringComparison.OrdinalIgnoreCase);
    }

    private static ServiceProvider BuildRoot(string connectionString, bool includeIdentity)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddSingleton(TimeProvider.System)
            .AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);

        services.AddCShells(builder =>
        {
            builder
                .WithAssemblies(
                    typeof(SqliteGroundworkUnifiedPersistenceShellFeature).Assembly,
                    typeof(AspNetCoreIdentityGroundworkFeature).Assembly,
                    typeof(GroundworkShellSchemaActivationTests).Assembly)
                .AddShell(ShellName, shell =>
                {
                    shell
                        .WithFeature<GroundworkResumptionDependencyProbe>()
                        .WithFeature<SqliteGroundworkUnifiedPersistenceShellFeature>(feature =>
                        {
                            feature.ConnectionString = connectionString;
                            feature.AutoApplySchemaOnStartup = false;
                        });
                    if (includeIdentity)
                        shell.WithFeature<AspNetCoreIdentityGroundworkFeature>();
                });
        });

        return services.BuildServiceProvider();
    }

    private static async Task ApplySchemaAsync<TDeploymentSource>(string connectionString)
        where TDeploymentSource : GroundworkDeploymentSchemaManifestSource, new()
    {
        var services = new ServiceCollection()
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);
        await using var provider = services
            .AddGroundworkSqliteUnifiedPersistence<TDeploymentSource>(connectionString)
            .BuildServiceProvider();
        await provider.ApplySqliteGroundworkSchemaAsync(connectionString);
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        return string.Join(" | ", messages);
    }

    private sealed class TenantAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public static TenantAccessContextAccessor Instance { get; } = new();

        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1"));
    }

    private sealed class TemporarySqliteDatabase : IAsyncDisposable
    {
        private readonly string path = Path.Combine(
            Path.GetTempPath(),
            $"elsa-groundwork-shell-schema-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={path}";

        public ValueTask DisposeAsync()
        {
            File.Delete(path);
            return ValueTask.CompletedTask;
        }
    }
}

[ShellFeature(
    name: "WorkflowsRuntimeResumption",
    DisplayName = "Groundwork resumption dependency probe",
    Description = "Satisfies the unified provider dependency while schema selection and admission are tested in isolation.")]
public sealed class GroundworkResumptionDependencyProbe : IShellFeature
{
    public void ConfigureServices(IServiceCollection services)
    {
    }
}
