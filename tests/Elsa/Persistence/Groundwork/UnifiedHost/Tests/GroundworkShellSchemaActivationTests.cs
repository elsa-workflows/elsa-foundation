using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Diagnostics.OpenTelemetry;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Groundwork;
using Elsa.Diagnostics.StructuredLogs;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Persistence.Groundwork;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.AspNetCoreIdentity.Groundwork;
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
using Groundwork.DiagnosticRecords;
using Groundwork.Documents.Store;
using Groundwork.Sqlite.DiagnosticRecords;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    public async Task Diagnostics_feature_auto_applies_fresh_streams_and_activates_both_Groundwork_stores_without_an_EF_store()
    {
        await using var database = new TemporarySqliteDatabase();
        await using var root = BuildRoot(
            database.ConnectionString,
            includeIdentity: false,
            includeDiagnostics: true,
            autoApply: true);

        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();

        Assert.IsType<GroundworkAllFeaturesWithDiagnosticsDeploymentSchema>(
            scope.ServiceProvider.GetRequiredService<IPhysicalSchemaManifestSource>());
        Assert.IsType<GroundworkOpenTelemetryStore>(
            scope.ServiceProvider.GetRequiredService<IOpenTelemetryStore>());
        Assert.IsType<GroundworkStructuredLogStore>(
            scope.ServiceProvider.GetRequiredService<IStructuredLogStore>());
        Assert.DoesNotContain(
            scope.ServiceProvider.GetServices<IOpenTelemetryStore>(),
            store => store.GetType().Namespace?.Contains(".EFCore", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(
            scope.ServiceProvider.GetServices<IStructuredLogStore>(),
            store => store.GetType().Namespace?.Contains(".EFCore", StringComparison.Ordinal) == true);

        var deployment = new GroundworkAllFeaturesWithDiagnosticsDeploymentSchema().CreateDeploymentManifest();
        var inspection = await new SqliteDiagnosticRecordDeploymentInspector(database.ConnectionString)
            .InspectAsync(deployment);
        Assert.Equal(DiagnosticRecordDeploymentAdmissionStatus.Ready, inspection.Status);
    }

    [Fact]
    public async Task Diagnostics_feature_with_auto_apply_disabled_fails_when_streams_are_missing()
    {
        await using var database = new TemporarySqliteDatabase();
        await ApplySchemaAsync<GroundworkAllFeaturesWithDiagnosticsDeploymentSchema>(database.ConnectionString);
        await using var root = BuildRoot(
            database.ConnectionString,
            includeIdentity: false,
            includeDiagnostics: true,
            autoApply: false);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        Assert.Contains("GW-DIAG-DEPLOY-001", Flatten(exception), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_feature_auto_apply_is_idempotent_across_shell_restarts()
    {
        await using var database = new TemporarySqliteDatabase();

        await ActivateDiagnosticsAsync(database.ConnectionString);
        await ActivateDiagnosticsAsync(database.ConnectionString);

        var deployment = new GroundworkAllFeaturesWithDiagnosticsDeploymentSchema().CreateDeploymentManifest();
        var inspection = await new SqliteDiagnosticRecordDeploymentInspector(database.ConnectionString)
            .InspectAsync(deployment);
        Assert.Equal(DiagnosticRecordDeploymentAdmissionStatus.Ready, inspection.Status);
    }

    [Fact]
    public async Task Diagnostics_feature_auto_apply_rejects_drift_before_creating_missing_streams()
    {
        await using var database = new TemporarySqliteDatabase();
        var deployment = new GroundworkAllFeaturesWithDiagnosticsDeploymentSchema().CreateDeploymentManifest();
        var expected = deployment.Streams[0];
        var drifted = expected with { SchemaVersion = expected.SchemaVersion + 1 };
        _ = await SqliteDiagnosticRecordStoreFactory.CreateAsync(database.ConnectionString, drifted);

        await using var root = BuildRoot(
            database.ConnectionString,
            includeIdentity: false,
            includeDiagnostics: true,
            autoApply: true);
        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName));

        Assert.Contains("GW-DIAG-DEPLOY-002", Flatten(exception), StringComparison.Ordinal);
        var inspector = new SqliteDiagnosticRecordDeploymentInspector(database.ConnectionString);
        var driftInspection = await inspector.InspectAsync(
            new DiagnosticRecordDeploymentManifest(deployment.Storage, [expected]));
        Assert.Equal(DiagnosticRecordDeploymentAdmissionStatus.Drifted, driftInspection.Status);
        var missingInspection = await inspector.InspectAsync(
            new DiagnosticRecordDeploymentManifest(deployment.Storage, [deployment.Streams[1]]));
        Assert.Equal(DiagnosticRecordDeploymentAdmissionStatus.Missing, missingInspection.Status);
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

    [Fact]
    public async Task Skip_if_current_stamps_the_first_activation_and_skips_the_walk_on_the_second()
    {
        await using var database = new TemporarySqliteDatabase();

        // First activation against a fresh database: auto-apply admits and records the applied-plan stamp.
        var firstLog = new CapturingLoggerProvider();
        await using (var first = BuildRoot(
            database.ConnectionString, includeIdentity: false,
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
            database.ConnectionString, includeIdentity: false,
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
        bool includeIdentity,
        bool includeDiagnostics = false,
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
                    typeof(AspNetCoreIdentityGroundworkFeature).Assembly,
                    typeof(DiagnosticsGroundworkPersistenceFeature).Assembly,
                    typeof(OpenTelemetryFeature).Assembly,
                    typeof(StructuredLogsFeature).Assembly,
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
                    if (includeIdentity)
                        shell.WithFeature<AspNetCoreIdentityGroundworkFeature>();
                    if (includeDiagnostics)
                        shell.WithFeature<DiagnosticsGroundworkPersistenceFeature>();
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

    private static async Task ActivateDiagnosticsAsync(string connectionString)
    {
        await using var root = BuildRoot(
            connectionString,
            includeIdentity: false,
            includeDiagnostics: true,
            autoApply: true);
        var shell = await root.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        await using var scope = shell.BeginScope();
        Assert.IsType<GroundworkOpenTelemetryStore>(
            scope.ServiceProvider.GetRequiredService<IOpenTelemetryStore>());
        Assert.IsType<GroundworkStructuredLogStore>(
            scope.ServiceProvider.GetRequiredService<IStructuredLogStore>());
    }

    private static string Flatten(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            messages.Add(current.Message);
        return string.Join(" | ", messages);
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
