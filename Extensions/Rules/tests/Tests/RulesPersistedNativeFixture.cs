using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Events;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Persistence.Core;
using Elsa.Rules.Persistence.EntityFrameworkCore;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Runtime;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Rules.Tests;

/// <summary>Shared durable native execution setup for the hand-built Flowchart and compiled Sequence journeys.</summary>
internal sealed class RulesPersistedNativeFixture : IAsyncDisposable
{
    private readonly string _databasePath;
    private readonly WorkflowExecutable _executable;
    private WorkflowExecutionHarness? _harness;

    private RulesPersistedNativeFixture(WorkflowExecutable executable, string workflowExecutionId, string databasePath,
        RuleSemanticPin implementation)
    {
        _executable = executable;
        WorkflowExecutionId = workflowExecutionId;
        _databasePath = databasePath;
        Implementation = implementation;
    }

    internal const string RulesNodeId = "rules";
    internal const string AfterRulesNodeId = "after-rules";
    internal static WorkflowExecutionPartition Partition => WorkflowExecutionHarness.Partition;
    internal WorkflowExecutionHarness Harness => _harness ?? throw new InvalidOperationException("The durable native fixture is not initialized.");
    internal string WorkflowExecutionId { get; }
    internal RuleSemanticPin Implementation { get; }

