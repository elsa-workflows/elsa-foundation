using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Readability;
using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Primitives;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Primitives.Services;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// A committed PublishStimulus intent is delivered at least once: a peer re-claims it when the first deliverer's lease
/// lapses, and a crash between dispatch and completion redelivers it after a restart (#2195). Every node here is a fresh
/// service provider over one database. Separate constructed clocks/fleet answers control this component;
/// the real committed source, claim fencing, cohort and actor execution are durable provider behavior. However the
/// deliveries interleave, the message-start workflow must start once: one execution, one run of its start.
/// Written once so SQLite and each native provider are held to the same outcome.
/// </summary>
internal static class PublishStimulusStartContract
{
    private const string EventName = "order-placed";

    // The started workflow waits on another event, so it stays running: a start that ran twice would collide with it
    // rather than meet a terminal execution the drainer refuses to touch.
    private static readonly WorkflowExecutable Executable = RuntimeEventExecutableTestFixture.Create("message-start");
    private static readonly RuntimePostCommitIntent Intent = PublishIntent();
    private static readonly KeyedWorkflowStartIdentity Keyed = KeyedWorkflowStartIdentity.For(Intent.IdempotencyKey!, Executable.Identity.ArtifactId);

    /// <summary>The first node starts the workflow; a later node delivering the same intent again starts nothing.</summary>
    public static async Task ARedeliveryOnAnotherNodeStartsTheWorkflowOnceAsync(string provider, string connectionString)
    {
        await using (var first = await StartNodeAsync(provider, connectionString))
        {
            await PublishMessageStartWorkflowAsync(first);
            await CommitSourceAsync(first);
            Assert.Equal(1, (await DeliverAsync(first)).StartedCount);
        }

        await using var second = await StartNodeAsync(provider, connectionString, now: Intent.RecordedAt.AddMinutes(3));
        var redelivery = await DeliverAsync(second);

        Assert.Equal(0, redelivery.StartedCount);
        Assert.Equal(1, redelivery.SkippedStartCount);
        await AssertStartedOnceAsync(second);
    }

    /// <summary>
    /// The second node's delivery passes the dispatcher's duplicate check while nothing has started yet, and is held
    /// right after it. The first node then starts the workflow and commits it. After source-claim takeover, the released stale node must refuse before enqueue instead of running again.
    /// </summary>
    public static async Task ARacingDeliveryOnAnotherNodeConvergesOnTheFirstStartAsync(string provider, string connectionString)
    {
        var racePoint = new StartRacePoint(Keyed.WorkflowExecutionId);
        await using var first = await StartNodeAsync(provider, connectionString, now: Intent.RecordedAt.AddMinutes(3));
        await using var second = await StartNodeAsync(provider, connectionString, racePoint.Install);
        await PublishMessageStartWorkflowAsync(second);
        await CommitSourceAsync(second);
        var racing = DeliverAsync(second);
        var reached = racePoint.ReachedAsync();
        if (await Task.WhenAny(racing, reached) == racing) await racing;
        await reached;
        try { Assert.Equal(1, (await DeliverAsync(first)).StartedCount); }
        finally { racePoint.Release(); }
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => racing);

