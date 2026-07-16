using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ActivityTransitionKind = Elsa.Workflows.Runtime.Core.Models.ActivityTransitionKind;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class TypedActivityStateRecoveryTests
{
    private const string WorkflowExecutionId = "workflow-typed-recovery";
    private const string ActivityExecutionId = "activity-typed-recovery";
    private const string ExecutableNodeId = "node-approval";
    private const string ResumeTargetKey = "approval-received";
    private const string StimulusType = "approval";
    private const string StimulusHash = "sha256:approval:order-42";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-16T12:00:00Z");

    [Theory]
    [InlineData("sqlite")]
    [InlineData("memory")]
    public async Task Typed_state_survives_worker_replacement_and_duplicate_delivery_does_not_reactivate(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var executable = NewExecutable();
        var initialActivator = new RecordingActivator("worker-one");
        string registrationId;

        await using (var worker = BuildWorker(fixture.DocumentStore, initialActivator))
        {
            await worker.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
            await worker.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(NewWorkflowState(executable.Identity));
            await worker.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewRunningState(executable.RootActivity.ActivityContract!));

            await Handler<WorkflowInvokeActivitySchedulerWorkHandler>(worker)
                .HandleAsync(NewInvokeWorkItem(executable.Identity));

            var suspended = await FindActivityAsync(worker);
            Assert.Equal(ActivityExecutionStatus.Suspended, suspended.Status);
            Assert.Equal(ActivityTransitionKind.Suspend, Assert.Single(suspended.Attempts!).TransitionKind);
            Assert.Equal("order-42", suspended.PrivateState!.Value.InlineValue!.Value.GetProperty("OrderId").GetString());
            registrationId = Assert.Single(suspended.TriggerRegistrations!).RegistrationId;
            var initialActivity = Assert.Single(initialActivator.Activities);
            Assert.Equal("worker-one", initialActivity.Worker);
            Assert.True(initialActivity.Disposed);

            await DeliverOutboxAsync(worker);
            var createBookmark = Assert.Single(
                await worker.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                    .ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId)),
                item => item.CommandKind == WorkflowExecutionCommandKind.CreateBookmark);
            await Handler<WorkflowCreateBookmarkSchedulerWorkHandler>(worker).HandleAsync(createBookmark);

            var bookmark = await worker.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(WorkflowExecutionId, registrationId);
            Assert.NotNull(bookmark);
            Assert.Equal([registrationId], (await FindActivityAsync(worker)).BookmarkIds);
        }

        var resumeActivator = new RecordingActivator("worker-two");
        var resumeWorkItem = NewResumeWorkItem(executable.Identity, registrationId);
        ActivityCompletion committedCompletion;

        await using (var worker = BuildWorker(fixture.DocumentStore, resumeActivator))
        {
            var recoveredSuspension = await FindActivityAsync(worker);
            Assert.Equal(ActivityExecutionStatus.Suspended, recoveredSuspension.Status);
            Assert.Equal("order-42", recoveredSuspension.PrivateState!.Value.InlineValue!.Value.GetProperty("OrderId").GetString());

            await Handler<WorkflowResumeBookmarkSchedulerWorkHandler>(worker).HandleAsync(resumeWorkItem);

            var request = Assert.Single(resumeActivator.Requests);
            Assert.Equal(ActivityAttemptReason.Resume, request.Attempt.Reason);
            var resumeActivity = Assert.Single(resumeActivator.Activities);
            Assert.Equal("worker-two", resumeActivity.Worker);
            Assert.Equal(new ApprovalState("order-42", 1), resumeActivity.ObservedState);
            Assert.Equal(new ApprovalTrigger("approved"), resumeActivity.ObservedTrigger);
            Assert.True(resumeActivity.Disposed);

            var completed = await FindActivityAsync(worker);
            Assert.Equal(ActivityExecutionStatus.Completed, completed.Status);
            Assert.Equal(2, completed.Attempts!.Count);
            Assert.Equal(ActivityTransitionKind.Complete, completed.Attempts.OrderBy(attempt => attempt.Ordinal).Last().TransitionKind);
            Assert.Null(completed.PrivateState);
            Assert.Empty(completed.TriggerRegistrations!);
            Assert.Empty(completed.BookmarkIds);
            Assert.Equal(ActivityTriggerDeliveryStatus.Consumed, Assert.Single(completed.TriggerDeliveries!).Status);
            committedCompletion = Assert.IsType<ActivityCompletion>(completed.Completion);
            Assert.Equal("approved", committedCompletion.Result.InlineValue!.Value.GetProperty("decision").GetString());
            Assert.Null(await worker.GetRequiredService<IBookmarkStateStore>()
                .FindAsync(WorkflowExecutionId, registrationId));
        }

        var recoveryActivator = new RecordingActivator("worker-three");
        await using (var worker = BuildWorker(fixture.DocumentStore, recoveryActivator))
        {
            await Handler<WorkflowResumeBookmarkSchedulerWorkHandler>(worker).HandleAsync(resumeWorkItem);

            Assert.Empty(recoveryActivator.Requests);
            Assert.Empty(recoveryActivator.Activities);
            var recovered = await FindActivityAsync(worker);
            Assert.Equal(ActivityExecutionStatus.Completed, recovered.Status);
            Assert.NotNull(recovered.Completion);
            Assert.Equal(committedCompletion.InvocationId, recovered.Completion.InvocationId);
            Assert.Equal(committedCompletion.AttemptId, recovered.Completion.AttemptId);
            Assert.Equal(committedCompletion.OutcomeKey, recovered.Completion.OutcomeKey);
            Assert.Equal(committedCompletion.CompletedAt, recovered.Completion.CompletedAt);
            Assert.Equal(committedCompletion.ContractFingerprint, recovered.Completion.ContractFingerprint);
            Assert.Equal(
                committedCompletion.Result.InlineValue!.Value.GetRawText(),
                recovered.Completion.Result.InlineValue!.Value.GetRawText());
            Assert.Equal(2, recovered.Attempts!.Count);
            Assert.Single(recovered.TriggerDeliveries!);
        }
    }

    private static ServiceProvider BuildWorker(IDocumentStore documentStore, IActivityActivator activator)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        services.AddSingleton(documentStore);
        services.AddGroundworkRuntimeStores();
        services.AddSingleton(activator);
        return services.BuildServiceProvider();
    }

    private static T Handler<T>(IServiceProvider services) where T : class, IWorkflowSchedulerWorkHandler =>
        Assert.Single(services.GetServices<IWorkflowSchedulerWorkHandler>(), handler => handler is T) as T
        ?? throw new InvalidOperationException($"Scheduler handler '{typeof(T).Name}' was not registered.");

    private static async Task DeliverOutboxAsync(IServiceProvider services) =>
        await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
            .ProcessAsync(new RuntimePostCommitOutboxProcessRequest(10, WorkflowExecutionId));

    private static async Task<ActivityExecutionState> FindActivityAsync(IServiceProvider services) =>
        await services.GetRequiredService<IActivityExecutionStateStore>()
            .FindAsync(WorkflowExecutionId, ActivityExecutionId)
        ?? throw new InvalidOperationException("The typed activity execution state was not persisted.");

    private static WorkflowExecutable NewExecutable()
    {
        var descriptor = JsonSerializer.SerializeToElement(new { type = "typed-stateful-recovery" });
        var resultType = Descriptor<ApprovalResult>();
        var contract = new ActivityContract(
            "test/typed-stateful-recovery",
            "1.0.0",
            "typed-stateful-recovery",
            descriptor,
            [],
            new ActivityResultContract(resultType, true, ActivityValuePolicy.Default, []),
            ["Done"],
            new ActivityActivationRequirement("typed-stateful-recovery", "test/typed-stateful-recovery"));
        var node = new ExecutableNode(
            ExecutableNodeId,
            "authored-approval",
            contract.ActivityTypeKey,
            contract.ContractVersion,
            contract.DescriptorKind,
            descriptor,
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            activityContract: contract);

        return new WorkflowExecutable(
            new WorkflowExecutableIdentity("artifact-typed-recovery", "definition-typed-recovery", "version-1", "1.0.0", "sha256:typed-recovery"),
            node,
            new Dictionary<string, WorkflowExecutableResumeTarget>
            {
                [ResumeTargetKey] = new(ResumeTargetKey, ExecutableNodeId, ResumeTargetKey, new Dictionary<string, string>())
            },
            Now,
            new Dictionary<string, string>());
    }

    private static WorkflowExecutionState NewWorkflowState(WorkflowExecutableIdentity identity)
    {
        var root = new VariableFrameFactory().CreateRoot(
            WorkflowExecutionId,
            Elsa.Expressions.Core.Models.VariableReference.WorkflowScopeId,
            new Dictionary<string, ValueEnvelope>());
        return new WorkflowExecutionState(
            WorkflowExecutionId,
            identity,
            WorkflowExecutionStatus.Running,
            null,
            Now,
            Now,
            Now,
            null,
            null,
            null,
            null,
            new Dictionary<string, string>())
        {
            RootVariableFrame = root
        };
    }

    private static ActivityExecutionState NewRunningState(ActivityContract contract)
    {
        var attempt = new ActivityAttempt(
            $"{ActivityExecutionId}:attempt:1",
            ActivityExecutionId,
            1,
            ActivityAttemptReason.Initial,
            Now.AddMinutes(-1));
        var snapshot = new ActivityInputSnapshot(
            ActivityExecutionId,
            contract.SchemaFingerprint,
            "sha256:no-bindings",
            new Dictionary<string, ValueEnvelope>(),
            Now.AddMinutes(-1));

        return new ActivityExecutionState(
            new ActivityExecution(
                ActivityExecutionId,
                WorkflowExecutionId,
                ExecutableNodeId,
                "authored-approval",
                contract.ActivityTypeKey,
                contract.ContractVersion),
            ActivityExecutionStatus.Running,
            SubStatus: null,
            ExecutionSequence: 1,
            ScheduledAt: Now.AddMinutes(-2),
            StartedAt: Now.AddMinutes(-1),
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            Provenance: ActivitySchedulingProvenance.From(WorkflowExecutionId, null, null, null, null, null, null, null),
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>(),
            ContractIdentity: new ActivityInvocationContractIdentity(
                contract.ActivityTypeKey,
                contract.ContractVersion,
                contract.SchemaFingerprint),
            InputSnapshot: snapshot,
            Attempts: [attempt]);
    }

    private static RuntimeSchedulerWorkItem NewInvokeWorkItem(WorkflowExecutableIdentity identity)
    {
        var payload = new RuntimeInvokeActivityCommandPayload(
            identity,
            ExecutableNodeId,
            ActivityExecutionId,
            RuntimeInvokeActivityCommandPayload.StartedActivityReason);
        return WorkItem(
            "invoke-typed-recovery",
            WorkflowExecutionCommandKind.InvokeActivity,
            "typed-recovery:invoke",
            JsonSerializer.SerializeToElement(payload),
            sequence: 10);
    }

    private static RuntimeSchedulerWorkItem NewResumeWorkItem(
        WorkflowExecutableIdentity identity,
        string registrationId)
    {
        var triggerType = Descriptor<ApprovalTrigger>();
        var payload = new RuntimeResumeBookmarkCommandPayload(
            identity,
            registrationId,
            ActivityExecutionId,
            ExecutableNodeId,
            ResumeTargetKey,
            StimulusType,
            StimulusHash,
            JsonSerializer.SerializeToElement(new ApprovalTrigger("approved")),
            RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason,
            triggerDelivery: new RuntimeTypedTriggerDeliveryMetadata(
                "delivery-order-42",
                triggerType,
                "provider-approval",
                Now,
                "approval-order-42"));
        return WorkItem(
            "resume-typed-recovery",
            WorkflowExecutionCommandKind.ResumeBookmark,
            "typed-recovery:resume:order-42",
            JsonSerializer.SerializeToElement(payload),
            sequence: 20);
    }

    private static RuntimeSchedulerWorkItem WorkItem(
        string id,
        WorkflowExecutionCommandKind kind,
        string idempotencyKey,
        JsonElement payload,
        long sequence) =>
        new(
            id,
            WorkflowExecutionId,
            $"command:{id}",
            kind,
            $"envelope:{id}",
            idempotencyKey,
            Now,
            Now,
            sequence,
            payload,
            new Dictionary<string, string> { ["worker"] = "test" },
            new Dictionary<string, string> { ["transport"] = "in-process" });

    private static ValueTypeDescriptor Descriptor<T>() =>
        new(TypeAliasConvention.CanonicalAlias(typeof(T)), schemaVersion: 1);

    private sealed class RecordingActivator(string worker) : IActivityActivator
    {
        public List<ActivityActivationRequest> Requests { get; } = [];
        public List<ApprovalActivity> Activities { get; } = [];

        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var activity = new ApprovalActivity(worker);
            Activities.Add(activity);
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class ApprovalActivity(string worker) :
        StatefulActivity<ApprovalResult, ApprovalState, ApprovalTrigger>,
        IDisposable
    {
        public string Worker { get; } = worker;
        public bool Disposed { get; private set; }
        public ApprovalState? ObservedState { get; private set; }
        public ApprovalTrigger? ObservedTrigger { get; private set; }

        protected override ValueTask<ActivityTransition<ApprovalResult, ApprovalState>> ExecuteAsync(
            ActivityExecutionContext context) =>
            ValueTask.FromResult(Suspend(
                new ApprovalState("order-42", 1),
                [new ActivityTriggerRegistration<ApprovalTrigger>(
                    ResumeTargetKey,
                    StimulusType,
                    StimulusHash,
                    ActivityTriggerDeduplicationMode.IdempotencyKey)]));

        protected override ValueTask<ActivityTransition<ApprovalResult, ApprovalState>> ResumeAsync(
            ActivityResumeContext<ApprovalState, ApprovalTrigger> context)
        {
            ObservedState = context.State;
            ObservedTrigger = context.Trigger;
            return ValueTask.FromResult(Complete(new ApprovalResult(context.Trigger.Decision)));
        }

        public void Dispose() => Disposed = true;
    }

    private sealed record ApprovalState(string OrderId, int Revision);
    private sealed record ApprovalTrigger(string Decision);
    private sealed record ApprovalResult(string Decision);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
