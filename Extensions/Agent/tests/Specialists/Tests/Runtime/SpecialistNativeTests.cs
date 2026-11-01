using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Services;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Tests.Publication;
using Elsa.Agent.Specialists.Tests.Recovery;
using Elsa.Primitives.Models;
using Elsa.Secrets.Features;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Runtime;

public sealed partial class SpecialistNativeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Facade_completion_refuses_binding_changed_by_the_child_completion_checkpoint(bool durable)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Article\":\"accepted before final binding changed\"}");
        var trace = new LeaseTrace { ChangeBindingAtChildCompletion = true };
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, leaseTrace: trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var parent = states.Single(state => state.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        Assert.Equal("changed-at-child-completion", parent.InputSnapshot!.BindingFingerprint);
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Null(parent.Completion);
        Assert.Equal(ActivityExecutionStatus.Completed, states.Single(state => state.ParentActivityExecutionId == parent.InvocationId).Status);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(SpecialistTurnStatus.Accepted, (await NativeTurnAsync(provider, executable)).Document.Status);
    }

    private sealed class LateBindingMutation(IActivityExecutionStateStore states) : IRuntimeCheckpointCommitEnricher
    {
        public async ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            var child = commit.StateChanges.ActivityExecutions.FirstOrDefault(change => change.Operation == RuntimeStateChangeOperation.Upsert &&
                change.State.Status == ActivityExecutionStatus.Completed && change.State.Provenance.Metadata.ContainsKey(SpecialistNativeBinding.TurnHandleMetadataKey))?.State;
            if (child?.ParentActivityExecutionId is not { } parentId) return commit;
            var change = commit.StateChanges.ActivityExecutions.FirstOrDefault(candidate => candidate.StateId == parentId);
            var parent = change?.State ?? await states.FindAsync(commit.WorkflowExecutionId, parentId, cancellationToken);
            if (parent?.InputSnapshot is not { } input || parent.Status != ActivityExecutionStatus.Running) return commit;
            var updated = parent with { InputSnapshot = new(input.InvocationId, input.ContractFingerprint, "changed-at-child-completion", input.Values, input.MaterializedAt) };
            return commit with { StateChanges = commit.StateChanges.WithActivityExecutions([
                .. commit.StateChanges.ActivityExecutions.Where(candidate => candidate.StateId != parentId),
                new(parentId, RuntimeStateChangeOperation.Upsert, updated, change?.Metadata ?? new Dictionary<string, string>())]) };
        }
    }

    [Theory]
    [InlineData(false, "contract", "before-accept")]
    [InlineData(false, "contract", "after-accept")]
    [InlineData(false, "contract", "confirmation")]
    [InlineData(false, "binding", "before-accept")]
    [InlineData(false, "binding", "after-accept")]
    [InlineData(false, "binding", "confirmation")]
    [InlineData(false, "operation", "before-accept")]
    [InlineData(false, "operation", "after-accept")]
    [InlineData(false, "operation", "confirmation")]
    [InlineData(true, "contract", "before-accept")]
    [InlineData(true, "contract", "after-accept")]
    [InlineData(true, "contract", "confirmation")]
    [InlineData(true, "binding", "before-accept")]
    [InlineData(true, "binding", "after-accept")]
    [InlineData(true, "binding", "confirmation")]
    [InlineData(true, "operation", "before-accept")]
    [InlineData(true, "operation", "after-accept")]
    [InlineData(true, "operation", "confirmation")]
    public async Task Revalidation_refuses_changed_committed_native_binding(bool durable, string changed, string phase)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Article\":\"accepted before binding changed\"}");
        var afterAccept = phase != "before-accept";
        var trace = new LeaseTrace { LoseAcceptanceResponse = phase == "confirmation" };
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, leaseTrace: trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var stateStore = provider.GetRequiredService<IActivityExecutionStateStore>();
        var original = (await stateStore.ListAllAsync("run")).Single(state => state.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        var descriptor = SpecialistNativeBinding.Descriptor(executable.RootActivity);
        var retainedIdentity = new SpecialistInvocationIdentity(workflow.TenantId, workflow.Partition!.Value, "run", original.InvocationId, original.OperationKey,
            executable.Identity.ArtifactId, executable.Identity.ArtifactHash, descriptor.DefinitionVersionId!, descriptor.TemplateHash!);
        var handle = SpecialistNativeBinding.Handle(original);
        async ValueTask ChangeBindingAsync()
        {
            var parent = (await stateStore.FindAsync("run", original.InvocationId))!;
            var identity = parent.ContractIdentity!;
            var input = parent.InputSnapshot!;
            var updated = changed switch
            {
                "contract" => parent with { ContractIdentity = new(identity.ActivityTypeKey, identity.ContractVersion, "changed-contract-fingerprint") },
                "binding" => parent with { InputSnapshot = new(input.InvocationId, input.ContractFingerprint, "changed-binding-fingerprint", input.Values, input.MaterializedAt) },
                _ => parent with { Provenance = parent.Provenance with { Metadata = new Dictionary<string, string>(parent.Provenance.Metadata) { [RuntimeMetadataKeys.OperationKey] = "changed-operation" } } }
            };
            await stateStore.SaveAsync(updated);
        }
        if (afterAccept) trace.AfterAccepted = ChangeBindingAsync;
        else factory.BeforeResponse = ChangeBindingAsync;
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        var states = await stateStore.ListAllAsync("run");
        var child = states.Single(state => state.Execution.ExecutableNodeId == SpecialistNativeBinding.ChildNode(executable.RootActivity).ExecutableNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, child.Status);
        Assert.Null(child.Completion);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(afterAccept ? SpecialistTurnStatus.Accepted : SpecialistTurnStatus.Failed,
            (await provider.GetRequiredService<SpecialistTurnJournal>().ReadAsync(handle, retainedIdentity, default)).Document.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_cancellation_after_journal_acceptance_prevents_acknowledgement_recovery_from_applying_the_result(bool durable)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Article\":\"accepted before cancel\"}");
        var trace = new LeaseTrace { LoseAcceptanceResponse = true };
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, leaseTrace: trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        trace.AfterAccepted = async () =>
        {
            var now = DateTimeOffset.UtcNow;
            var command = new WorkflowExecutionCommand("cancel-after-accept", "run", WorkflowExecutionCommandKind.Cancel, now, null, new Dictionary<string, string>());
            await actor.EnqueueAsync(new("cancel-after-accept-envelope", "run", command, "cancel-after-accept", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
            await DrainAsync(actor, "cancel-after-accept");
        };
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim).WaitAsync(TimeSpan.FromSeconds(20));
        await DrainAsync(actor, "after-cancelled-worker");
        var (document, request) = await NativeTurnAsync(provider, executable);
        Assert.Equal(SpecialistTurnStatus.Accepted, document.Status);
        Assert.NotNull(document.ProtectedResult);
        Assert.Null(document.Applications);
        Assert.Equal(1, (await provider.GetRequiredService<ISpecialistTurnStore>().ReadConversationAsync(document.Handle, request.Identity, default))!.Revision);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(1, factory.Calls);
        Assert.All(await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run"), state => Assert.Null(state.Completion));
    }

    [Theory]
    [InlineData(false, "committed")]
    [InlineData(true, "committed")]
    [InlineData(false, "before-commit")]
    [InlineData(true, "before-commit")]
    [InlineData(false, "read-unavailable")]
    [InlineData(true, "read-unavailable")]
    public async Task A_lost_acceptance_acknowledgement_requires_an_authoritative_receipt_before_native_completion(bool durable, string mode)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Article\":\"accepted once\"}");
        var trace = new LeaseTrace { LoseAcceptanceResponse = mode != "before-commit", FailBeforeAcceptance = mode == "before-commit", AlwaysLoseAcceptedReceiptRead = mode == "read-unavailable" };
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, leaseTrace: trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        trace.AlwaysLoseAcceptedReceiptRead = false; // Inspection after the deliberately unconfirmed native decision.
        var (document, request) = await NativeTurnAsync(provider, executable);
        var completed = mode == "committed";
        var accepted = mode != "before-commit";
        Assert.Equal(accepted ? SpecialistTurnStatus.Accepted : SpecialistTurnStatus.OutcomeUnknown, document.Status);
        Assert.Equal(completed ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Faulted,
            (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(1, factory.Opens);
        Assert.Single(document.ModelRequests!);
        Assert.Equal(3, document.ResponseObservation!.OutputTokens);
        var conversation = (await provider.GetRequiredService<ISpecialistTurnStore>().ReadConversationAsync(document.Handle, request.Identity, default))!;
        Assert.Equal(accepted ? 1 : 0, conversation.Revision);
        Assert.Equal(!accepted, conversation.OutcomeUnknown);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        if (completed)
        {
            Assert.NotNull(document.ProtectedResult);
            Assert.Equal("accepted once", states.Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId).Completion!.Result.InlineValue!.Value.GetProperty("Article").GetString());
        }
        else Assert.All(states, state => Assert.Null(state.Completion));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task A_real_native_operator_retry_replays_committed_acceptance_into_a_new_parent_and_child(bool durable, bool changeReplayBinding)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Article\":\"accepted once\"}");
        var trace = new LeaseTrace { LoseAcceptedReceiptRead = true };
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, leaseTrace: trace);
        var specialist = await ExecutableAsync(provider, allowContinuity: true, invocationControls: true);
        var alias = typeof(RetryInterventionActivity).FullName!;
        var payload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias));
        var descriptorKind = typeof(ClrActivityDescriptor).FullName!;
        var contract = new ActivityContract(alias, "1.0.0", descriptorKind, payload, [],
            new(new(typeof(ActivityUnit).FullName!), true, ActivityValuePolicy.Default, []), ["Done"], new(descriptorKind, alias), SideEffectProfile.ReplaySafe);
        var root = new ExecutableNode("retry-wrapper", "retry-wrapper", alias, "1.0.0", new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, "1", payload),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string> { [RuntimeMetadataKeys.StructuralFaultHandling] = RuntimeMetadataKeys.StructuralFaultHandlingChild },
            [new ExecutableChildSlot("body", [specialist.RootActivity])],
            new ExecutableActivityStructure("test.specialist-retry", "1", Json("{}")), contract);
        var executable = new WorkflowExecutable(new("retry-editorial", "retry-workflow", "retry-workflow-v1", "1.0.0", "retry-artifact-hash"), root, specialist.ResumeTargets, DateTimeOffset.UtcNow, new Dictionary<string, string>(),
            inputContract: null, dependencies: null, specialist.RuntimeRequirements, specialist.StorageDriverRequirements, IncidentStrategyBuiltIns.FaultReference);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var (actor, _) = await StartAsync(provider, executable, new("specialist-test", "specialist-test"));
        await DrainAsync(actor, "start-retry-wrapper");
        var firstClaim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("original-worker", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        Assert.True(firstClaim is not null, JsonSerializer.Serialize(new {
            States = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Select(x => new { x.InvocationId, x.Execution.ExecutableNodeId, x.Status, x.Fault }),
            Incidents = await provider.GetRequiredService<IIncidentStateStore>().ListAsync("run") }));
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), firstClaim);
        await DrainAsync(actor, "lost-acceptance");
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var original = states.Single(x => x.Execution.ExecutableNodeId == specialist.RootActivity.ExecutableNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, original.Status);
        Assert.True((await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status == WorkflowExecutionStatus.Running,
            JsonSerializer.Serialize(new { States = states.Select(x => new { x.InvocationId, x.Execution.ExecutableNodeId, x.Status, x.Fault }), Incidents = await provider.GetRequiredService<IIncidentStateStore>().ListAsync("run") }));
        var firstChild = states.Single(x => x.ParentActivityExecutionId == original.InvocationId);
        var handle = firstChild.Provenance.Metadata[SpecialistNativeBinding.TurnHandleMetadataKey];
        var model = SpecialistNativeBinding.Descriptor(specialist.RootActivity);
        var originalIdentity = new SpecialistInvocationIdentity(null, "default", "run", original.InvocationId, original.OperationKey,
            executable.Identity.ArtifactId, executable.Identity.ArtifactHash, model.DefinitionVersionId!, model.TemplateHash!);
        var accepted = (await provider.GetRequiredService<SpecialistTurnJournal>().ReadAsync(handle, originalIdentity, default)).Document;
        Assert.Equal(SpecialistTurnStatus.Accepted, accepted.Status);
        var admission = await provider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(new WorkflowAlterationSubmission(
            new("default", "specialist-test", "specialist-test"), WorkflowAlterationTargetSelector.ForExecutionIds(["run"]),
            [new("RetryFaultedActivity", 1, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = original.InvocationId }))]),
            new WorkflowAlterationOperatorProvenance("test-operator", null), "retry-accepted-specialist");
        WorkflowAlterationJobState? job = null;
        for (var step = 0; step < 8; step++)
        {
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationOrchestrationSweep>().ExecuteAsync(new() { WorkerId = "retry-worker" }, "retry-worker");
            job = await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(WorkflowAlterationIdentity.CreateJobId(admission.Plan.PlanId, "run"));
            if (job?.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed) break;
        }
        Assert.NotNull(job);
        Assert.True(job.Status == WorkflowAlterationJobStatus.Succeeded, JsonSerializer.Serialize(job));
        for (var step = 0; step < 8; step++)
        {
            await provider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest());
            await DrainAsync(actor, "retry-sweep-" + step);
        }
        var replayClaim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("replay-worker", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        Assert.True(replayClaim is not null, JsonSerializer.Serialize(new {
            States = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Select(x => new { x.InvocationId, x.Execution.ExecutableNodeId, x.Status, x.Fault }),
            Incidents = await provider.GetRequiredService<IIncidentStateStore>().ListAsync("run") }));
        if (changeReplayBinding) trace.AfterReplayBound = async identity =>
        {
            var store = provider.GetRequiredService<IActivityExecutionStateStore>();
            var parent = (await store.FindAsync("run", identity.ActivityExecutionId))!;
            var input = parent.InputSnapshot!;
            await store.SaveAsync(parent with { InputSnapshot = new(input.InvocationId, input.ContractFingerprint, "changed-replay-binding", input.Values, input.MaterializedAt) });
        };
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), replayClaim);
        await DrainAsync(actor, "accepted-replay");
        for (var step = 0; step < 8 && (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status == WorkflowExecutionStatus.Running; step++)
        {
            await provider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest());
            await DrainAsync(actor, "replay-complete-" + step);
        }
        Assert.Equal(1, factory.Calls);
        Assert.Equal(1, factory.Opens);
        if (changeReplayBinding)
        {
            states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
            var refusedChild = states.Single(state => state.Execution.ExecutableNodeId == SpecialistNativeBinding.ChildNode(specialist.RootActivity).ExecutableNodeId && state.ParentActivityExecutionId != original.InvocationId);
            Assert.Equal(ActivityExecutionStatus.Faulted, refusedChild.Status);
            Assert.Null(refusedChild.Completion);
            Assert.Equal(accepted.ProtectedResult, (await provider.GetRequiredService<SpecialistTurnJournal>().ReadAsync(handle, originalIdentity, default)).Document.ProtectedResult);
            return;
        }
        Assert.True((await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status == WorkflowExecutionStatus.Completed,
            JsonSerializer.Serialize(new { States = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Select(x => new { x.InvocationId, x.Execution.ExecutableNodeId, x.Status, x.Fault }),
                Incidents = await provider.GetRequiredService<IIncidentStateStore>().ListAsync("run") }));
        Assert.Equal(1, provider.GetRequiredService<RetryCompletionLog>().Completions);
        states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var successor = states.Single(x => x.Execution.ExecutableNodeId == specialist.RootActivity.ExecutableNodeId && x.Status == ActivityExecutionStatus.Completed);
        Assert.NotEqual(original.InvocationId, successor.InvocationId);
        Assert.Equal(original.OperationKey, successor.OperationKey);
        var replayChild = states.Single(x => x.ParentActivityExecutionId == successor.InvocationId);
        Assert.NotEqual(firstChild.InvocationId, replayChild.InvocationId);
        Assert.Equal(ActivityExecutionStatus.Completed, replayChild.Status);
        Assert.Equal("accepted once", successor.Completion!.Result.InlineValue!.Value.GetProperty("Article").GetString());
        _ = await provider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(10, "run", SpecialistSettlementBoundary.AppliedKind));
        var replayed = (await provider.GetRequiredService<SpecialistTurnJournal>().ReadAsync(handle, originalIdentity with { ActivityExecutionId = successor.InvocationId }, default)).Document;
        var application = Assert.Single(replayed.Applications!);
        Assert.Equal(successor.InvocationId, application.Identity.ActivityExecutionId);
        Assert.Equal(replayChild.InvocationId, application.ChildInvocationId);
        Assert.Equal(accepted.ProtectedResult, replayed.ProtectedResult);
        Assert.Equal(firstChild.InvocationId, replayed.ChildInvocationId);
        Assert.Equal(2, replayed.Deliveries!.Count);
        Assert.Equal(1, (await provider.GetRequiredService<ISpecialistTurnStore>().ReadConversationAsync(handle, originalIdentity, default))!.Revision);
    }

    public sealed class RetryCompletionLog { public int Completions; }
    [Elsa.Activities.Runtime.Core.Attributes.Activity(IsDiscoverable = false)]
    public sealed class RetryInterventionActivity(RetryCompletionLog log) : Elsa.Activities.Runtime.Core.Abstractions.StructuralActivity,
        IRuntimeStructuralActivity, IRuntimeActivityChildFaultHandler, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            context.ScheduleChildActivity(context.ExecutableNode.ChildSlots.Single().Activities.Single().ExecutableNodeId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            ((IRuntimeActivityExecutionContext)context.ParentContext).RequestChildFaultIntervention(context.IncidentId!, "retry-specialist");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            log.Completions++;
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_retained_continuation_profile_does_not_admit_new_limit_controls(bool limits)
    {
        var factory = new FixtureFactory("{\"Article\":\"accepted\"}");
        await using var provider = await ServicesAsync(factory);
        var executable = await ExecutableAsync(provider, allowContinuity: true, invocationControls: true,
            optionsBinding: OptionsBinding(limits ? "{\"limits\":{\"maximumModelCalls\":1}}" : "{}"), controlsProfile: SpecialistInvocationControls.LegacyProfile);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        if (limits) Assert.Null(claim);
        else await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim!);
        await DrainAsync(actor);
        Assert.Equal(limits ? 0 : 1, factory.Calls);
        Assert.Equal(limits ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed,
            (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        if (limits) Assert.Contains(await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run"), state => state.Fault?.Code == "specialist.controls.invalid");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_invocation_lowers_effective_limits_without_rewriting_the_published_descriptor(bool durable)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var factory = new FixtureFactory("{\"Article\":\"accepted\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock);
        var requested = new SpecialistTurnLimits(1, 20_000, 32);
        var published = new SpecialistTurnLimits(8, 300_000, 64);
        var options = OptionsBinding("{\"limits\":{\"maximumModelCalls\":1,\"timeoutMilliseconds\":20000,\"maximumOutputTokens\":32}}");
        var executable = await ExecutableAsync(provider, allowContinuity: true, invocationControls: true, optionsBinding: options, limits: published);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", clock.GetUtcNow(), TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        var (document, request) = await NativeTurnAsync(provider, executable);
        Assert.Equal(SpecialistTurnStatus.Accepted, document.Status);
        Assert.Equal(published, request.Descriptor.Limits);
        Assert.Equal(requested, request.EffectiveLimits);
        Assert.Equal(1, document.MaximumModelCalls);
        Assert.Equal(clock.GetUtcNow().AddSeconds(20), document.Deadline);
        Assert.Equal(32, Assert.Single(document.ModelRequests!).MaximumOutputTokens);
        Assert.Equal(32, factory.MaximumOutputTokens);
        Assert.Equal(1, factory.Calls);
        Assert.DoesNotContain("limits", factory.LastUser!, StringComparison.Ordinal);
        Assert.DoesNotContain("maximumOutputTokens", factory.LastUser!, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_lowered_native_deadline_stops_waiting_inference_without_releasing_its_reservation(bool durable)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var factory = new FixtureFactory("{\"Article\":\"late\"}") { ResponseGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock);
        var executable = await ExecutableAsync(provider, allowContinuity: true, invocationControls: true,
            optionsBinding: OptionsBinding("{\"limits\":{\"timeoutMilliseconds\":1000}}"));
        var (actor, _) = await StartAsync(provider, executable);
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", clock.GetUtcNow(), TimeSpan.FromMinutes(2)))!;
        var running = provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await factory.RequestSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromMilliseconds(1001));
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        await DrainAsync(actor);
        var (document, request) = await NativeTurnAsync(provider, executable);
        Assert.Equal(1000, request.EffectiveLimits!.TimeoutMilliseconds);
        Assert.Equal(300_000, request.Descriptor.Limits.TimeoutMilliseconds);
        Assert.Equal(SpecialistTurnStatus.OutcomeUnknown, document.Status);
        Assert.Single(document.ModelRequests!);
        Assert.Null(document.ProtectedResult);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
    }

    [Theory]
    [InlineData("{\"maximumModelCalls\":0}")]
    [InlineData("{\"maximumModelCalls\":9}")]
    [InlineData("{\"timeoutMilliseconds\":999}")]
    [InlineData("{\"timeoutMilliseconds\":300001}")]
    [InlineData("{\"maximumOutputTokens\":0}")]
    [InlineData("{\"maximumOutputTokens\":65}")]
    public async Task Native_invocation_refuses_nonpositive_or_widened_limits_before_staging(string limits)
    {
        var factory = new FixtureFactory("{\"Article\":\"must not run\"}");
        await using var provider = await ServicesAsync(factory);
        var executable = await ExecutableAsync(provider, allowContinuity: true, invocationControls: true,
            optionsBinding: OptionsBinding("{\"limits\":" + limits + "}"), limits: new(8, 300_000, 64));
        var (actor, _) = await StartAsync(provider, executable);
        await DrainAsync(actor);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(0, factory.Calls);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        Assert.Contains(states, x => x.Fault?.Code == "specialist.limits.invalid");
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
    }

    private static RuntimeInputBinding OptionsBinding(string json) => new(SpecialistInvocationControls.InputKey, new(SpecialistInvocationControls.JsonType),
        ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
        literal: ValueEnvelope.Inline(new(SpecialistInvocationControls.JsonType), Json(json), ValueProtectionPolicy.InstanceInline));

    private static async ValueTask<(SpecialistTurnDocument Document, SpecialistTurnRequest Request)> NativeTurnAsync(ServiceProvider provider, WorkflowExecutable executable)
    {
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var parent = states.Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        var child = states.Single(x => x.Execution.ExecutableNodeId == SpecialistNativeBinding.ChildNode(executable.RootActivity).ExecutableNodeId);
        var descriptor = SpecialistNativeBinding.Descriptor(executable.RootActivity);
        var identity = new SpecialistInvocationIdentity(workflow.TenantId, workflow.Partition!.Value, workflow.WorkflowExecutionId, parent.InvocationId,
            parent.OperationKey, workflow.PinnedExecutable.ArtifactId, workflow.PinnedExecutable.ArtifactHash, descriptor.DefinitionVersionId!, descriptor.TemplateHash!);
        return await provider.GetRequiredService<SpecialistTurnJournal>().ReadAsync(child.Provenance.Metadata[SpecialistNativeBinding.TurnHandleMetadataKey], identity, default);
    }


    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Native_redelivery_recovers_expired_predispatch_but_never_repeats_expired_dispatch(bool durable, bool dispatched)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var trace = new LeaseTrace();
        var factory = new FixtureFactory("{\"Article\":\"accepted\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock, trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        var parent = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        var handle = SpecialistNativeBinding.Handle(parent);
        var descriptor = SpecialistNativeBinding.Descriptor(executable.RootActivity);
        var identity = new SpecialistInvocationIdentity(workflow.TenantId, workflow.Partition!.Value, workflow.WorkflowExecutionId, parent.InvocationId,
            parent.OperationKey, workflow.PinnedExecutable.ArtifactId, workflow.PinnedExecutable.ArtifactHash, descriptor.DefinitionVersionId!, descriptor.TemplateHash!);
        var workStore = provider.GetRequiredService<IBackgroundActivityWorkStore>();
        var delivery = (await workStore.ClaimNextAsync("recovered-worker", clock.GetUtcNow(), TimeSpan.FromMinutes(2)))!;
        var item = (await workStore.FindAsync("run", delivery.WorkId))!;
        var store = provider.GetRequiredService<ISpecialistTurnStore>();
        var crashed = (await store.ClaimAsync(handle, identity, item.Request.ActivityExecutionId, "crashed-worker", clock.GetUtcNow(), default))!;
        if (dispatched)
        {
            var token = new SpecialistTurnLeaseToken(crashed.Fence!, crashed.AdmissionGeneration);
            Assert.True(await store.DispatchAsync(handle, identity, token, clock.GetUtcNow(), default));
            Assert.NotNull(await store.ReserveModelRequestAsync(handle, identity, token,
                new(new string('e', 64), 64, clock.GetUtcNow(), new("fixture", "fixture-model", "incarnation", 1, "fixture/1", "fixture/1")), clock.GetUtcNow(), default));
        }
        var redelivered = provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), delivery);
        await trace.ClaimedRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(redelivered.IsCompleted);
        Assert.Equal(0, factory.Opens);
        clock.Advance(TimeSpan.FromSeconds(31));
        await redelivered.WaitAsync(TimeSpan.FromSeconds(10));
        await DrainAsync(actor);
        var restored = (await provider.GetRequiredService<SpecialistTurnJournal>().ReadAsync(handle, identity, default)).Document;
        Assert.Equal(crashed.Deadline, restored.Deadline);
        Assert.Equal(dispatched ? 1 : 2, restored.AdmissionGeneration);
        Assert.Equal(dispatched ? SpecialistTurnStatus.OutcomeUnknown : SpecialistTurnStatus.Accepted, restored.Status);
        Assert.Equal(dispatched ? 0 : 1, factory.Opens);
        Assert.Equal(dispatched ? 0 : 1, factory.Calls);
        Assert.Single(restored.ModelRequests!);
        if (dispatched)
        {
            Assert.True((await store.ReadConversationAsync(handle, identity, default))!.OutcomeUnknown);
            var child = (await provider.GetRequiredService<IActivityExecutionStateStore>().FindAsync("run", item.Request.ActivityExecutionId))!;
            Assert.Equal("specialist.turn.outcome-unknown", child.Fault?.Code);
            Assert.Equal("outcome-unknown", child.Fault?.Metadata.GetValueOrDefault(ActivityFault.CategoryMetadataKey));
        }
    }

    [Theory]
    [InlineData(false, "success")]
    [InlineData(false, "lost")]
    [InlineData(false, "uncertain")]
    [InlineData(false, "blocked")]
    [InlineData(true, "success")]
    [InlineData(true, "lost")]
    [InlineData(true, "uncertain")]
    [InlineData(true, "blocked")]
    public async Task Native_worker_renews_in_a_fresh_scope_and_stops_on_uncertain_renewal(bool durable, string mode)
    {
        using var files = new JournalFiles();
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow);
        var loseLease = mode != "success";
        var trace = new LeaseTrace { RejectRenewal = mode == "lost", LoseRenewalResponse = mode == "uncertain", BlockRenewal = mode == "blocked" };
        var factory = new FixtureFactory("{\"Article\":\"accepted\"}") { ResponseGate = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null, clock, trace);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var parent = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        var handle = SpecialistNativeBinding.Handle(parent);
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", clock.GetUtcNow(), TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        var running = provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await factory.RequestSent.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromSeconds(10));
        if (mode == "blocked")
        {
            await trace.RenewalStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            clock.Advance(TimeSpan.FromSeconds(20)); // Last confirmed lease expires, before absolute deadline.
        }
        else
        {
            var renewal = await trace.Renewed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (mode == "lost") Assert.Null(renewal);
            else Assert.NotNull(renewal);
        }
        if (!loseLease)
        {
            factory.ResponseGate.TrySetResult();
        }
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        await DrainAsync(actor);
        Assert.Equal(1, factory.Calls);
        var send = Assert.Single(trace.Sends);
        Assert.All(trace.Renewals, current => Assert.NotEqual(send.Store, current.Store));
        if (durable) Assert.All(trace.Renewals, current => { Assert.NotNull(current.Context); Assert.NotEqual(send.Context, current.Context); });
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        var descriptor = SpecialistNativeBinding.Descriptor(executable.RootActivity);
        var identity = new SpecialistInvocationIdentity(workflow.TenantId, workflow.Partition!.Value, workflow.WorkflowExecutionId, parent.InvocationId,
            parent.OperationKey, workflow.PinnedExecutable.ArtifactId, workflow.PinnedExecutable.ArtifactHash, descriptor.DefinitionVersionId!, descriptor.TemplateHash!);
        var document = (await provider.GetRequiredService<SpecialistTurnJournal>().ReadAsync(handle, identity, default)).Document;
        Assert.Equal(loseLease ? SpecialistTurnStatus.OutcomeUnknown : SpecialistTurnStatus.Accepted, document.Status);
        Assert.Single(document.ModelRequests!);
        Assert.Equal(loseLease ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed, workflow.Status);
        if (loseLease)
        {
            Assert.Equal("specialist.turn.lease", document.FailureCode);
            Assert.True((await provider.GetRequiredService<ISpecialistTurnStore>().ReadConversationAsync(handle, identity, default))!.OutcomeUnknown);
        }
    }

    [Fact]
    public async Task Continuation_profile_cannot_be_reinterpreted_as_a_legacy_descriptor()
    {
        var factory = new FixtureFactory("{\"Article\":\"must not run\"}");
        await using var provider = await ServicesAsync(factory);
        var placed = await SpecialistPublicationTests.PublishAndPlaceAsync("Article", "String", allowContinuity: true, invocationControls: true);
        var root = placed.Placement.Root;
        var wrongSchema = new ExecutableNode(root.ExecutableNodeId, root.AuthoredActivityId, root.ActivityType, root.ActivityTypeVersion,
            new RuntimeActivityDescriptor(root.DescriptorType, "1", root.DescriptorPayload), root.InputBindings, root.Metadata, root.ChildSlots, root.Structure, root.ActivityContract);
        var executable = new WorkflowExecutable(new("editorial", "workflow", "workflow-v1", "1.0.0", "artifact-hash"), wrongSchema,
            placed.Placement.ResumeTargets, DateTimeOffset.UtcNow, new Dictionary<string, string>(), inputContract: null, dependencies: null,
            placed.Placement.RuntimeRequirements, placed.Storage, IncidentStrategyBuiltIns.FaultReference);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var (actor, _) = await StartAsync(provider, executable);
        await DrainAsync(actor);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
    }

    [Fact]
    public async Task Unknown_conversation_is_a_native_conflict_before_client_open()
    {
        var factory = new FixtureFactory("{\"Article\":\"must not run\"}");
        await using var provider = await ServicesAsync(factory);
        var binding = new RuntimeInputBinding(SpecialistInvocationControls.InputKey, new(SpecialistInvocationControls.JsonType), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new(SpecialistInvocationControls.JsonType),
                JsonSerializer.SerializeToElement(new { conversationId = new string('a', 64), revision = 1 }), ValueProtectionPolicy.InstanceInline));
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, allowContinuity: true, invocationControls: true, optionsBinding: binding));
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(0, factory.Calls);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        Assert.Contains(states, x => x.Fault?.Code == "specialist.conversation.conflict");
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
    }

    [Theory]
    [InlineData("{\"limits\":null}")]
    [InlineData("{\"limits\":[]}")]
    [InlineData("{\"limits\":{\"owner\":\"caller\"}}")]
    [InlineData("{\"limits\":{\"maximumModelCalls\":1,\"maximumModelCalls\":2}}")]
    [InlineData("{\"limits\":{\"maximumModelCalls\":\"1\"}}")]
    [InlineData("{\"limits\":{\"timeoutMilliseconds\":1.5}}")]
    [InlineData("{\"limits\":{\"maximumOutputTokens\":null}}")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("\"conversation\"")]
    [InlineData("{\"owner\":\"caller\"}")]
    [InlineData("{\"revision\":1}")]
    [InlineData("{\"conversationId\":\"invalid\",\"revision\":1}")]
    [InlineData("{\"conversationId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"revision\":0}")]
    [InlineData("{\"conversationId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"revision\":\"1\"}")]
    [InlineData("{\"conversationId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"revision\":1,\"tenantId\":\"foreign\"}")]
    [InlineData("{\"conversationId\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"revision\":1,\"revision\":2}")]
    public async Task Invalid_native_conversation_options_open_no_client_or_background_turn(string optionsJson)
    {
        var factory = new FixtureFactory("{\"Article\":\"must not run\"}");
        await using var provider = await ServicesAsync(factory);
        var binding = new RuntimeInputBinding(SpecialistInvocationControls.InputKey, new(SpecialistInvocationControls.JsonType), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal, literal: ValueEnvelope.Inline(new(SpecialistInvocationControls.JsonType), Json(optionsJson), ValueProtectionPolicy.InstanceInline));
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, allowContinuity: true, invocationControls: true, optionsBinding: binding));
        await DrainAsync(actor);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(0, factory.Calls);
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Published_conversation_output_continues_through_a_native_variable_capture(bool durable)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory("{\"Article\":\"accepted\"}");
        await using var provider = await ServicesAsync(factory, durable ? files.ConnectionString : null);
        var captures = new Dictionary<string, RuntimeOutputCapture>
        {
            [SpecialistInvocationControls.OutputKey] = new(SpecialistInvocationControls.OutputKey, "variable:Conversation", new(SpecialistInvocationControls.JsonType, "elsa.json", null),
                DurableValueLifecycle.Instance, DurableValueStorage.Custom, true, new Dictionary<string, string> { [RuntimeMetadataKeys.TargetVariableReferenceKey] = "conversation-variable" })
        };
        var first = await SpecialistPublicationTests.PublishAndPlaceAsync("Article", "String", allowContinuity: true, invocationControls: true, originKey: "first", outputCaptures: captures);
        var options = new RuntimeInputBinding(SpecialistInvocationControls.InputKey, new(SpecialistInvocationControls.JsonType), ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.VariableRead, variable: new("conversation-variable", "workflow"));
        var second = await SpecialistPublicationTests.PublishAndPlaceAsync("Article", "String", allowContinuity: true, invocationControls: true, originKey: "second", optionsBinding: options);
        Assert.Equal(first.Placement.Root.DescriptorPayload.GetProperty("templateHash").GetString(), second.Placement.Root.DescriptorPayload.GetProperty("templateHash").GetString());
        var children = new[] { first.Placement.Root, second.Placement.Root };
        var sequenceAlias = typeof(Elsa.Activities.Sequence.Activities.Sequence).FullName!;
        var sequencePayload = JsonSerializer.SerializeToElement(new ClrActivityDescriptor(sequenceAlias));
        var sequenceDescriptor = typeof(ClrActivityDescriptor).FullName!;
        var sequenceContract = new ActivityContract(sequenceAlias, "1.0.0", sequenceDescriptor, sequencePayload, [],
            new(new(typeof(ActivityUnit).FullName!), true, ActivityValuePolicy.Default, []), ["Done", "Break"], new(sequenceDescriptor, sequenceAlias), SideEffectProfile.ReplaySafe);
        var sequence = new ExecutableNode("sequence", "sequence", sequenceAlias, "1.0.0", new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, "1", sequencePayload),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), [new ExecutableChildSlot(Elsa.Activities.Sequence.Activities.Sequence.ActivitiesSlotName, children)],
            new ExecutableActivityStructure(Elsa.Activities.Sequence.Activities.Sequence.StructureKind, Elsa.Activities.Sequence.Activities.Sequence.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(x => x.ExecutableNodeId).ToArray() })), sequenceContract);
        var executable = new WorkflowExecutable(new("editorial", "workflow", "workflow-v1", "1.0.0", "artifact-hash"), sequence,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), DateTimeOffset.UtcNow, new Dictionary<string, string>(), inputContract: null, dependencies: null,
            first.Placement.RuntimeRequirements.Concat(second.Placement.RuntimeRequirements).Distinct().ToArray(), first.Storage, IncidentStrategyBuiltIns.FaultReference,
            workflowVariables: [new("conversation-variable", "Conversation", new(SpecialistInvocationControls.JsonType), ValueProtectionPolicy.InstanceInline)]);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var (actor, _) = await StartAsync(provider, executable);
        await DrainAsync(actor);
        for (var index = 0; index < 2; index++)
        {
            BackgroundActivityWorkClaim? claim = null;
            for (var step = 0; step < 16 && claim is null; step++)
            {
                claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
                if (claim is null) await DrainAsync(actor, $"turn-{index}-step-{step}");
            }
            Assert.True(claim is not null, JsonSerializer.Serialize(new {
                States = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run")).Select(x => new { x.InvocationId, x.Execution.ExecutableNodeId, x.Status, x.SubStatus, x.Fault }),
                Incidents = await provider.GetRequiredService<IIncidentStateStore>().ListAsync("run"),
                Work = await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync("run") }));
            await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
            await DrainAsync(actor, "worker-" + index);
        }
        for (var step = 0; step < 16 && (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status == WorkflowExecutionStatus.Running; step++)
            await DrainAsync(actor, "complete-" + step);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(2, factory.Calls);
        Assert.Equal(new[] { 1, 3 }, factory.Requests.Select(x => x.Count));
        Assert.All(factory.Requests.SelectMany(x => x), message => Assert.DoesNotContain("AgentOptions", message.Text, StringComparison.Ordinal));
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var firstResult = states.Single(x => x.Execution.ExecutableNodeId == children[0].ExecutableNodeId).Completion!.Result.InlineValue!.Value;
        var secondResult = states.Single(x => x.Execution.ExecutableNodeId == children[1].ExecutableNodeId).Completion!.Result.InlineValue!.Value;
        var firstConversation = firstResult.GetProperty("Conversation");
        var secondConversation = secondResult.GetProperty("Conversation");
        Assert.Equal(firstConversation.GetProperty("conversationId").GetString(), secondConversation.GetProperty("conversationId").GetString());
        Assert.Equal(1, firstConversation.GetProperty("revision").GetInt64());
        Assert.Equal(2, secondConversation.GetProperty("revision").GetInt64());
        Assert.Equal(new[] { "conversationId", "revision" }, secondConversation.EnumerateObject().Select(x => x.Name));
        Assert.All(factory.Requests.SelectMany(x => x), message => Assert.DoesNotContain(firstConversation.GetProperty("conversationId").GetString()!, message.Text, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("known")]
    [InlineData("missing")]
    [InlineData("partial")]
    public async Task Accepted_native_receipts_distinguish_observed_usage_from_unknown_counts(string usageMode)
    {
        var factory = new FixtureFactory("{\"Article\":\"accepted\"}", usageMode: usageMode);
        await using var provider = await ServicesAsync(factory);
        var executable = await ExecutableAsync(provider, allowContinuity: true);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow.Status);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var boundary = states.Single(state => state.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        var child = states.Single(state => state.Execution.ExecutableNodeId == executable.RootActivity.ChildSlots.Single().Activities.Single().ExecutableNodeId);
        var descriptor = SpecialistNativeBinding.Descriptor(executable.RootActivity);
        var identity = new SpecialistInvocationIdentity(workflow.TenantId, workflow.Partition!.Value, workflow.WorkflowExecutionId, boundary.InvocationId,
            boundary.OperationKey, workflow.PinnedExecutable.ArtifactId, workflow.PinnedExecutable.ArtifactHash, descriptor.DefinitionVersionId!, descriptor.TemplateHash!);
        var handle = child.Completion!.Result.InlineValue!.Value.GetString()!;
        var result = await provider.GetRequiredService<SpecialistTurnJournal>().ResultAsync(handle, identity, child.InvocationId, CancellationToken.None);
        Assert.Equal("accepted", result.Result.GetProperty("Article").GetString());
        Assert.NotNull(result.Session);
        Assert.Equal(1, result.ModelCalls);
        Assert.Equal(4096, Assert.Single(result.ModelRequests!).MaximumOutputTokens);
        Assert.Equal(usageMode != "missing", result.ResponseObservation!.UsageAvailable);
        Assert.Equal(usageMode == "missing" ? (long?)null : 5, result.ResponseObservation.InputTokens);
        Assert.Equal(usageMode == "known" ? 3 : (long?)null, result.ResponseObservation.OutputTokens);
        Assert.Equal(usageMode == "known", result.IsOutputUsageKnown);
        Assert.Equal(usageMode == "known" ? 3 : 4096, result.AccountedOutputTokens);
        Assert.Equal("provider-reported-fixture-model", result.ResponseObservation.ObservedModelId);
        Assert.Single(boundary.Completion!.Result.InlineValue!.Value.EnumerateObject());
    }

    [Theory]
    [InlineData("negative", "specialist.result.metadata")]
    [InlineData("overrun", "specialist.budget.output-overrun")]
    public async Task Invalid_or_overrun_usage_cannot_create_an_accepted_native_result(string mode, string code)
    {
        var factory = new FixtureFactory("{\"Article\":\"must not accept\"}", usageMode: mode);
        await using var provider = await ServicesAsync(factory);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider));
        var claim = (await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)))!;
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(1, factory.Calls);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        Assert.Contains(states, state => state.Fault?.Code == code);
        Assert.All(states, state => Assert.Null(state.Completion));
    }

    [Fact]
    public async Task A_provider_with_unknown_physical_accounting_is_refused_before_any_external_response_call()
    {
        var factory = new FixtureFactory("{\"Article\":\"must not run\"}", enforcesAccounting: false);
        await using var provider = await ServicesAsync(factory);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider));
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(1, factory.Opens);
        Assert.Equal(0, factory.Calls);
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        Assert.Contains(states, state => state.Fault?.Code == "specialist.budget.provider");
        Assert.All(states, state => Assert.Null(state.Completion));
    }

    [Theory]
    [InlineData("{\"Article\":null}", WorkflowExecutionStatus.Completed)]
    [InlineData("{}", WorkflowExecutionStatus.Faulted)]
    public async Task Required_nullable_result_keeps_presence_separate_from_allowed_null(string response, WorkflowExecutionStatus expected)
    {
        var factory = new FixtureFactory(response);
        await using var provider = await ServicesAsync(factory);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, outputNullable: true));
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(expected, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(1, factory.Calls);
    }

    [Fact]
    public async Task Optional_nonnullable_input_can_be_absent_without_becoming_explicit_null()
    {
        var factory = new FixtureFactory("{\"Article\":\"no brief supplied\"}");
        await using var provider = await ServicesAsync(factory);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, optionalBrief: true));
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.False(JsonDocument.Parse(factory.LastUser!).RootElement.TryGetProperty("Brief", out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Null_or_encryption_required_input_is_refused_before_dispatch(bool requiresEncryption)
    {
        var factory = new FixtureFactory("{\"Article\":\"must not dispatch\"}");
        await using var provider = await ServicesAsync(factory);
        var policy = requiresEncryption
            ? new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true)
            : ValueProtectionPolicy.InstanceInline;
        var value = requiresEncryption ? ValueEnvelope.Inline(new("String"), Json("\"private brief\""), policy) : ValueEnvelope.Null(new("String"), policy);
        var binding = new RuntimeInputBinding("brief-key", new("String"), policy, RuntimeInputBindingSource.Literal, literal: value);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, optionalBrief: true, briefBinding: binding));
        await DrainAsync(actor);
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(0, factory.Calls);
        Assert.Equal(0, factory.Opens);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_capture_admission_respects_the_destination_policy_before_dispatch(bool requiresEncryption)
    {
        var factory = new FixtureFactory("{\"Article\":\"private result\"}");
        await using var provider = await ServicesAsync(factory);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: requiresEncryption);
        var captures = new Dictionary<string, RuntimeOutputCapture>
        {
            ["article-key"] = new("article-key", "variable:Article", new("String", "elsa.json", null), DurableValueLifecycle.Instance, DurableValueStorage.Custom, true,
                new Dictionary<string, string> { [RuntimeMetadataKeys.TargetVariableReferenceKey] = "article-variable" })
        };
        var executable = await ExecutableAsync(provider, outputCaptures: captures, variables: [new("article-variable", "Article", new("String"), policy)]);
        var (actor, _) = await StartAsync(provider, executable);
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        if (requiresEncryption) Assert.Null(claim);
        else
        {
            Assert.NotNull(claim);
            await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
            await DrainAsync(actor);
        }
        var workflow = (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!;
        Assert.Equal(requiresEncryption ? WorkflowExecutionStatus.Faulted : WorkflowExecutionStatus.Completed, workflow.Status);
        Assert.Equal(requiresEncryption ? 0 : 1, factory.Opens);
        Assert.Equal(requiresEncryption ? 0 : 1, factory.Calls);
        var value = workflow.RootVariableFrame!.Values["article-variable"];
        Assert.True(value.Policy.IsSensitive);
        Assert.True(value.Policy.HidesValue());
        if (!requiresEncryption) Assert.Equal("private result", value.InlineValue!.Value.GetString());
    }

    [Fact]
    public async Task Secret_business_input_is_refused_before_opening_a_client()
    {
        var factory = new FixtureFactory("{\"Article\":\"must not dispatch\"}");
        await using var provider = await ServicesAsync(factory);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true, requiresEncryption: true);
        var input = new RuntimeInputBinding("brief-key", new("String"), policy, RuntimeInputBindingSource.SecretRead, secret: new("logical-secret"));
        await StartAsync(provider, await ExecutableAsync(provider, briefBinding: input));
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(0, factory.Calls);
    }

    [Theory]
    [InlineData("result")]
    [InlineData("projection")]
    public async Task Encryption_required_result_policy_is_refused_before_opening_a_client(string mode)
    {
        var factory = new FixtureFactory("{\"Article\":\"must not dispatch\"}");
        await using var provider = await ServicesAsync(factory);
        await StartAsync(provider, await ExecutableAsync(provider, resultPolicyMode: mode));
        Assert.Null(await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2)));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(0, factory.Opens);
        Assert.Equal(0, factory.Calls);
    }

    [Fact]
    public async Task Sensitive_native_values_execute_with_redacted_inspection_evidence()
    {
        var factory = new FixtureFactory("{\"Article\":\"private result\"}");
        await using var provider = await ServicesAsync(factory);
        var policy = new ValueProtectionPolicy(DurableValueLifecycle.Instance, DurableValueStorage.Inline, isSensitive: true);
        var input = new RuntimeInputBinding("brief-key", new("String"), policy, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new("String"), Json("\"private brief\""), policy));
        var executable = await ExecutableAsync(provider, briefBinding: input, resultPolicyMode: "sensitive");
        var (actor, _) = await StartAsync(provider, executable);
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.Equal(1, factory.Calls);
        var boundary = (await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run"))
            .Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        Assert.True(boundary.Completion!.Result.Policy.IsSensitive);
        Assert.False(boundary.Completion.Result.Policy.RequiresEncryption);
        var inspection = (await provider.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync("run", boundary.InvocationId))!;
        Assert.Contains(inspection.ValueSnapshots, x => x.Subject == ActivityExecutionInspectionValueSubject.ActivityInput && x.IsSensitive);
        Assert.All(inspection.ValueSnapshots.Where(x => x.IsSensitive), x =>
        {
            Assert.Equal(RuntimePayloadCaptureMode.None, x.CaptureMode);
            Assert.Null(x.Payload);
        });
        Assert.DoesNotContain("private brief", JsonSerializer.Serialize(inspection), StringComparison.Ordinal);
        Assert.DoesNotContain("private result", JsonSerializer.Serialize(inspection), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Malformed_model_json_retains_the_parser_cause()
    {
        await using var provider = await ServicesAsync(new FixtureFactory("not JSON"));
        var node = (await ExecutableAsync(provider)).RootActivity;
        var request = new SpecialistTurnRequest(new(null, "default", "run", "activity", "operation", "artifact", "hash", "copywriter-v1", "template"),
            SpecialistNativeBinding.Descriptor(node), node.ActivityContract!, Json("{\"Brief\":\"brief\"}"), "binding");
        var exception = await Assert.ThrowsAsync<SpecialistTurnException>(async () => await provider.GetRequiredService<SpecialistModelRunner>().RunAsync(request, CancellationToken.None));
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
        Assert.Equal("The model result is not valid bounded JSON.", exception.Message);
    }

    [Fact]
    public void Malformed_descriptor_json_retains_the_parser_cause()
    {
        var node = new ExecutableNode("specialist", "specialist", "type", "1", new RuntimeActivityDescriptor(SpecialistRuntimeIds.ConsumerKey, "1", Json("{\"instructions\":{}}")),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        var exception = Assert.Throws<SpecialistTurnException>(() => SpecialistNativeBinding.Descriptor(node));
        Assert.IsAssignableFrom<JsonException>(exception.InnerException);
    }

    [Theory]
    [InlineData("Article", false, false)]
    [InlineData("Article", true, false)]
    [InlineData("Article.body", false, false)]
    [InlineData("Article", true, true)]
    [InlineData("Article.body", false, true)]
    public async Task Native_actor_runs_the_published_shape_on_a_background_worker_and_commits_its_typed_result(string outputName, bool allowContinuity, bool durableJournal)
    {
        using var files = new JournalFiles();
        var factory = new FixtureFactory(JsonSerializer.Serialize(new Dictionary<string, string> { [outputName] = "draft about moss gardens" }));
        await using var provider = await ServicesAsync(factory, durableJournal ? files.ConnectionString : null);
        var executable = await ExecutableAsync(provider, outputName, allowContinuity: allowContinuity);
        var (actor, start) = await StartAsync(provider, executable);
        Assert.Equal(0, factory.Calls);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        var boundary = Assert.Single(states.Where(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId));
        Assert.Null(boundary.Completion);
        Assert.True(boundary.PrivateState is not null, boundary.SubStatus + ": " + boundary.Fault?.Message + "\n" + boundary.Fault?.SanitizedStackTrace);
        Assert.Equal(JsonValueKind.String, boundary.PrivateState!.Value.InlineValue!.Value.ValueKind);
        var handle = boundary.PrivateState.Value.InlineValue.Value.GetString();
        Assert.Equal(64, handle!.Length);
        Assert.DoesNotContain("moss", boundary.PrivateState.Value.InlineValue.Value.GetRawText(), StringComparison.Ordinal);

        var workStore = provider.GetRequiredService<IBackgroundActivityWorkStore>();
        var claim = await workStore.ClaimNextAsync("specialist-test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        Assert.Equal(1, factory.Calls);
        Assert.Equal("Write the supplied brief.", factory.Instructions);
        Assert.Equal("fixture-model", factory.ModelId);
        Assert.Equal("moss gardens", JsonDocument.Parse(factory.LastUser!).RootElement.GetProperty("Brief").GetString());
        Assert.True(JsonElement.DeepEquals(executable.RootActivity.DescriptorPayload.GetProperty("resultSchema"), factory.Schema!.Value));
        Assert.Equal(1, factory.Disposals);
        await DrainAsync(actor);
        var workflow = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run");
        Assert.Equal(WorkflowExecutionStatus.Completed, workflow!.Status);
        states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        boundary = states.Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ExecutableNodeId);
        Assert.Equal("draft about moss gardens", boundary.Completion!.Result.InlineValue!.Value.GetProperty(outputName).GetString());
        var consumer = boundary with
        {
            Execution = new("consumer", "run", "consumer", "consumer", "reader", "1"),
            Status = ActivityExecutionStatus.Running, SchedulingActivityExecutionId = boundary.InvocationId, Completion = null
        };
        var binding = new RuntimeInputBinding("next", new("String"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
            activityResult: new(executable.RootActivity.ExecutableNodeId, "article-key", boundary.Provenance.ExecutionScopeId ?? "scope:root"));
        var resolved = provider.GetRequiredService<IRuntimeInputBindingResolver>().Resolve(binding,
            new("run", consumer.InvocationId, consumer, states.Append(consumer).ToArray(), executable));
        Assert.Equal("draft about moss gardens", resolved.Envelope!.InlineValue!.Value.GetString());
        var worker = states.Single(x => x.Execution.ExecutableNodeId == executable.RootActivity.ChildSlots.Single().Activities.Single().ExecutableNodeId);
        Assert.Equal(handle, worker.Completion!.Result.InlineValue!.Value.GetString());
        var descriptor = SpecialistNativeBinding.Descriptor(executable.RootActivity);
        var identity = new SpecialistInvocationIdentity(workflow.TenantId, workflow.Partition!.Value, workflow.WorkflowExecutionId,
            boundary.InvocationId, boundary.OperationKey, workflow.PinnedExecutable.ArtifactId, workflow.PinnedExecutable.ArtifactHash,
            descriptor.DefinitionVersionId!, descriptor.TemplateHash!);
        var accepted = await provider.GetRequiredService<SpecialistTurnJournal>().ResultAsync(handle, identity, worker.InvocationId, CancellationToken.None);
        if (allowContinuity)
        {
            Assert.NotNull(accepted.Session);
            Assert.Equal(handle, accepted.Session.ConversationId);
            Assert.Equal(1, accepted.Session.Revision);
            Assert.Equal(2, provider.GetRequiredService<SpecialistSessionCodec>().Restore(accepted.Session, identity).Count);
        }
        else Assert.Null(accepted.Session);
        await actor.EnqueueAsync(start);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        Assert.Equal(1, factory.Calls);
    }

    [Theory]
    [InlineData("{\"wrong\":true}", "String")]
    [InlineData("not JSON", "String")]
    [InlineData("{\"Article\":\"not-a-guid\"}", "Guid")]
    public async Task Invalid_model_result_cannot_create_a_native_completion(string response, string outputType)
    {
        var factory = new FixtureFactory(response);
        await using var provider = await ServicesAsync(factory);
        var (actor, _) = await StartAsync(provider, await ExecutableAsync(provider, outputType: outputType));
        var claim = await provider.GetRequiredService<IBackgroundActivityWorkStore>().ClaimNextAsync("test", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(2));
        Assert.NotNull(claim);
        await provider.GetRequiredService<BackgroundActivityWorker>().ExecuteClaimAsync(new("default"), claim);
        await DrainAsync(actor);
        Assert.Equal(1, factory.Calls);
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("run");
        Assert.All(states, x => Assert.Null(x.Completion));
        Assert.Equal(WorkflowExecutionStatus.Faulted, (await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
    }

    private static async ValueTask<ServiceProvider> ServicesAsync(FixtureFactory factory, string? journalConnection = null, TimeProvider? clock = null, LeaseTrace? leaseTrace = null, PersistenceMode persistenceMode = PersistenceMode.Durable, bool allowNewOperations = true, bool streamResponses = false)
    {
        var services = new ServiceCollection();
        if (clock is not null) services.AddSingleton(clock);
        services.AddLogging();
        services.AddWorkflowRuntime();
        services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
        services.AddSingleton<RetryCompletionLog>();
        services.AddSingleton<IRuntimeDurableValueStorageDriverRegistry>(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()]));
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new Elsa.Activities.Sequence.ActivitiesSequenceFeature().ConfigureServices(services);
        new Elsa.Activities.Primitives.ActivitiesPrimitivesFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
        new BackgroundActivitiesFeature { RunWorkers = false }.ConfigureServices(services);
        new SecretsFeature { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }.ConfigureServices(services);
        new SpecialistsRuntimeFeature { AllowNewOperations = allowNewOperations, StreamResponses = streamResponses }.ConfigureServices(services);
        new SpecialistsInMemoryJournalFeature().ConfigureServices(services);
        if (journalConnection is not null)
            Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.DependencyInjection.SpecialistsEntityFrameworkCoreRegistration.AddSpecialistsEntityFrameworkCore(services,
                new() { Provider = "Sqlite", ConnectionString = journalConnection });
        services.AddSingleton<ISpecialistModelClientFactory>(factory);
        leaseTrace?.Decorate(services);
        if (leaseTrace?.ChangeBindingAtChildCompletion == true)
            services.AddScoped<IRuntimeCheckpointCommitEnricher>(provider => new LateBindingMutation(provider.GetRequiredService<IActivityExecutionStateStore>()));
        services.AddPersistenceModeOptions(new PersistenceModeOptions { Mode = persistenceMode });
        services.AddPersistenceModes();
        var provider = services.BuildServiceProvider();
        if (journalConnection is not null)
            await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.MigrateAsync(provider.GetRequiredService<Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.SpecialistsDbContext>().Database);
        var types = provider.GetRequiredService<IWellKnownTypeRegistry>();
        types.RegisterType(typeof(string), "String");
        types.RegisterType(typeof(JsonElement), "System.Text.Json.JsonElement");
        types.RegisterType(typeof(Elsa.Activities.Sequence.Activities.Sequence), typeof(Elsa.Activities.Sequence.Activities.Sequence).FullName!);
        types.RegisterType(typeof(ActivityUnit), typeof(ActivityUnit).FullName!);
        types.RegisterType(typeof(RetryInterventionActivity), typeof(RetryInterventionActivity).FullName!);
        await new SpecialistTypesStartupTask(types).ExecuteAsync(CancellationToken.None);
        return provider;
    }

    private sealed class JournalFiles : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "elsa-specialist-native-journal-" + Guid.NewGuid().ToString("N"));
        public JournalFiles() => Directory.CreateDirectory(directory);
        public string ConnectionString => "Data Source=" + Path.Combine(directory, "journal.db") + ";Pooling=False";
        public void Dispose() => Directory.Delete(directory, true);
    }

    private static async ValueTask<WorkflowExecutable> ExecutableAsync(ServiceProvider provider, string outputName = "Article", string outputType = "String", bool outputNullable = false, bool optionalBrief = false, RuntimeInputBinding? briefBinding = null, IReadOnlyDictionary<string, RuntimeOutputCapture>? outputCaptures = null, IReadOnlyCollection<RuntimeVariableDeclaration>? variables = null, string? resultPolicyMode = null, bool allowContinuity = false, bool invocationControls = false, RuntimeInputBinding? optionsBinding = null, SpecialistTurnLimits? limits = null, string? controlsProfile = null)
    {
        // The authoring fixture is disposed, including its three SQLite stores, before execution starts.
        var published = await SpecialistPublicationTests.PublishAndPlaceAsync(outputName, outputType, outputNullable, optionalBrief, briefBinding, outputCaptures, allowContinuity, invocationControls, optionsBinding: optionsBinding, limits: limits);
        var root = published.Placement.Root;
        if (resultPolicyMode is not null)
        {
            // A seeded artifact backstop: generic publication currently has no authored policy fields.
            var original = root.ActivityContract!;
            var policy = ActivityValuePolicy.Default with { IsSensitive = true, RequiresEncryption = resultPolicyMode != "sensitive" };
            var result = new ActivityResultContract(original.Result.Type, original.Result.IsRequired,
                resultPolicyMode == "projection" ? original.Result.Policy : policy,
                original.Result.Projections.Values.Select(x => new ActivityResultProjectionContract(x.Key, x.Path, x.Type, x.IsRequired,
                    resultPolicyMode == "result" ? x.Policy : policy, x.SourceRepresentation)), original.Result.SourceRepresentation);
            var contract = new ActivityContract(original.ActivityTypeKey, original.ContractVersion, original.DescriptorKind, original.DescriptorPayload,
                original.Inputs.Values, result, original.Outcomes, original.Activation, original.SideEffectProfile, original.SupportsBackgroundExecution);
            root = new(root.ExecutableNodeId, root.AuthoredActivityId, root.ActivityType, root.ActivityTypeVersion, root.Descriptor,
                root.InputBindings, root.Metadata, root.ChildSlots, root.Structure, contract);
        }
        if (controlsProfile is not null)
        {
            // Retained compiled reader fixture: change only the old profile identity, preserving
            // the pinned runtime descriptor/contract pair and native child placement.
            var payload = JsonSerializer.SerializeToElement(SpecialistNativeBinding.Descriptor(root) with { InvocationControlsProfile = controlsProfile }, SpecialistJson.Options);
            var before = root.ActivityContract!;
            var contract = new ActivityContract(before.ActivityTypeKey, before.ContractVersion, before.DescriptorKind, payload,
                before.Inputs.Values, before.Result, before.Outcomes, before.Activation, before.SideEffectProfile, before.SupportsBackgroundExecution);
            root = new(root.ExecutableNodeId, root.AuthoredActivityId, root.ActivityType, root.ActivityTypeVersion,
                new RuntimeActivityDescriptor(root.DescriptorType, root.DescriptorSchemaVersion, payload), root.InputBindings, root.Metadata, root.ChildSlots, root.Structure, contract);
        }
        var executable = new WorkflowExecutable(new("editorial", "workflow", "workflow-v1", "1.0.0", "artifact-hash"), root,
            published.Placement.ResumeTargets, DateTimeOffset.UtcNow, new Dictionary<string, string>(), inputContract: null, dependencies: null,
            published.Placement.RuntimeRequirements, published.Storage, IncidentStrategyBuiltIns.FaultReference, workflowVariables: variables);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        return executable;
    }

    private static async ValueTask<(IWorkflowExecutionActor Actor, WorkflowExecutionCommandEnvelope Start)> StartAsync(ServiceProvider provider, WorkflowExecutable executable, WorkflowExecutionAuthoritySnapshot? authority = null)
    {
        var now = DateTimeOffset.UtcNow;
        var actor = await provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new("run", WorkflowExecutionActorActivationReason.Start, now,
            "specialist-tests", WorkflowExecutionActorCapabilities.InProcessMailbox));
        var metadata = new Dictionary<string, string> { ["runtime.artifactId"] = executable.Identity.ArtifactId };
        var command = new WorkflowExecutionCommand("start", "run", WorkflowExecutionCommandKind.Start, now,
            JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(executable.Identity, executable.Identity.ArtifactId,
                variables: null, inputs: null, stimulusInput: null, triggerNodeId: null, runKind: WorkflowRunKind.Unknown, pinnedSource: null,
                parentWorkflowExecutionId: null, correlationId: null, tenantId: null, partition: new("default"), authority: authority)), metadata);
        var envelope = new WorkflowExecutionCommandEnvelope("start-envelope", "run", command, "start", WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now, 1, metadata);
        await actor.EnqueueAsync(envelope);
        return (actor, envelope);
    }

    private static async ValueTask DrainAsync(IWorkflowExecutionActor actor, string suffix = "")
    {
        var now = DateTimeOffset.UtcNow;
        var command = new WorkflowExecutionCommand("drain" + suffix, "run", WorkflowExecutionCommandKind.RunSchedulerWork, now, null, new Dictionary<string, string>());
        await actor.EnqueueAsync(new("drain-envelope" + suffix, "run", command, "drain" + suffix, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now));
    }

    private static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();
    private sealed class FixtureFactory(string response, bool enforcesAccounting = true, string usageMode = "known") : ISpecialistModelClientFactory
    {
        private string Response { get; } = response;
        private string UsageMode { get; } = usageMode;
        public string ProviderKey => "fixture";
        public int Opens { get; private set; }
        public int Calls { get; private set; }
        public int Disposals { get; private set; }
        public int AtomicCalls { get; private set; }
        public int StreamingCalls { get; private set; }
        public bool SupportsStreaming { get; init; } = true;
        public string StreamMode { get; init; } = "success";
        public TaskCompletionSource? StreamTerminalGate { get; init; }
        public TaskCompletionSource StreamPrefixSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string? Instructions { get; private set; }
        public string? LastUser { get; private set; }
        public string? ModelId { get; private set; }
        public int? MaximumOutputTokens { get; private set; }
        public JsonElement? Schema { get; private set; }
        public TaskCompletionSource? ResponseGate { get; init; }
        public Func<ValueTask>? BeforeResponse { get; set; }
        public TaskCompletionSource RequestSent { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];
        public ValueTask<SpecialistModelClientLease> OpenAsync(SpecialistModelOpenRequest request, CancellationToken cancellationToken = default)
        {
            Opens++;
            return ValueTask.FromResult(new SpecialistModelClientLease(new Client(this, request), new ChatOptions { MaxOutputTokens = 4096 }, "fixture/1", 1)
                { EnforcesDispatchAuthority = enforcesAccounting && request.DispatchAuthority is not null, SupportsStreaming = SupportsStreaming });
        }
        private sealed class Client(FixtureFactory owner, SpecialistModelOpenRequest request) : IChatClient
        {
            public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            {
                owner.AtomicCalls++;
                return RespondAsync(messages, options, cancellationToken);
            }
            private async Task<ChatResponse> RespondAsync(IEnumerable<ChatMessage> messages, ChatOptions? options, CancellationToken cancellationToken)
            {
                if (request.DispatchAuthority is { } authority)
                    await authority.BeforeSendAsync(new("fixture", request.Descriptor.Model.ModelId, "fixture-incarnation", 1, "fixture/1", "external-deterministic-one-response/1"), cancellationToken);
                owner.Calls++;
                owner.Requests.Add(messages.ToArray());
                owner.RequestSent.TrySetResult();
                if (owner.ResponseGate is not null) await owner.ResponseGate.Task.WaitAsync(cancellationToken);
                if (owner.BeforeResponse is { } callback) await callback();
                owner.LastUser = messages.Last(x => x.Role == ChatRole.User).Text;
                owner.Instructions = options?.Instructions;
                owner.ModelId = options?.ModelId;
                owner.MaximumOutputTokens = options?.MaxOutputTokens;
                owner.Schema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema;
                return new ChatResponse(new ChatMessage(ChatRole.Assistant, owner.Response))
                {
                    ModelId = "provider-reported-fixture-model",
                    Usage = owner.UsageMode switch
                    {
                        "missing" => null,
                        "partial" => new UsageDetails { InputTokenCount = 5 },
                        "negative" => new UsageDetails { InputTokenCount = 5, OutputTokenCount = -1 },
                        "overrun" => new UsageDetails { InputTokenCount = 5, OutputTokenCount = 8192 },
                        _ => new UsageDetails { InputTokenCount = 5, OutputTokenCount = 3 }
                    }
                };
            }
            public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
                [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                owner.StreamingCalls++;
                var response = await RespondAsync(messages, options, cancellationToken);
                yield return new(ChatRole.Assistant, [new TextReasoningContent("fixture-private-reasoning"), new TextContent(response.Text)]);
                owner.StreamPrefixSent.TrySetResult();
                if (owner.StreamTerminalGate is { } gate) await gate.Task.WaitAsync(cancellationToken);
                if (owner.StreamMode == "missing-terminal") yield break;
                yield return new ChatResponseUpdate { FinishReason = owner.StreamMode == "refusal" ? ChatFinishReason.ContentFilter : ChatFinishReason.Stop, ModelId = response.ModelId };
                if (response.Usage is not null) yield return new(ChatRole.Assistant, [new UsageContent(response.Usage)]);
            }
            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() => owner.Disposals++;
        }
    }
}