        await AssertStartedOnceAsync(first);
    }

    private static async Task AssertStartedOnceAsync(WorkflowExecutionHarness node)
    {
        await using var scope = await node.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(new(PersistenceScope.DefaultValue));
        var services = scope.ServiceProvider;
        var executions = await services.GetRequiredService<IWorkflowExecutionStateStore>().ListAsync();
        Assert.Equal(2, executions.Count);
        var execution = Assert.Single(executions, item => item.WorkflowExecutionId == Keyed.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Running, execution.Status);
        Assert.Single(await services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(execution.WorkflowExecutionId));
        Assert.Empty(await services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(execution.WorkflowExecutionId));
        Assert.Empty(await services.GetRequiredService<IIncidentStateStore>().ListAsync(execution.WorkflowExecutionId));
    }

    private static Task<WorkflowExecutionHarness> StartNodeAsync(string provider, string connectionString,
        Action<IServiceCollection>? configure = null, DateTimeOffset? now = null) =>
        KeyedStartNodes.StartAsync(provider, connectionString, services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(new FakeTimeProvider(now ?? Intent.RecordedAt)));
            services.AddSingleton<IEfSchemaFleet, SingleCurrentMember>();
            services.AddEfSchemaDormancy();
            configure?.Invoke(services);
        });

    // Real admitted gates/finalization records and shared observations, with controlled fleet answers for this component.
    private sealed class SingleCurrentMember : IEfSchemaFleet
    {
        public EfSchemaFleetStanding GetLocalStanding() => new(new("component-host", "component-incarnation"), false);
        public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public TimeSpan SettleMargin => TimeSpan.Zero;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }

    private static async Task CommitSourceAsync(WorkflowExecutionHarness node)
    {
        await using var scope = await node.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(new(PersistenceScope.DefaultValue));
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new("wfexec-publisher", Executable.Identity,
            WorkflowExecutionStatus.Running, null, Intent.RecordedAt, Intent.RecordedAt, Intent.RecordedAt, null, null, null, null,
            new Dictionary<string, string>()) { RunKind = WorkflowRunKind.PublishedRun });
        var commit = new RuntimeCheckpointCommit("publish-source", new("publish-source-checkpoint", RuntimeCheckpointNames.PostCommitIntentRecorded,
            "wfexec-publisher", Intent.RecordedAt, [], new Dictionary<string, string>()), new(null, null, [], [], [], [], []), [Intent], new Dictionary<string, string>());
        await services.GetRequiredService<RuntimeCheckpointCommitter>().CommitAsync(commit);
    }

    private static async Task PublishMessageStartWorkflowAsync(WorkflowExecutionHarness node)
    {
        var reference = await node.PublishAsync(Executable, "ref-message-start");
        await using var scope = node.Services.CreateAsyncScope();
        // The binding targets a node other than the root, so the root waits instead of completing as the trigger target.
        await scope.ServiceProvider.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(new WorkflowTriggerBinding(
            TriggerBindingId: WorkflowTriggerBinding.BuildId(Executable.Identity.ArtifactId, "message-start-trigger", EventStimulus.Hash(EventName)),
            ArtifactId: Executable.Identity.ArtifactId,
            DefinitionId: Executable.Identity.DefinitionId,
            ArtifactVersion: Executable.Identity.ArtifactVersion,
            ArtifactHash: Executable.Identity.ArtifactHash,
            ExecutableNodeId: "message-start-trigger",
            StimulusType: EventStimulus.StimulusType,
            StimulusHash: EventStimulus.Hash(EventName),
            CorrelationScope: null,
            Metadata: new Dictionary<string, string>(),
            CreatedAt: WorkflowExecutionHarness.Timestamp,
            ActivationId: reference.ActivationId,
            SlotId: reference.SlotId));
    }

    private static async Task<StimulusRoutingResult> DeliverAsync(WorkflowExecutionHarness node)
    {
        await using var scope = await node.Services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScopeAsync(new(PersistenceScope.DefaultValue));
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var claim = Assert.Single(await ((IRuntimePostCommitOutboxClaimStore)store).ClaimAsync(new("deliverer-" + Guid.NewGuid().ToString("N"),
            services.GetRequiredService<TimeProvider>().GetUtcNow(), TimeSpan.FromMinutes(1), 1, intentKind: Intent.Kind)));
        var router = new KeyedStartNodes.ScopedStimulusRouter(node);
        await ((IRuntimePostCommitDurableSourceIntentHandler)new PublishStimulusExecutor(router)).HandleAsync(new(claim, new(PersistenceScope.DefaultValue)));
        return router.LastResult!;
    }

    // The intent a committed PublishEvent leaves in the outbox: its idempotency key is fixed by the publishing activity
    // execution, so every delivery of it carries the same key.
    private static RuntimePostCommitIntent PublishIntent()
    {
        var buffer = new PublishStimulusStagingBuffer();
        buffer.StagePublishStimulus(new PublishStimulusRequest("wfexec-publisher", "actexec-publish", EventName));
        return Assert.Single(buffer.TakePublishStimuli("wfexec-publisher", "actexec-publish"));
    }

    /// <summary>
    /// Holds a node's first start activation of one execution: the start dispatcher activates the actor after its
    /// duplicate check and before it enqueues the start, so the hold sits exactly between the two.
    /// </summary>
    private sealed class StartRacePoint(string workflowExecutionId)
    {
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _held;

        public void Install(IServiceCollection services)
        {
            var original = services.Last(descriptor => descriptor.ServiceType == typeof(IWorkflowExecutionActorProvider));
            services.Remove(original);
            services.AddSingleton<IWorkflowExecutionActorProvider>(provider => new HeldActorProvider(
                (IWorkflowExecutionActorProvider)(original.ImplementationInstance
                    ?? original.ImplementationFactory?.Invoke(provider)
                    ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!)),
                this));
        }

        public Task ReachedAsync() => _reached.Task.WaitAsync(Patience);

        public void Release() => _released.TrySetResult();

        private async ValueTask HoldAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken)
        {
            if (request.Reason != WorkflowExecutionActorActivationReason.Start ||
                !StringComparer.Ordinal.Equals(request.WorkflowExecutionId, workflowExecutionId) ||
                Interlocked.Exchange(ref _held, 1) == 1)
            {
                return;
            }

            _reached.TrySetResult();
            await _released.Task.WaitAsync(Patience, cancellationToken);
        }

        private sealed class HeldActorProvider(IWorkflowExecutionActorProvider inner, StartRacePoint racePoint)
            : IWorkflowExecutionActorProvider, IDisposable
        {
            public WorkflowExecutionActorCapabilities Capabilities => inner.Capabilities;

            public async ValueTask<IWorkflowExecutionActor> GetAgentAsync(
                WorkflowExecutionActorActivationRequest request,
                CancellationToken cancellationToken = default)
            {
                await racePoint.HoldAsync(request, cancellationToken);
                return await inner.GetAgentAsync(request, cancellationToken);
            }

            public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) =>
                inner.PassivateAsync(request, cancellationToken);

            public void Dispose() => (inner as IDisposable)?.Dispose();
        }
    }
}
