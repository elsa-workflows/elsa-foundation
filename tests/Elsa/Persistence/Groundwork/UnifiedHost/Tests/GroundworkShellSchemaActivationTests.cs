using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.Unified;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using System.Collections.Concurrent;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

public sealed class GroundworkShellSchemaActivationTests
{
    private const string ShellName = "groundwork-schema-selection";

    [Fact]
    public async Task Bare_provider_shell_selects_and_admits_the_default_schema_without_identity()
    {
        await using var database = new TemporarySqliteDatabase("elsa-groundwork-shell-schema");
        await ApplySchemaAsync<GroundworkAllFeaturesDeploymentSchema>(database.ConnectionString);
        await using var root = BuildRoot(database.ConnectionString);

        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();

        Assert.IsType<GroundworkAllFeaturesDeploymentSchema>(
            scope.ServiceProvider.GetRequiredService<IPhysicalSchemaManifestSource>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentStore>());
    }

    [Fact]
    public async Task Skip_if_current_stamps_the_first_activation_and_skips_the_walk_on_the_second()
    {
        await using var database = new TemporarySqliteDatabase("elsa-groundwork-shell-schema");

        // First activation against a fresh database: auto-apply admits and records the applied-plan stamp.
        var firstLog = new CapturingLoggerProvider();
        await using (var first = BuildRoot(
            database.ConnectionString,
            autoApply: true, skipInspection: true, loggerProvider: firstLog))
        {
            var shell = await first.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.BeginScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentStore>());
        }
        Assert.DoesNotContain(firstLog.Messages, m => m.Contains("skipped the inspection walk"));

        // Second activation against the same, now-current database: the stamp covers the plan, so the full
        // inspection walk is skipped and activation still yields a working document store.
        var secondLog = new CapturingLoggerProvider();
        await using (var second = BuildRoot(
            database.ConnectionString,
            autoApply: true, skipInspection: true, loggerProvider: secondLog))
        {
            var shell = await second.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
            await using var scope = shell.BeginScope();
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<IDocumentStore>());
        }
        Assert.Contains(secondLog.Messages, m => m.Contains("skipped the inspection walk"));
    }

    private static ServiceProvider BuildRoot(
        string connectionString,
        bool autoApply = false,
        bool skipInspection = false,
        ILoggerProvider? loggerProvider = null)
    {
        var services = new ServiceCollection()
            .AddLogging(builder =>
            {
                if (loggerProvider is not null)
                    builder.AddProvider(loggerProvider);
            })
            .AddSingleton<IPayloadSerializer, FakePayloadSerializer>()
            .AddSingleton<ISystemClock, FakeSystemClock>()
            .AddSingleton(TimeProvider.System)
            .AddScoped<IPersistenceAccessContextAccessor>(_ => TenantAccessContextAccessor.Instance);

        services.AddCShells(builder =>
        {
            builder
                .WithAssemblies(
                    typeof(SqliteGroundworkUnifiedPersistenceShellFeature).Assembly,
                    typeof(GroundworkShellSchemaActivationTests).Assembly)
                .AddShell(ShellName, shell =>
                {
                    shell
                        .WithFeature<GroundworkResumptionDependencyProbe>()
                        .WithFeature<SqliteGroundworkUnifiedPersistenceShellFeature>(feature =>
                        {
                            feature.ConnectionString = connectionString;
                            feature.AutoApplySchemaOnStartup = autoApply;
                            feature.SkipSchemaInspectionWhenPlanUnchanged = skipInspection;
                        });
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

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Messages);
        public void Dispose() { }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }

    private sealed class TenantAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public static TenantAccessContextAccessor Instance { get; } = new();

        public PersistenceAccessContext Current { get; } =
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-1"));
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
