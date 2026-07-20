using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Reconciliation;
using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Events.Core.Contracts;
using Elsa.Locking.Core;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Tests;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Primitives.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

internal sealed class SqliteDesignPersistenceContractFixture : IDesignPersistenceContractFixture
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), $"elsa-design-groundwork-{Guid.NewGuid():N}");
    private readonly GroundworkBaselineTelemetry _telemetry;
    private readonly GroundworkTargetEventCapture _events;
    private ServiceProvider _services = null!;

    private SqliteDesignPersistenceContractFixture(GroundworkBaselineTelemetry telemetry)
    {
        _telemetry = telemetry;
        _events = new(telemetry);
    }

    public string Provider => "groundwork-sqlite";
    public int RestartCount { get; private set; }
    public int BoundScopeCount { get; private set; }
    public GroundworkSchemaEvidence SchemaEvidence { get; private set; } = null!;

    private string DatabasePath => Path.Join(_directory, "design.db");
    private string ConnectionString => $"Data Source={DatabasePath}";

    public static async Task<SqliteDesignPersistenceContractFixture> CreateAsync(
        GroundworkBaselineTelemetry telemetry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        var fixture = new SqliteDesignPersistenceContractFixture(telemetry);
        Directory.CreateDirectory(fixture._directory);
        fixture.SchemaEvidence = await GroundworkSchemaCli.ApplyFreshAsync(
            fixture.ConnectionString,
            cancellationToken);
        telemetry.RecordSchema(fixture.SchemaEvidence);
        await fixture.OpenAndAdmitAsync(cancellationToken);
        return fixture;
    }

    public IServiceScope CreateScope(string storageScope)
    {
        var scope = _services.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.Scoped(new PersistenceScope(storageScope)));
            BoundScopeCount++;
            _telemetry.RecordBoundScope();
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        await _services.DisposeAsync();
        RestartCount++;
        _telemetry.RecordRestart();
        await OpenAndAdmitAsync(cancellationToken);
    }

    public async Task ValidateReadinessAsync(CancellationToken cancellationToken = default)
    {
        var validation = await GroundworkSchemaCli.RunAsync(
            "validate",
            ConnectionString,
            cancellationToken);
        if (validation.ExitCode != 0)
            throw validation.ToException("Groundwork live schema validation failed");
        if (!string.Equals(validation.Report.GetProperty("outcome").GetString(), "ready", StringComparison.OrdinalIgnoreCase))
            throw validation.ToException("Groundwork live schema validation did not report ready");
    }

    public Task StageActivityReconciliationCandidatesAsync(
        string storageScope,
        IReadOnlyCollection<ActivityDefinitionVersion> candidates,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _events.Stage(storageScope, candidates);
        _telemetry.RecordCandidates(candidates.Count);
        return Task.CompletedTask;
    }

    public void ClearObservedEvents() => _events.Clear();

    public Task<IReadOnlyList<IEvent>> ReadObservedEventsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(_events.Snapshot());
    }

    public Task<IDesignAtomicityFaultLease> ArmAtomicityFaultAsync(
        DesignAtomicityFaultPlan plan,
        CancellationToken cancellationToken = default) =>
        throw GroundworkTargetAtomicityUnavailableException.Create();

    public Task<DesignAtomicityOperationResult> ExecuteAtomicityOperationAsync(
        DesignAtomicityOperationRequest request,
        CancellationToken cancellationToken = default) =>
        throw GroundworkTargetAtomicityUnavailableException.Create();

    public Task<DesignAtomicitySnapshot> ReadAtomicitySnapshotAsync(
        string storageScope,
        CancellationToken cancellationToken = default) =>
        throw GroundworkTargetAtomicityUnavailableException.Create();

    public async ValueTask DisposeAsync()
    {
        if (_services is not null)
            await _services.DisposeAsync();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A uniquely named test directory is harmless if SQLite releases a sidecar late.
        }
    }

    private async Task OpenAndAdmitAsync(CancellationToken cancellationToken)
    {
        _services = BuildServices();
        foreach (var initializer in _services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync(cancellationToken);
    }

    private ServiceProvider BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ISystemClock>(new DesignPersistenceFixtureData.FixedSystemClock(DesignPersistenceFixtureData.Epoch));
        services.AddSingleton<IIdentityGenerator, SequentialIdentityGenerator>();
        services.AddSingleton<IDistributedLockProvider, ImmediateDistributedLockProvider>();
        services.AddSingleton<IPayloadSerializer, DesignPersistenceFixtureData.DeterministicPayloadSerializer>();
        services.AddSingleton(_events);
        services.AddScoped<GroundworkTargetEventPublisher>();
        services.AddScoped<IInlineEventPublisher>(sp => sp.GetRequiredService<GroundworkTargetEventPublisher>());
        services.AddScoped<IDeferredEventPublisher>(sp => sp.GetRequiredService<GroundworkTargetEventPublisher>());
        services.AddSingleton<IActivityStructureService, EmptyActivityStructureService>();
        services.AddGroundworkSqliteUnifiedPersistence(ConnectionString, autoApplyOnStartup: false);
        new WorkflowDesignValidationsFeature().ConfigureServices(services);
        new ActivitiesDesignReconciliationFeature().ConfigureServices(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private sealed class SequentialIdentityGenerator : IIdentityGenerator
    {
        private int _next;
        public string Generate() => $"groundwork-{Interlocked.Increment(ref _next):D6}";
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

internal sealed class GroundworkTargetAtomicityUnavailableException : InvalidOperationException
{
    public const string Classification = "design-atomicity-operation-ledger-absent";

    private GroundworkTargetAtomicityUnavailableException()
        : base("The target atomicity operation ledger and provider-neutral fault seam are not implemented.")
    {
    }

    public static GroundworkTargetAtomicityUnavailableException Create() => new();
}

internal sealed class GroundworkTargetEventCapture(GroundworkBaselineTelemetry telemetry)
{
    private readonly ConcurrentQueue<IEvent> _events = new();
    private readonly ConcurrentDictionary<string, IReadOnlyCollection<ActivityDefinitionVersion>> _candidates =
        new(StringComparer.Ordinal);

    public void Stage(string scope, IReadOnlyCollection<ActivityDefinitionVersion> candidates) =>
        _candidates[scope] = candidates.ToArray();

    public IReadOnlyCollection<ActivityDefinitionVersion> Candidates(string scope) =>
        _candidates.TryGetValue(scope, out var candidates) ? candidates : [];

    public void Clear()
    {
        while (_events.TryDequeue(out _))
        {
        }
    }

    public IReadOnlyList<IEvent> Snapshot() => _events.ToArray();
    public void Record(IEvent @event)
    {
        _events.Enqueue(@event);
        telemetry.RecordEvent(@event.GetType());
    }

    public void RecordReconciliationPass() => telemetry.RecordReconciliationPass();
}

internal sealed class GroundworkTargetEventPublisher(
    IServiceProvider services,
    IPersistenceAccessContextAccessor accessContext,
    GroundworkTargetEventCapture capture) : IInlineEventPublisher, IDeferredEventPublisher
{

    Task IInlineEventPublisher.Publish(IEvent @event, CancellationToken cancellationToken) =>
        PublishAsync(@event, cancellationToken);

    Task IDeferredEventPublisher.Publish(IEvent @event, CancellationToken cancellationToken) =>
        PublishAsync(@event, cancellationToken);

    private async Task PublishAsync(IEvent @event, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        capture.Record(@event);
        switch (@event)
        {
            case OnGroundworkStorageComposing composing:
                foreach (var handler in services.GetServices<IEventHandler<OnGroundworkStorageComposing>>())
                    await handler.Handle(composing, cancellationToken);
                break;
            case Elsa.Workflows.Design.Validations.Core.Events.OnDraftValidating validating:
                foreach (var handler in services.GetServices<
                             IEventHandler<Elsa.Workflows.Design.Validations.Core.Events.OnDraftValidating>>())
                    await handler.Handle(validating, cancellationToken);
                break;
            case OnActivityVersionsReconciling reconciling:
                capture.RecordReconciliationPass();
                foreach (var handler in services.GetServices<IEventHandler<OnActivityVersionsReconciling>>())
                    await handler.Handle(reconciling, cancellationToken);
                var storageScope = accessContext.Current.Scope?.Value
                                   ?? throw new InvalidOperationException(
                                       "Activity reconciliation requires a scope-bound persistence access context.");
                foreach (var candidate in capture.Candidates(storageScope))
                    reconciling.Versions.Add(candidate);
                break;
        }
    }
}

internal sealed class ImmediateDistributedLockProvider : IDistributedLockProvider
{
    public IDistributedSynchronizationHandle? TryAcquireLock(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) => new Handle();

    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());

    public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(
        string name,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());

    private sealed class Handle : IDistributedSynchronizationHandle
    {
        public CancellationToken HandleLostToken => CancellationToken.None;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed record GroundworkSchemaEvidence(string TargetFingerprint, string PlanFingerprint);

internal sealed class GroundworkBaselineTelemetry
{
    private readonly object _sync = new();
    private readonly List<string> _eventTypes = [];
    private GroundworkSchemaEvidence? _schema;
    private int _restartCount;
    private int _boundScopeCount;
    private int _candidateCount;
    private int _reconciliationPassCount;

    public void RecordSchema(GroundworkSchemaEvidence schema)
    {
        lock (_sync)
        {
            if (_schema is not null && _schema != schema)
                throw new InvalidOperationException("Groundwork schema fingerprints drifted within one baseline run.");
            _schema = schema;
        }
    }

    public void RecordRestart() => Interlocked.Increment(ref _restartCount);
    public void RecordBoundScope() => Interlocked.Increment(ref _boundScopeCount);
    public void RecordCandidates(int count) => Interlocked.Add(ref _candidateCount, count);
    public void RecordReconciliationPass() => Interlocked.Increment(ref _reconciliationPassCount);

    public void RecordEvent(Type eventType)
    {
        lock (_sync)
            _eventTypes.Add(eventType.FullName ?? eventType.Name);
    }

    public GroundworkBaselineTelemetrySnapshot Snapshot()
    {
        lock (_sync)
        {
            var schema = _schema
                         ?? throw new InvalidOperationException("No Groundwork schema evidence was recorded.");
            var canonicalEventTypes = string.Join(
                "\n",
                _eventTypes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            return new(
                schema.TargetFingerprint,
                schema.PlanFingerprint,
                _restartCount,
                _boundScopeCount,
                _candidateCount,
                _reconciliationPassCount,
                Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalEventTypes))).ToLowerInvariant());
        }
    }
}

internal sealed record GroundworkBaselineTelemetrySnapshot(
    string TargetFingerprint,
    string PlanFingerprint,
    int RestartCount,
    int BoundScopeCount,
    int ReconciliationCandidateCount,
    int ReconciliationPassCount,
    string EventTypeDigest);

internal static class GroundworkSchemaCli
{
    private const string ConnectionEnvironmentVariable = "ELSA_DESIGN_GROUNDWORK_SQLITE_CONNECTION";

    public static async Task<GroundworkSchemaEvidence> ApplyFreshAsync(
        string connectionString,
        CancellationToken cancellationToken)
    {
        var offline = await RunAsync("validate", null, cancellationToken, "--offline");
        if (offline.ExitCode != 0)
            throw offline.ToException("Groundwork offline schema validation failed");

        var plan = await RunAsync("plan", connectionString, cancellationToken);
        if (plan.ExitCode != 2)
            throw plan.ToException("A fresh Groundwork SQLite target did not report a pending plan");

        var apply = await RunAsync("apply", connectionString, cancellationToken, "--safe");
        if (apply.ExitCode != 0)
            throw apply.ToException("Groundwork safe schema apply failed");

        return new(
            apply.Report.GetProperty("target").GetProperty("fingerprint").GetString()
            ?? throw new InvalidOperationException("Groundwork apply omitted the target fingerprint."),
            apply.Report.GetProperty("planFingerprint").GetString()
            ?? plan.Report.GetProperty("planFingerprint").GetString()
            ?? throw new InvalidOperationException("Groundwork apply omitted the plan fingerprint."));
    }

    public static async Task<GroundworkCliResult> RunAsync(
        string command,
        string? connectionString,
        CancellationToken cancellationToken,
        params string[] extraArguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = FindRepositoryRoot(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
                 {
                     "tool", "run", "groundwork", "--", command,
                     "--manifest-assembly", typeof(GroundworkAllFeaturesDeploymentSchema).Assembly.Location,
                     "--manifest-type", typeof(GroundworkAllFeaturesDeploymentSchema).FullName!,
                     "--provider", "sqlite",
                     "--output", "json"
                 })
            start.ArgumentList.Add(argument);
        if (connectionString is not null)
        {
            start.Environment[ConnectionEnvironmentVariable] = connectionString;
            start.ArgumentList.Add("--connection-env");
            start.ArgumentList.Add(ConnectionEnvironmentVariable);
        }
        foreach (var argument in extraArguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("Could not start the Groundwork schema tool.");
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var output = await outputTask;
        var error = await errorTask;
        try
        {
            using var document = JsonDocument.Parse(output);
            return new(process.ExitCode, document.RootElement.Clone(), error);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Groundwork schema tool emitted invalid JSON (exit {process.ExitCode}); stderr digest {Digest(error)}.",
                exception);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Join(current.FullName, "Elsa.Server.slnx")))
            current = current.Parent;
        return current?.FullName
               ?? throw new InvalidOperationException("Could not locate the repository root for Groundwork.Tool.");
    }

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

internal sealed record GroundworkCliResult(int ExitCode, JsonElement Report, string StandardError)
{
    public Exception ToException(string message) =>
        new InvalidOperationException($"{message} (exit {ExitCode}; stderr digest {Digest(StandardError)}).");

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
