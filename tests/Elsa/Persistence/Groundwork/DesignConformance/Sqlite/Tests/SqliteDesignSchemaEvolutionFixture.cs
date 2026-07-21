using System.Security.Cryptography;
using System.Text;
using CShells.Lifecycle;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Validations;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Store;
using Groundwork.Sqlite;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>
/// SQLite execution of the provider-neutral <see cref="UnifiedSchemaEvolutionContractSuite"/>. The
/// baseline reference schema is applied through the repository-pinned CLI (the same seam T061 pins);
/// additive evolution, interrupted apply and admission run through the real in-process runtime
/// admission gate (<see cref="GroundworkPhysicalSchemaManifestSource.InspectRuntimeAdmissionAsync"/>),
/// which is the exact core the CLI wraps. Interruption injects a deterministic failure into the
/// physical-schema executor after a fixed number of applied operations, before the applied-state
/// snapshot is recorded. The additive extension is the leaf-owned
/// <see cref="SqliteDesignEvolutionDeploymentSchema"/>; the collision extension is
/// <see cref="SqliteDesignEvolutionCollisionDeploymentSchema"/>.
/// </summary>
internal sealed class SqliteDesignSchemaEvolutionFixture : IDesignSchemaEvolutionFixture
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), $"elsa-design-evolution-{Guid.NewGuid():N}");
    private ServiceProvider? _host;

    private string DatabasePath => Path.Join(_directory, "design.db");
    private string ConnectionString => $"Data Source={DatabasePath}";

    public string Provider => "groundwork-sqlite";

    public static async Task<SqliteDesignSchemaEvolutionFixture> CreateAsync(CancellationToken cancellationToken = default)
    {
        var fixture = new SqliteDesignSchemaEvolutionFixture();
        Directory.CreateDirectory(fixture._directory);
        await GroundworkSchemaCli.ApplyFreshAsync(fixture.ConnectionString, cancellationToken);
        await fixture.OpenHostAsync(evolved: false, cancellationToken);
        return fixture;
    }

    public async Task<DesignEvolutionSeed> SeedRepresentativeAggregateAsync(CancellationToken cancellationToken = default)
    {
        var host = _host ?? throw new InvalidOperationException("The baseline host is not open.");
        using (var scope = CreateScope(host, DesignPersistenceFixtureData.ScopeA))
        {
            await scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionCommand>().Execute(
                DesignPersistenceFixtureData.OperationKey("evolution-seed"),
                DesignPersistenceFixtureData.WorkflowDefinition(),
                DesignPersistenceFixtureData.WorkflowDraft(),
                cancellationToken);
        }

        var hash = await ReadSeededAggregateHashAsync(DesignPersistenceFixtureData.WorkflowDefinitionId, cancellationToken);
        return new DesignEvolutionSeed(DesignPersistenceFixtureData.WorkflowDefinitionId, hash);
    }

    public async Task<DesignEvolutionApplyReport> ApplyAdditiveEvolutionAsync(CancellationToken cancellationToken = default)
    {
        await CloseHostAsync();
        await using var compositionServices = BuildCompositionServices(evolved: true);

        var plan = await InspectAsync(compositionServices, autoApply: false, cancellationToken);
        var pending = plan.PendingOperations.Count;

        var apply = await InspectAsync(compositionServices, autoApply: true, cancellationToken);

        var validate = await InspectAsync(compositionServices, autoApply: false, cancellationToken);

        return new DesignEvolutionApplyReport(
            PlanReportedPending: pending > 0 && !plan.IsReady,
            PendingOperationsBeforeApply: pending,
            ApplySucceeded: apply.IsReady,
            AppliedOperations: apply.AppliedOperationCount,
            LiveValidationReady: validate.IsReady && validate.PendingOperations.Count == 0);
    }

    public async Task<DesignEvolutionAdmissionReport> RestartUnderEvolvedSchemaAsync(CancellationToken cancellationToken = default)
    {
        await CloseHostAsync();
        await OpenHostAsync(evolved: true, cancellationToken);
        await using var compositionServices = BuildCompositionServices(evolved: true);
        var admission = await InspectAsync(compositionServices, autoApply: false, cancellationToken);
        return new DesignEvolutionAdmissionReport(admission.IsReady, admission.PendingOperations.Count);
    }

    public async Task<string> ReadSeededAggregateHashAsync(string aggregateId, CancellationToken cancellationToken = default)
    {
        var host = _host ?? throw new InvalidOperationException("No host is open to read the seeded aggregate.");
        using var scope = CreateScope(host, DesignPersistenceFixtureData.ScopeA);
        var document = await scope.ServiceProvider.GetRequiredService<IDocumentStore>().LoadAsync(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            aggregateId,
            cancellationToken);
        if (document is null)
            throw new InvalidOperationException($"The seeded aggregate '{aggregateId}' was not readable after evolution.");
        return Digest(document.ContentJson);
    }

    public async Task<bool> ProbeEvolvedRouteServableAsync(CancellationToken cancellationToken = default)
    {
        var host = _host ?? throw new InvalidOperationException("No host is open to probe the evolved route.");
        using var scope = CreateScope(host, DesignPersistenceFixtureData.ScopeA);
        var store = scope.ServiceProvider.GetRequiredService<IDocumentStore>();
        // Writing a probe document and reading it back proves the freshly provisioned table accepts
        // the unit's projections and serves point reads, not merely that the table exists.
        const string probeId = "design-evolution-probe-1";
        await store.SaveAsync(new SaveDocumentRequest(
            DesignEvolutionProbeManifest.ProbeKind,
            probeId,
            "1.0.0",
            $"{{\"collection\":\"{DesignEvolutionProbeManifest.ProbeCollection}\",\"entity\":{{\"id\":\"{probeId}\",\"value\":\"probe\"}}}}"));
        var read = await store.LoadAsync(DesignEvolutionProbeManifest.ProbeKind, probeId, cancellationToken);
        return read is not null;
    }

    public async Task<DesignEvolutionCollisionReport> InspectNameCollisionAsync(CancellationToken cancellationToken = default)
    {
        await using var compositionServices = BuildCompositionServices(collision: true);
        try
        {
            await ComposeAsync(compositionServices, cancellationToken);
            return new DesignEvolutionCollisionReport(false, string.Empty, string.Empty, string.Empty, TargetMutated: false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var message = exception.Message;
            var identifier = FirstQuotedToken(message);
            return new DesignEvolutionCollisionReport(
                SurfacedCollision: true,
                DiagnosticCode: message,
                CollidingIdentifier: identifier,
                DiagnosticText: message,
                TargetMutated: false);
        }
    }

    public async Task<DesignEvolutionInterruptionReport> ApplyAdditiveEvolutionWithInterruptionAsync(
        int failAfterOperations,
        CancellationToken cancellationToken = default)
    {
        await CloseHostAsync();
        await using var compositionServices = BuildCompositionServices(evolved: true);
        var source = await ComposeAsync(compositionServices, cancellationToken);

        var interrupted = false;
        await using (var connection = SqliteConnectionFactory.Create(ConnectionString))
        {
            var executor = new InterruptingPhysicalSchemaExecutor(new SqlitePhysicalSchemaExecutor(connection), failAfterOperations);
            try
            {
                await source.InspectRuntimeAdmissionAsync(
                    executor,
                    new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true },
                    null,
                    cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                interrupted = executor.FailureInjected || IsInterruption(exception);
            }
        }

        var validate = await InspectAsync(compositionServices, autoApply: false, cancellationToken);
        return new DesignEvolutionInterruptionReport(interrupted, validate.IsReady);
    }

    public async Task<DesignEvolutionApplyReport> ResumeInterruptedEvolutionAsync(CancellationToken cancellationToken = default)
    {
        await using var compositionServices = BuildCompositionServices(evolved: true);
        var apply = await InspectAsync(compositionServices, autoApply: true, cancellationToken);
        var validate = await InspectAsync(compositionServices, autoApply: false, cancellationToken);
        return new DesignEvolutionApplyReport(
            PlanReportedPending: false,
            PendingOperationsBeforeApply: 0,
            ApplySucceeded: apply.IsReady,
            AppliedOperations: apply.AppliedOperationCount,
            LiveValidationReady: validate.IsReady && validate.PendingOperations.Count == 0);
    }

    public async Task<DesignEvolutionDriftReport> PerturbAppliedObjectAndInspectAsync(CancellationToken cancellationToken = default)
    {
        string droppedIndex;
        await using (var connection = new SqliteConnection(ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using (var find = connection.CreateCommand())
            {
                find.CommandText =
                    "SELECT name FROM sqlite_master WHERE type = 'index' " +
                    "AND sql IS NOT NULL AND name LIKE '%definition%' ORDER BY name LIMIT 1";
                droppedIndex = (string?)await find.ExecuteScalarAsync(cancellationToken)
                    ?? throw new InvalidOperationException("No applied design index was found to drift.");
            }

            await using var drop = connection.CreateCommand();
            drop.CommandText = $"DROP INDEX \"{droppedIndex}\"";
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        var validation = await GroundworkSchemaCli.RunAsync("validate", ConnectionString, cancellationToken);
        var validationBlocked = validation.ExitCode != 0 ||
            !string.Equals(validation.Report.GetProperty("outcome").GetString(), "ready", StringComparison.OrdinalIgnoreCase) ||
            validation.Report.GetProperty("pendingOperations").GetArrayLength() > 0;

        await CloseHostAsync();
        var admissionBlocked = false;
        try
        {
            await OpenHostAsync(evolved: false, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            admissionBlocked = true;
        }

        return new DesignEvolutionDriftReport(
            PerturbedObject: droppedIndex,
            ValidationBlocked: validationBlocked,
            AdmissionBlocked: admissionBlocked,
            Detail: $"dropped index '{droppedIndex}'; validate exit {validation.ExitCode}");
    }

    public async Task<bool> ObserveCancellationBeforeProviderIoAsync(CancellationToken cancellationToken = default)
    {
        await using var compositionServices = BuildCompositionServices(evolved: true);
        var source = await ComposeAsync(compositionServices, cancellationToken);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await using var connection = SqliteConnectionFactory.Create(ConnectionString);
        try
        {
            await source.InspectRuntimeAdmissionAsync(
                new SqlitePhysicalSchemaExecutor(connection),
                new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = false },
                null,
                cancelled.Token);
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    public Task<string> CaptureTargetFingerprintAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fingerprint = File.Exists(DatabasePath)
            ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(DatabasePath)))
            : "absent";
        return Task.FromResult(fingerprint);
    }

    public async ValueTask DisposeAsync()
    {
        await CloseHostAsync();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A uniquely named test directory is harmless if SQLite releases a sidecar late.
        }
    }

    private async Task OpenHostAsync(bool evolved, CancellationToken cancellationToken)
    {
        var host = BuildHost(evolved);
        try
        {
            foreach (var initializer in host.GetServices<IShellInitializer>())
                await initializer.InitializeAsync(cancellationToken);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }

        _host = host;
    }

    private async Task CloseHostAsync()
    {
        if (_host is null)
            return;

        var host = _host;
        _host = null;
        await host.DisposeAsync();
    }

    private ServiceProvider BuildHost(bool evolved)
    {
        var services = CreateDesignServices();
        if (evolved)
            services.AddGroundworkSqliteUnifiedPersistence<SqliteDesignEvolutionDeploymentSchema>(ConnectionString, autoApplyOnStartup: false);
        else
            services.AddGroundworkSqliteUnifiedPersistence(ConnectionString, autoApplyOnStartup: false);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static ServiceCollection CreateDesignServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ISystemClock>(new DesignPersistenceFixtureData.FixedSystemClock(DesignPersistenceFixtureData.Epoch));
        services.AddSingleton<IIdentityGenerator, EvolutionIdentityGenerator>();
        services.AddSingleton<IDistributedLockProvider, ImmediateDesignContractLockProvider>();
        services.AddSingleton<IPayloadSerializer, DesignPersistenceFixtureData.DeterministicPayloadSerializer>();
        new EventsFeature().ConfigureServices(services);
        services.AddScoped<IDeferredEventPublisher, DroppingDeferredEventPublisher>();
        services.AddSingleton<IActivityStructureService, EmptyActivityStructureService>();
        return services;
    }

    private ServiceProvider BuildCompositionServices(bool evolved = false, bool collision = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        if (collision)
            services.AddGroundworkStorageComposition<SqliteDesignEvolutionCollisionDeploymentSchema>();
        else if (evolved)
            services.AddGroundworkStorageComposition<SqliteDesignEvolutionDeploymentSchema>();
        else
            services.AddGroundworkStorageComposition<GroundworkAllFeaturesDeploymentSchema>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static async Task<GroundworkPhysicalSchemaManifestSource> ComposeAsync(
        ServiceProvider services,
        CancellationToken cancellationToken)
    {
        using var scope = services.CreateScope();
        var capabilities = await GroundworkProviderCapabilitySnapshotBuilder.ForSelectedSourcesAsync(
            SqliteGroundworkCapabilities.Runtime(),
            new GroundworkProviderTopologySnapshot(
                SqliteGroundworkCapabilities.Provider.Name,
                "sqlite-file",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            scope.ServiceProvider.GetServices<IGroundworkStorageManifestSource>(),
            cancellationToken);
        return await scope.ServiceProvider
            .GetRequiredService<GroundworkStorageCompositionFactory>()
            .CreateSourceAsync(capabilities, SqliteGroundworkCapabilities.PhysicalNames, cancellationToken);
    }

    private async Task<Elsa.Persistence.Groundwork.Unified.Composition.GroundworkRuntimeSchemaAdmissionResult> InspectAsync(
        ServiceProvider services,
        bool autoApply,
        CancellationToken cancellationToken)
    {
        var source = await ComposeAsync(services, cancellationToken);
        await using var connection = SqliteConnectionFactory.Create(ConnectionString);
        return await source.InspectRuntimeAdmissionAsync(
            new SqlitePhysicalSchemaExecutor(connection),
            new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = autoApply },
            null,
            cancellationToken);
    }

    private IServiceScope CreateScope(ServiceProvider host, string storageScope)
    {
        var scope = host.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.Scoped(new PersistenceScope(storageScope)));
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private static bool IsInterruption(Exception exception) =>
        exception is DesignSchemaInterruptionException ||
        (exception.InnerException is not null && IsInterruption(exception.InnerException));

    private static string FirstQuotedToken(string message)
    {
        var start = message.IndexOf('\'');
        if (start < 0)
            return message;
        var end = message.IndexOf('\'', start + 1);
        return end < 0 ? message : message.Substring(start + 1, end - start - 1);
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class EvolutionIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"evolution-{Interlocked.Increment(ref _next):D6}";
    }

    private sealed class DroppingDeferredEventPublisher : IDeferredEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }
    }

    private sealed class EmptyActivityStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
}

