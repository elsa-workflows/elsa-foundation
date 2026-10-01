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
/// service provider over one database, so it shares nothing with the other nodes but the database. However the
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

            Assert.Equal(1, (await DeliverAsync(first)).StartedCount);
        }

        await using var second = await StartNodeAsync(provider, connectionString);
        var redelivery = await DeliverAsync(second);

        Assert.Equal(0, redelivery.StartedCount);
        Assert.Equal(1, redelivery.SkippedStartCount);
        await AssertStartedOnceAsync(second);
    }

    /// <summary>
    /// The second node's delivery passes the dispatcher's duplicate check while nothing has started yet, and is held
    /// right after it. The first node then starts the workflow and commits it. Released, the second node enqueues the
    /// same start into the running execution, and it must converge there instead of running again.
    /// </summary>
    public static async Task ARacingDeliveryOnAnotherNodeConvergesOnTheFirstStartAsync(string provider, string connectionString)
    {
        var racePoint = new StartRacePoint(Keyed.WorkflowExecutionId);
        await using var first = await StartNodeAsync(provider, connectionString);
        await using var second = await StartNodeAsync(provider, connectionString, racePoint.Install);
        await PublishMessageStartWorkflowAsync(first);

        var racing = DeliverAsync(second);
        await racePoint.ReachedAsync();
        Assert.Equal(1, (await DeliverAsync(first)).StartedCount);
        racePoint.Release();
        await racing;

        await AssertStartedOnceAsync(first);
    }

    private static Task AssertStartedOnceAsync(WorkflowExecutionHarness node) =>
        KeyedStartNodes.AssertStartedOnceAsync(node, Keyed.WorkflowExecutionId);

    private static Task<WorkflowExecutionHarness> StartNodeAsync(
        string provider,
        string connectionString,
        Action<IServiceCollection>? configure = null) =>
        KeyedStartNodes.StartAsync(provider, connectionString, configure);

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
        var router = new KeyedStartNodes.ScopedStimulusRouter(node);
        await new PublishStimulusExecutor(router).HandleAsync(Intent);
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
