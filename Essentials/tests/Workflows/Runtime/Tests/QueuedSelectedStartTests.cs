using System.Text.Json;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class QueuedSelectedStartTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable)]
    [InlineData(PersistenceMode.Balanced)]
    public async Task Selected_leaf_uses_real_dispatch_and_atomic_root_checkpoint_without_invoking_ancestors_or_siblings(PersistenceMode mode)
    {
        var owner = new Owner();
        await using var harness = Build(owner, mode);
        await Setup(harness, owner);
        var before = JsonSerializer.Serialize(owner.Executable);
        var result = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        var state = await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId);
        Assert.NotNull(state);
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.True(owner.Entry!.Matches(state.TestEntry));
        Assert.Equal(owner.Executable!.Identity, state.PinnedExecutable);
        var activity = Assert.Single(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
        Assert.Equal("selected", activity.Execution.ExecutableNodeId);
        Assert.Null(activity.ParentActivityExecutionId);
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);
        Assert.Equal(before, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(owner.Executable.Identity.ArtifactId)));
        Assert.True(owner.SeedReads >= 3);
    }

    [Theory]
    [InlineData("teardown")]
    [InlineData("expiry")]
    public async Task Scope_change_after_preparation_refuses_the_atomic_first_commit_and_invokes_no_activity(string profile)
    {
        var owner = new Owner();
        await using var harness = Build(owner, PersistenceMode.Balanced, services =>
            services.AddScoped<IRuntimeCheckpointCommitEnricher>(sp => new BeforeCommit(owner, sp, profile)));
        await Setup(harness, owner);
        _ = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        Assert.True(owner.AtomicHookRan);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    private static WorkflowExecutionHarness Build(Owner owner, PersistenceMode mode, Action<IServiceCollection>? extra = null) =>
        WorkflowExecutionHarness.Create().WithPersistenceMode(mode).ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(owner.Clock);
            services.AddSingleton<IWorkflowTestEntryContextResolver>(owner);
            services.AddSingleton<IRuntimeTestContextSeedSource>(owner);
            services.AddSingleton<IRuntimeTestPersistenceBoundary>(owner);
            services.AddSingleton<IRuntimeTestExecutionRoutingBoundary>(owner);
            extra?.Invoke(services);
        }).Build();

    private static async Task Setup(WorkflowExecutionHarness harness, Owner owner)
    {
        var selected = WorkflowExecutionHarness.NewProbeNode("selected");
        var root = new ExecutableNode("container", "container", typeof(ProbeActivity).FullName!, "1", "test.container", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots:
            [new("children", [WorkflowExecutionHarness.NewProbeNode("before"), selected, WorkflowExecutionHarness.NewProbeNode("after")])]);
        var original = WorkflowExecutionHarness.NewExecutable(root);
        var publication = await harness.PublishAsync(original, "ordinary-publication");
        owner.Executable = (await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(original.Identity.ArtifactId))!;
        owner.Source = publication with { SourceReferenceId = "test-source", Scope = WorkflowExecutableReferenceScope.TestRun,
            ActivationId = null, SlotId = null, ExpiresAt = owner.Clock.Now.AddMinutes(30) };
        await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(owner.Source);
        var scope = new WorkflowTestScope("selected-scope", owner.Clock.Now.AddMinutes(10), null, new(WorkflowExecutionPartition.DefaultValue));
        owner.Entry = new("context", "setup-v1", owner.Executable.Identity, "selected", scope, new("tester", "tester"));
        await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(scope, owner.Clock.Now);
    }

    private static WorkflowExecutionStartDispatchRequest Request(WorkflowExecutionHarness harness, Owner owner) => new(
        owner.Executable!.Identity.ArtifactId, "tester", harness.ExecutionId, null, null, null, null, null, null,
        WorkflowRunKind.TestRun, new WorkflowExecutableSourceSelection(owner.Source!.SourceReferenceId), WorkflowExecutableProvenanceRequirement.RequireLiveReference,
        null, null, null, owner.Entry!.Scope.Partition, owner.Entry.Authority, WorkflowExecutableStartAuthority.LiveReference,
        0, owner.Entry.Scope) { TestEntry = owner.Entry };

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Owner : IWorkflowTestEntryContextResolver, IRuntimeTestContextSeedSource,
        IRuntimeTestPersistenceBoundary, IRuntimeTestExecutionRoutingBoundary
    {
        internal Clock Clock { get; } = new();
        internal WorkflowExecutable? Executable;
        internal WorkflowExecutableSourceReference? Source;
        internal WorkflowTestEntry? Entry;
        internal int SeedReads;
        internal bool AtomicHookRan;
        public ValueTask<WorkflowTestEntry?> FindAsync(string contextId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Entry);
        public ValueTask<RuntimeTestContextSeed?> ResolveAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
            string workflowExecutionId, CancellationToken cancellationToken = default)
        { SeedReads++; return ValueTask.FromResult<RuntimeTestContextSeed?>(new(entry.ContextId, entry.ContextFingerprint, [], [])); }
        public bool CanPersistTestEntry(RuntimeTestPersistenceBoundaryContext context) => true;
        public ValueTask<bool> CanRouteTestEntryAsync(WorkflowTestEntry entry, IWorkflowExecutionActorProvider actors,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
    }

    private sealed class BeforeCommit(Owner owner, IServiceProvider services, string profile) : IRuntimeCheckpointCommitEnricher
    {
        public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            if (commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted && commit.StateChanges.WorkflowExecution?.State.TestEntry is not null)
            {
                owner.AtomicHookRan = true;
                if (profile == "expiry") owner.Clock.Now = owner.Entry!.Scope.ExpiresAt;
                else await services.GetRequiredService<IWorkflowTestScopeStore>().CloseAsync(new(owner.Entry!.Scope.ScopeId,
                    WorkflowTestScopeCloseReason.ExplicitTeardown, owner.Clock.Now), cancellationToken);
            }
            return commit;
        }
    }
}