/// <summary>Signals a deliberately injected mid-apply schema-operation failure.</summary>
internal sealed class DesignSchemaInterruptionException(int appliedBeforeFailure)
    : Exception($"Injected design schema-apply interruption after {appliedBeforeFailure} operation(s).")
{
    public int AppliedBeforeFailure { get; } = appliedBeforeFailure;
}

/// <summary>
/// Wraps a physical-schema executor to inject one deterministic failure after a fixed number of
/// applied operations, before the applied-state snapshot is recorded. All other operations —
/// including history inspection and applied-state recording — are forwarded unchanged, so the
/// interrupted target is exactly what a real mid-apply crash would leave behind.
/// </summary>
internal sealed class InterruptingPhysicalSchemaExecutor(
    SqlitePhysicalSchemaExecutor inner,
    int failAfterOperations) : IPhysicalSchemaExecutor, IPhysicalSchemaHistoryInspector
{
    private int _applied;

    public bool FailureInjected { get; private set; }

    public ValueTask<IPhysicalSchemaApplicationLock> AcquireApplicationLockAsync(
        PhysicalSchemaTargetIdentity target,
        CancellationToken cancellationToken) =>
        inner.AcquireApplicationLockAsync(target, cancellationToken);

    public ValueTask<PhysicalSchemaHistoryState> ReadHistoryAsync(
        PhysicalSchemaTargetIdentity target,
        IPhysicalSchemaApplicationLock applicationLock,
        CancellationToken cancellationToken) =>
        inner.ReadHistoryAsync(target, applicationLock, cancellationToken);

    public async ValueTask<PhysicalSchemaOperationAcknowledgement> ApplyOperationAsync(
        PhysicalSchemaTargetIdentity target,
        PhysicalSchemaOperation operation,
        IPhysicalSchemaApplicationLock applicationLock,
        CancellationToken cancellationToken)
    {
        if (_applied >= failAfterOperations)
        {
            FailureInjected = true;
            throw new DesignSchemaInterruptionException(_applied);
        }

        var acknowledgement = await inner.ApplyOperationAsync(target, operation, applicationLock, cancellationToken);
        _applied++;
        return acknowledgement;
    }

    public ValueTask RecordAppliedStateAsync(
        PhysicalSchemaAppliedState state,
        string? expectedAppliedTargetFingerprint,
        IPhysicalSchemaApplicationLock applicationLock,
        CancellationToken cancellationToken) =>
        inner.RecordAppliedStateAsync(state, expectedAppliedTargetFingerprint, applicationLock, cancellationToken);

    public ValueTask<PhysicalSchemaInspectionResult> InspectHistoryAsync(
        PhysicalSchemaTarget target,
        CancellationToken cancellationToken) =>
        inner.InspectHistoryAsync(target, cancellationToken);
}