    internal static async Task<RulesPersistedNativeFixture> CreateAsync(
        WorkflowExecutable executable,
        string workflowExecutionId,
        Action<IServiceCollection>? configureServices = null,
        RuleSemanticPin? implementation = null)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);

        var selectedImplementation = implementation ?? RuleTestData.Invocation().ExpectedImplementation;
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-rules-native-{Guid.NewGuid():N}.db");
        var fixture = new RulesPersistedNativeFixture(executable, workflowExecutionId, databasePath, selectedImplementation);
        try
        {
            fixture._harness = WorkflowExecutionHarness.Create()
                .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
                .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
                .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
                .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
                .WithFeature(services => new RulesRuntimeFeature { MaxConcurrentAttempts = 1, PollIntervalMilliseconds = 10 }.ConfigureServices(services))
                .WithFeature(services => new RulesWorkflowsRuntimeFeature { MaxConcurrentDeliveries = 1, PollIntervalMilliseconds = 10 }.ConfigureServices(services))
                .WithFeature(services => new RulesRuntimeEntityFrameworkCoreFeature
                {
                    ConnectionString = $"Data Source={databasePath};Pooling=False"
                }.ConfigureServices(services))
                .ConfigureServices(services =>
                {
                    services.AddLogging();
                    services.AddSingleton<IRuleImplementationIdentitySource>(new TestImplementationIdentitySource(selectedImplementation));
                    configureServices?.Invoke(services);
                    services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                    {
                        Provider = "Sqlite",
                        ConnectionString = $"Data Source={databasePath};Pooling=False",
                        CacheWorkflowExecutables = false,
                        RecoveryContinuationSigningKey = "rules-native-test-recovery-key-32-bytes",
                        HierarchyCursorSigningKey = "rules-native-test-hierarchy-key-32-bytes"
                    }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                })
                .WithPersistenceMode(PersistenceMode.Durable)
                .Build(executable.Identity, workflowExecutionId,
                    Enumerable.Range(1, 64).Select(index => $"{workflowExecutionId}-activity-{index}"));

            foreach (var initializer in fixture.Harness.Services.GetServices<IShellInitializer>())
                await initializer.InitializeAsync();
            fixture.Harness.InitializeActivityTypes();
            return fixture;
        }
        catch
        {
            await fixture.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_harness is not null)
        {
            await _harness.DisposeAsync();
            _harness = null;
        }

        foreach (var file in new[] { _databasePath, $"{_databasePath}-wal", $"{_databasePath}-shm" })
            if (File.Exists(file))
                File.Delete(file);
    }

    internal async Task<WorkflowExecutableSourceReference> PublishAsync(string sourceReferenceId)
    {
        // Harness.PublishAsync normalizes persistence and CLR contracts for hand-built graphs. These graphs
        // already carry their contracts, and the compiled artifact must remain byte-for-byte behavioral input.
        // Save only publication inputs here; execution state is created by the real start dispatcher below.
        var identity = _executable.Identity;
        var reference = new WorkflowExecutableSourceReference(
            sourceReferenceId, identity.ArtifactId, "WorkflowDefinitionVersion", identity.DefinitionVersionId,
            identity.ArtifactVersion, identity.DefinitionId, identity.DefinitionVersionId, identity.ArtifactVersion,
            _executable.CreatedAt, _executable.CreatedAt, WorkflowExecutableReferenceScope.Published,
            ActivationId: $"publication-{sourceReferenceId}", SlotId: $"slot-{sourceReferenceId}", TenantId: Partition.Value);
        await using (var target = await CreatePartitionScopeAsync())
        {
            await target.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(_executable);
            await target.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
        }

        await using var verification = await CreatePartitionScopeAsync();
        var persisted = await verification.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>()
            .FindAsync(reference.ArtifactId);
        AssertSameExecutable(_executable, Assert.IsType<WorkflowExecutable>(persisted));
        return reference;
    }

    internal async Task<WorkflowExecutionStartDispatchResult> StartPublishedAsync(WorkflowExecutableSourceReference reference)
    {
        await using var scope = await CreatePartitionScopeAsync();
        var authority = new WorkflowExecutionAuthoritySnapshot(
            systemIdentity: "rules-persisted-native-test", rootInitiator: "rules-persisted-native-test");
        var started = await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId: reference.ArtifactId, requestedBy: authority.SystemIdentity,
                workflowExecutionId: WorkflowExecutionId, idempotencyKey: $"start:{WorkflowExecutionId}",
                metadata: null, variables: null, inputs: null, stimulusInput: null, triggerNodeId: null,
                runKind: WorkflowRunKind.PublishedRun,
                sourceSelection: new WorkflowExecutableSourceSelection(reference.SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null, correlationId: null,
                tenantId: Partition.Value, partition: Partition, authority: authority));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, started.CommandDispatch.Status);
        return started;
    }

    internal async Task SweepPartitionAsync()
    {
        await using var scope = await CreatePartitionScopeAsync();
        var resumption = scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>();
        for (var pass = 0; pass < 8; pass++)
            if ((await resumption.SweepAsync(new RuntimeResumptionSweepRequest())).OutboxAttemptedCount == 0)
                return;
        throw new InvalidOperationException("Native post-commit intents did not settle within eight sweeps.");
    }

    internal async Task<IReadOnlyCollection<ActivityExecutionState>> ActivityStatesAsync()
    {
        await using var scope = await CreatePartitionScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(WorkflowExecutionId);
    }

    internal async Task<WorkflowExecutionState?> WorkflowStateAsync()
    {
        await using var scope = await CreatePartitionScopeAsync();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>()
            .FindAsync(WorkflowExecutionId);
    }

    internal async ValueTask<PersistenceOperationScope> CreatePartitionScopeAsync() =>
        await Harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
            .CreateAsync(new PersistenceScope(Partition.Value));

    internal async Task<RuleRunIdentity> ReadRulesIdentityAsync(string executableNodeId = RulesNodeId)
    {
        var activity = StateFor(await ActivityStatesAsync(), executableNodeId);
        var request = RulesNativeBoundary.ReadState(activity);
        return new(request.Scope, request.RunId);
    }

    internal async Task<RuleRunReadResult> FindRunAsync(RuleRunIdentity identity)
    {
        using var scope = Harness.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRuleRunReader>().FindAsync(identity);
    }

    internal async Task<RuleTerminalReadResult> ReadTerminalAsync(RuleRunIdentity identity)
    {
        using var scope = Harness.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRuleRunReader>().ReadTerminalAsync(identity);
    }

    internal async Task<RuleTerminalReadResult> DriveRulesToTerminalAsync(RuleRunIdentity identity)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            using var scope = Harness.Services.CreateScope();
            var reader = scope.ServiceProvider.GetRequiredService<IRuleRunReader>();
            Assert.Equal(RuleRunReadDisposition.Found, (await reader.FindAsync(identity)).Disposition);
            var terminal = await reader.ReadTerminalAsync(identity);
            if (terminal.Disposition == RuleTerminalReadDisposition.Found)
                return terminal;
            Assert.Equal(RuleTerminalReadDisposition.NotTerminal, terminal.Disposition);
            Assert.True(await scope.ServiceProvider.GetRequiredService<RuleExecution>()
                    .ExecuteOneAsync("native-test-computation", CancellationToken.None),
                "The persisted Rules invocation stopped making progress before a terminal result was stored.");
        }

        return await ReadTerminalAsync(identity);
    }

    internal async Task<bool> DeliverOneAsync(string owner)
    {
        await using var scope = Harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RulesNativeDeliveryExecution>()
            .ExecuteOneAsync(owner);
    }

    internal async Task<RuleDeliveryClaimDisposition> ClaimDeliveryDispositionAsync(string owner)
    {
        using var scope = Harness.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IRuleRunStore>()
            .ClaimDeliveryAsync(new(owner, [RulesNativeBoundary.FormatVersion]))).Disposition;
    }

    internal static ActivityExecutionState StateFor(IEnumerable<ActivityExecutionState> states, string nodeId) =>
        Assert.Single(states, item => item.Execution.ExecutableNodeId == nodeId);

    private static string ContentHash(WorkflowExecutable executable)
    {
        var hasher = new WorkflowExecutableHasher();
        return executable.InputContract is { } contract
            ? hasher.ComputeHash(executable.RootActivity, contract, executable.Dependencies,
                executable.PersistenceMode, executable.WorkflowVariables, executable.IncidentStrategy)
            : hasher.ComputeHash(executable.RootActivity);
    }

    private static void AssertSameExecutable(WorkflowExecutable expected, WorkflowExecutable actual)
    {
        Assert.Equal(expected.Identity, actual.Identity);
        Assert.Equal(expected.Identity.ArtifactHash, ContentHash(expected));
        Assert.Equal(expected.Identity.ArtifactHash, ContentHash(actual));
        Assert.Equal(expected.CreatedAt, actual.CreatedAt);
        Assert.Equal(expected.IncidentStrategy, actual.IncidentStrategy);
        Assert.Equal(expected.PersistenceMode, actual.PersistenceMode);
        Assert.Equal(expected.CompatibilityMetadata.OrderBy(item => item.Key, StringComparer.Ordinal),
            actual.CompatibilityMetadata.OrderBy(item => item.Key, StringComparer.Ordinal));
        Assert.Equal(expected.ResumeTargets.Keys.Order(StringComparer.Ordinal), actual.ResumeTargets.Keys.Order(StringComparer.Ordinal));
        foreach (var (id, target) in expected.ResumeTargets)
        {
            var persisted = actual.ResumeTargets[id];
            Assert.Equal(target.ResumeTargetId, persisted.ResumeTargetId);
            Assert.Equal(target.ExecutableNodeId, persisted.ExecutableNodeId);
            Assert.Equal(target.HandlerKey, persisted.HandlerKey);
            Assert.Equal(target.LocalResumeTargetId, persisted.LocalResumeTargetId);
            Assert.Equal(target.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal),
                persisted.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal));
        }
        Assert.Equal(expected.Dependencies.Count, actual.Dependencies.Count);
        foreach (var dependency in expected.Dependencies)
        {
            var persisted = Assert.Single(actual.Dependencies, item => item.ArtifactId == dependency.ArtifactId);
            Assert.Equal(dependency.ArtifactHash, persisted.ArtifactHash);
            Assert.Equal(dependency.DispatchNodeIds, persisted.DispatchNodeIds);
        }
    }

    private sealed class TestImplementationIdentitySource(RuleSemanticPin identity) : IRuleImplementationIdentitySource
    {
        public ValueTask<RuleSemanticPin?> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult<RuleSemanticPin?>(identity);
        }
    }
}
