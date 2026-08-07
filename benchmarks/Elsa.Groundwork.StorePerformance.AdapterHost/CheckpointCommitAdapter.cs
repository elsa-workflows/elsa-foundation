using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The <c>checkpoint-commit</c> leaf: binds the frozen Spec 094 checkpoint workload to a real Groundwork
/// provider driver.
///
/// It implements <see cref="IBenchmarkAdapter"/> and <see cref="IRuntimeCheckpointCommitWorkloadAdapter"/>
/// on one object, so the frozen scenario drives exactly the composition the harness then measures.
/// </summary>
internal sealed class CheckpointCommitAdapter : IBenchmarkAdapter, IRuntimeCheckpointCommitWorkloadAdapter
{
    /// <summary>
    /// Not a free choice. The frozen scenario stamps <c>TenantId = "tenant-checkpoint"</c> onto every
    /// committed <see cref="WorkflowExecutionState"/>, and the Groundwork bridges call
    /// <c>PersistenceAccessContext.EnsureTenantScope(state.TenantId)</c> before any provider I/O — so the
    /// correctness scope is dictated by the contract, not selected by this leaf.
    /// </summary>
    private const string CorrectnessScope = "tenant-checkpoint";

    /// <summary>
    /// The benchmark rows live in their own storage scope. The scenario asserts exact observable counts
    /// (1024 accepted commits, 4096 activities, 3072 durable values, 2048 outbox items), so a single
    /// benchmark row visible to the correctness read would fail correctness closed.
    /// </summary>
    private const string MeasuredScope = "bench-measured";

    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);

    private const string SeedFencedExecutions = "seed-fenced-executions";
    private const string CommitCheckpointBundle = "commit-checkpoint-bundle";
    private const string ReplayEquivalentCommit = "replay-equivalent-commit";
    private const string AttemptStaleFenceCommit = "attempt-stale-fence-commit";
    private const string ReopenAndReadCommittedBundle = "reopen-and-read-committed-bundle";

    /// <summary>
    /// One timed operation per named phase of the frozen <c>operationSequence</c>, in the same order. The
    /// harness records these ids on every sample, so a divergence would silently retarget a Tier B ceiling
    /// at a different operation.
    /// </summary>
    internal static readonly string[] OperationIds =
    [
        SeedFencedExecutions,
        CommitCheckpointBundle,
        ReplayEquivalentCommit,
        AttemptStaleFenceCommit,
        ReopenAndReadCommittedBundle
    ];

    /// <summary>
    /// Only the genuinely material provider settings are retained. The Groundwork package version, provider
    /// key and topology already travel as their own bound request fields, and the schema object count is a
    /// property of the applied target rather than of the provider's configuration.
    /// </summary>
    private static readonly string[] RetainedDiagnosticKeys = ["engine-version", "container-image"];

    private readonly RunRequest _request;
    private readonly List<ClientLease> _leases = [];
    private GroundworkProviderDriver? _driver;
    private ClientLease? _measured;
    private MeasuredFixtures? _fixtures;
    private IReadOnlyDictionary<string, string> _observedConfiguration = new Dictionary<string, string>(StringComparer.Ordinal);
    private string _activeScope = CorrectnessScope;

    private CheckpointCommitAdapter(RunRequest request) => _request = request;

    public static async ValueTask<IBenchmarkAdapter> CreateAsync(AdapterContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var provider = context.Request.Provider;
        if (!context.Workload.RequiredProviderEvidence.ContainsKey(provider))
            throw new PerformanceContractException(
                $"Provider '{provider}' is not admitted by the frozen topology contract for '{context.Workload.Id}'.");

        var adapter = new CheckpointCommitAdapter(context.Request);
        try
        {
            await adapter.OpenDriverAsync(cancellationToken);
            return adapter;
        }
        catch
        {
            await adapter.DisposeAsync();
            throw;
        }
    }

    public IReadOnlyList<IBenchmarkOperation> Operations { get; private set; } = [];

    /// <summary>
    /// Everything here is untimed: every workload input carries <c>"timedSetup": "excluded"</c>, and
    /// <c>MeasureAsync</c> times the whole of <c>InvokeAsync</c>. Schema application, client opening and row
    /// seeding therefore all belong on this side of the line.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        var driver = RequireDriver();
        await driver.ResetPhysicalAsync(cancellationToken);
        _observedConfiguration = await CaptureProviderConfigurationAsync(driver, cancellationToken);

        _activeScope = MeasuredScope;
        _measured = await OpenLeaseAsync(MeasuredScope, cancellationToken);
        _fixtures = await SeedMeasuredFixturesAsync(_measured.Client, cancellationToken);
        Operations = CreateOperations(_measured.Client, _fixtures);
    }

    /// <summary>
    /// Runs the frozen scenario exactly once, in its own storage scope. <c>ValidateCorrectness</c> compares
    /// the returned digest with the catalog's ratified value, so this is the gate every timed sample sits
    /// behind.
    /// </summary>
    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        var driver = RequireDriver();
        _activeScope = CorrectnessScope;
        var result = await new RuntimeCheckpointCommitWorkload().ExecuteAsync(this, cancellationToken);
        _activeScope = MeasuredScope;

        return new CorrectnessEvidence(
            result.ResultDigest,
            driver.Descriptor.ProviderVersion,
            driver.Descriptor.Topology.Description,
            _observedConfiguration,
            new NativePlanEvidence(
                _request.NativePlanIdentity,
                _request.NativePlanEvidenceReference,
                _request.NativePlanContentSha256,
                []));
    }

    public async ValueTask<RuntimeCheckpointCommitClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default)
    {
        var primary = await OpenLeaseAsync(_activeScope, cancellationToken);
        var secondary = await OpenLeaseAsync(_activeScope, cancellationToken);
        return new RuntimeCheckpointCommitClients(primary.Client, secondary.Client);
    }

    public async ValueTask<RuntimeCheckpointCommitClient> ReopenClientAsync(CancellationToken cancellationToken = default) =>
        (await OpenLeaseAsync(_activeScope, cancellationToken)).Client;

    public async ValueTask DisposeAsync()
    {
        // Reverse order: the driver refuses to dispose cleanly while it still tracks open client leases.
        for (var index = _leases.Count - 1; index >= 0; index--)
            await _leases[index].DisposeAsync();
        _leases.Clear();
        _measured = null;

        if (_driver is not null)
            await _driver.DisposeAsync();
        _driver = null;
    }

    private async Task OpenDriverAsync(CancellationToken cancellationToken)
    {
        var driver = GroundworkProviderDriverFactory.Create(_request.Provider);
        _driver = driver;
        await driver.InitializeAsync(cancellationToken);

        // Fail closed here rather than after a full correctness run: ValidateCorrectness would otherwise
        // reject the same mismatch minutes later, having already paid for 1024 durable commits.
        if (!string.Equals(driver.Descriptor.Topology.Description, _request.ProviderTopology, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"The '{_request.Provider}' driver reports topology '{driver.Descriptor.Topology.Description}', not the requested '{_request.ProviderTopology}'.");
        if (!string.Equals(driver.Descriptor.ProviderVersion, _request.ProviderVersion, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"The '{_request.Provider}' driver reports provider version '{driver.Descriptor.ProviderVersion}', not the requested '{_request.ProviderVersion}'.");
    }

    private GroundworkProviderDriver RequireDriver() =>
        _driver ?? throw new PerformanceContractException("The checkpoint adapter was used before its provider driver was opened.");

    /// <summary>
    /// Reads the driver's own sanitized self-description rather than restating connection-string literals
    /// here, so the recorded configuration cannot drift from the provider that produced the numbers.
    /// </summary>
    internal static async ValueTask<IReadOnlyDictionary<string, string>> CaptureProviderConfigurationAsync(
        GroundworkProviderDriver driver,
        CancellationToken cancellationToken)
    {
        var diagnostics = await driver.CaptureDiagnosticsAsync(cancellationToken);
        var observed = ParseDiagnostics(diagnostics.Content);
        var configuration = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in RetainedDiagnosticKeys)
            if (observed.TryGetValue(key, out var value))
                configuration[key] = value;

        if (configuration.Count == 0)
            throw new PerformanceContractException(
                $"The '{driver.Descriptor.ProviderKey}' driver reported no retainable provider configuration; an empty configuration is rejected by ArtifactSafety.");
        return configuration;
    }

    private static Dictionary<string, string> ParseDiagnostics(string content)
    {
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in content.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator > 0 && separator < line.Length - 1)
                parsed[line[..separator]] = line[(separator + 1)..];
        }
        return parsed;
    }

    /// <summary>
    /// One provider client, one service provider and one DI scope per workload client. The workload's
    /// <c>IsDistinctClient</c> rejects reference-aliasing of all seven store components, and the driver
    /// independently refuses two clients that share a service provider or document-store adapter.
    /// </summary>
    private async Task<ClientLease> OpenLeaseAsync(string scope, CancellationToken cancellationToken)
    {
        var providerClient = await RequireDriver().OpenPhysicalClientAsync(
            DocumentStoreAccess.Scoped(new StorageScope(scope)),
            cancellationToken);

        ServiceProvider? services = null;
        try
        {
            var boundedStore = providerClient.BoundedDocumentStore
                               ?? throw new PerformanceContractException(
                                   "The provider client exposes no bounded document store; the runtime bridges require an applied physical target.");

            var collection = new ServiceCollection();
            collection.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            // Registered before AddWorkflowRuntime: AddPersistenceCore uses TryAdd, so the default
            // "default"-scope accessor would otherwise win and every bridge store would read the wrong
            // storage scope.
            collection.AddSingleton<IPersistenceAccessContextAccessor>(GroundworkTestAccess.AccessContext(scope));
            collection.AddSingleton<IDocumentStore>(providerClient.DocumentStore);
            collection.AddSingleton(boundedStore);
            // AddWorkflowRuntime is TryAdd-only and supplies IRuntimeExecutionOwnershipService, TimeProvider
            // and IWorkflowExecutableRootWriteLeaseManager, none of which has a Groundwork replacement.
            // AddGroundworkRuntimeStores then RemoveAll's the in-memory defaults, so this order is required.
            collection.AddWorkflowRuntime();
            collection.AddGroundworkRuntimeStores();
            services = collection.BuildServiceProvider();

            var diScope = services.CreateAsyncScope();
            var resolved = diScope.ServiceProvider;
            var lease = new ClientLease(
                providerClient,
                services,
                diScope,
                new RuntimeCheckpointCommitClient(
                    resolved.GetRequiredService<IRuntimeCheckpointCommitStore>(),
                    resolved.GetRequiredService<IRuntimeExecutionOwnershipService>(),
                    resolved.GetRequiredService<IWorkflowExecutableStore>(),
                    resolved.GetRequiredService<IWorkflowExecutionStateStore>(),
                    resolved.GetRequiredService<IActivityExecutionStateStore>(),
                    resolved.GetRequiredService<IDurableValueStateStore>(),
                    resolved.GetRequiredService<IRuntimePostCommitOutboxStore>()));
            _leases.Add(lease);
            return lease;
        }
        catch
        {
            if (services is not null)
                await services.DisposeAsync();
            await providerClient.DisposeAsync();
            throw;
        }
    }

    // ---- measured fixtures -------------------------------------------------------------------------

    /// <summary>
    /// Seeds everything the timed operations read or fence against. Each named phase gets its own execution
    /// so that acquiring a successor lease for the stale-fence operation cannot invalidate the fence the
    /// commit operation depends on.
    /// </summary>
    private static async Task<MeasuredFixtures> SeedMeasuredFixturesAsync(
        RuntimeCheckpointCommitClient client,
        CancellationToken cancellationToken)
    {
        var executable = CreateExecutable();
        await client.Executables.SaveAsync(executable, cancellationToken);

        const string commitExecutionId = "bench-commit-execution";
        const string replayExecutionId = "bench-replay-execution";
        const string readExecutionId = "bench-read-execution";
        const string staleExecutionId = "bench-stale-execution";

        var commitLease = await client.Ownership.AcquireAsync(commitExecutionId, cancellationToken);

        var replayLease = await client.Ownership.AcquireAsync(replayExecutionId, cancellationToken);
        var replayCommit = CreateCommit("bench-replay", replayExecutionId, replayLease.ToFence(), executable.Identity);
        await client.Checkpoints.CommitAsync(replayCommit, Immediate, cancellationToken);

        var readLease = await client.Ownership.AcquireAsync(readExecutionId, cancellationToken);
        await client.Checkpoints.CommitAsync(
            CreateCommit("bench-read", readExecutionId, readLease.ToFence(), executable.Identity),
            Immediate,
            cancellationToken);

        // A superseded lease yields a permanently stale fence. A stale commit is rejected before any write,
        // so the same bundle can be resubmitted for the whole measurement window.
        var supersededLease = await client.Ownership.AcquireAsync(staleExecutionId, cancellationToken);
        var successor = await client.Ownership.AcquireAsync(staleExecutionId, cancellationToken);
        if (successor.FencingToken <= supersededLease.FencingToken)
            throw new PerformanceContractException("Ownership supersession did not issue a newer fencing token for the stale-fence fixture.");
        var staleCommit = CreateCommit("bench-stale", staleExecutionId, supersededLease.ToFence(), executable.Identity);

        return new MeasuredFixtures(executable.Identity, commitExecutionId, commitLease.ToFence(), replayCommit, staleCommit, readExecutionId);
    }

    private static IReadOnlyList<IBenchmarkOperation> CreateOperations(
        RuntimeCheckpointCommitClient client,
        MeasuredFixtures fixtures) =>
    [
        // Nothing but the elementary store call sits inside InvokeAsync. Every value the call needs is
        // either a fixture or derived from the ordinal before the timing window opens.
        new BenchmarkOperation(SeedFencedExecutions, (invocation, token) =>
            client.Ownership.AcquireAsync($"bench-acquire-{IdentityKey(invocation)}", token).AsTask()),

        new BenchmarkOperation(CommitCheckpointBundle, (invocation, token) =>
        {
            var commit = CreateCommit($"bench-commit-{IdentityKey(invocation)}", fixtures.CommitExecutionId, fixtures.CommitFence, fixtures.Executable);
            return client.Checkpoints.CommitAsync(commit, Immediate, token).AsTask();
        }),

        new BenchmarkOperation(ReplayEquivalentCommit, (_, token) =>
            client.Checkpoints.CommitAsync(fixtures.ReplayCommit, Immediate, token).AsTask()),

        new BenchmarkOperation(AttemptStaleFenceCommit, async (_, token) =>
        {
            try
            {
                await client.Checkpoints.CommitAsync(fixtures.StaleCommit, Immediate, token);
            }
            catch (RuntimeStaleFencingTokenException)
            {
                return;
            }
            throw new PerformanceContractException("The checkpoint store accepted a stale fencing token during measurement.");
        }),

        new BenchmarkOperation(ReopenAndReadCommittedBundle, (_, token) =>
            client.WorkflowExecutions.FindAsync(fixtures.ReadExecutionId, token).AsTask())
    ];

    /// <summary>
    /// Warm-up ordinals are negative (<c>InvokeAsync(-1L - i)</c> for 50 iterations) and measured ordinals
    /// start at zero, so prefixing keeps the two identity namespaces provably disjoint.
    /// </summary>
    internal static string IdentityKey(long invocation) => invocation < 0 ? $"w{-invocation}" : $"m{invocation}";

    // ---- representative bundle ---------------------------------------------------------------------

    /// <summary>
    /// The same shape the frozen scenario commits — four activity changes, three durable values, two outbox
    /// intents and a 512-byte payload — so the timed call is representative of the named phase it stands for.
    /// </summary>
    private static RuntimeCheckpointCommit CreateCommit(
        string checkpointId,
        string executionId,
        RuntimeExecutionFence fence,
        WorkflowExecutableIdentity executable)
    {
        var occurredAt = DateTimeOffset.UtcNow;
        var payload = Payload(checkpointId);
        var workflow = new WorkflowExecutionState(
            executionId, executable, WorkflowExecutionStatus.Running, null, occurredAt, occurredAt, occurredAt,
            null, null, null, MeasuredScope,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["checkpoint"] = checkpointId,
                ["payload"] = payload.GetRawText()
            });

        var activities = Enumerable.Range(0, RuntimeCheckpointCommitWorkload.ActivityChangesPerCheckpoint).Select(activity =>
            new ActivityExecutionState(
                new ActivityExecution($"activity-{checkpointId}-{activity:D2}", executionId, $"node-{activity:D2}", $"authored-{activity:D2}", "Elsa.Checkpoint", "1.0.0"),
                ActivityExecutionStatus.Running, null, 1, occurredAt, occurredAt, null, null, null, null, null,
                ActivitySchedulingProvenance.From(executionId, null, null, null, null, null, null, "checkpoint"), null, [], [], 0, 0,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["payload"] = payload.GetRawText() })).ToArray();

        var values = Enumerable.Range(0, RuntimeCheckpointCommitWorkload.DurableValueChangesPerCheckpoint).Select(value =>
            new DurableValueState(
                $"value-{checkpointId}-{value:D2}", executionId, $"value-{value:D2}", new RuntimeValueTypeDescriptor("json", "checkpoint", null),
                DurableValueLifecycle.Instance, DurableValueStorage.Inline, payload, null, activities[value].Execution.ActivityExecutionId, occurredAt,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["checkpoint"] = checkpointId })).ToArray();

        var intents = Enumerable.Range(0, RuntimeCheckpointCommitWorkload.OutboxEntriesPerCheckpoint).Select(intent =>
            new RuntimePostCommitIntent(
                $"intent-{checkpointId}-{intent:D2}", executionId, "runtime-checkpoint-continuation", occurredAt,
                activities[intent].Execution.ActivityExecutionId, $"intent-{checkpointId}-{intent:D2}", payload)).ToArray();

        var changes = new RuntimeCheckpointStateChangeSet(
            new RuntimeStateChange<WorkflowExecutionState>(executionId, RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()),
            null,
            activities.Select(activity => new RuntimeStateChange<ActivityExecutionState>(activity.Execution.ActivityExecutionId, RuntimeStateChangeOperation.Upsert, activity, new Dictionary<string, string>())).ToArray(),
            [],
            values.Select(value => new RuntimeStateChange<DurableValueState>(value.DurableValueId, RuntimeStateChangeOperation.Upsert, value, new Dictionary<string, string>())).ToArray(),
            [],
            []);

        var commit = new RuntimeCheckpointCommit(
            checkpointId,
            new RuntimeCheckpoint(checkpointId, "runtime-checkpoint-commit", executionId, occurredAt, activities.Select(activity => activity.Execution.ActivityExecutionId).ToArray(), new Dictionary<string, string>()),
            changes,
            intents,
            new Dictionary<string, string>()) { ExpectedFence = fence };
        return commit with { StateChanges = commit.StateChanges.WithPostCommitOutbox(RuntimePostCommitOutboxItems.CreatePendingChanges(commit)) };
    }

    private static WorkflowExecutable CreateExecutable()
    {
        var identity = new WorkflowExecutableIdentity("artifact-checkpoint-bench", "definition-checkpoint-bench", "version-checkpoint-bench", "1.0.0", "sha256:checkpoint-bench");
        return new WorkflowExecutable(
            identity,
            new ExecutableNode("checkpoint-root", "checkpoint-root", "Elsa.Checkpoint", "1.0.0", "checkpoint", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UtcNow,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static JsonElement Payload(string checkpointId)
    {
        var prefix = $"{{\"checkpoint\":\"{checkpointId}\",\"payload\":\"";
        var padding = RuntimeCheckpointCommitWorkload.PayloadBytes - Encoding.UTF8.GetByteCount(prefix) - Encoding.UTF8.GetByteCount("\"}");
        if (padding < 0)
            throw new PerformanceContractException("The benchmark checkpoint identity exceeds the frozen payload size.");
        using var document = JsonDocument.Parse(prefix + new string('x', padding) + "\"}");
        return document.RootElement.Clone();
    }

    private sealed record MeasuredFixtures(
        WorkflowExecutableIdentity Executable,
        string CommitExecutionId,
        RuntimeExecutionFence CommitFence,
        RuntimeCheckpointCommit ReplayCommit,
        RuntimeCheckpointCommit StaleCommit,
        string ReadExecutionId);

    private sealed record BenchmarkOperation(string Id, Func<long, CancellationToken, Task> Invoke) : IBenchmarkOperation
    {
        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) => Invoke(invocation, cancellationToken);
    }

    private sealed record ClientLease(
        GroundworkProviderClient Provider,
        ServiceProvider Services,
        AsyncServiceScope Scope,
        RuntimeCheckpointCommitClient Client) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await Scope.DisposeAsync();
            await Services.DisposeAsync();
            await Provider.DisposeAsync();
        }
    }
}
