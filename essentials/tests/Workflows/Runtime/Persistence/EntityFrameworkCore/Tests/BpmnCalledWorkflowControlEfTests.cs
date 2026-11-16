using System.Text.Json;
using System.Collections.Concurrent;
using CShells.Lifecycle;
using Elsa.Activities.Bpmn;
using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using DispatchActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;
using F = Elsa.Activities.Bpmn.Tests.BpmnRuntimeFixture;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Actual dispatched BPMN roots and caller control delivery through SQLite and reopened providers.</summary>
public sealed partial class BpmnCalledWorkflowControlEfTests : IDisposable
{
    private const string ParentExecutionId = "called-control-ef-parent";
    private const string DispatchNodeId = "node-call";
    private const string HandlerNodeId = "node-error-handler";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly string _databasePath = Path.Join(Path.GetTempPath(), $"elsa-called-control-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _clock = new(DateTimeOffset.UtcNow);

    [Theory]
    [InlineData("ORDER", PersistenceMode.Durable)]
    [InlineData("ORDER", PersistenceMode.Balanced)]
    [InlineData("OTHER", PersistenceMode.Durable)]
    [InlineData("OTHER", PersistenceMode.Balanced)]
    public async Task Actual_called_coded_error_recovers_before_caller_delivery_without_rewriting_child_fault(
        string authoredCode, PersistenceMode mode)
    {
        var cut = new CalledResumeCut { RefuseDelivery = true };
        var expectedCallerStatus = authoredCode == "ORDER" ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Faulted;
        string childId;
        string sourceId;
        string originalChildFacts;
        string originalIntent;
        WorkflowExecutableIdentity childPin;
        RuntimeChildControlTransport originalControl;
        await using (var first = await StartAsync(mode, cut, "first"))
        {
            var child = await ChildExecutableAsync(authoredCode);
            childPin = child.Identity;
            var childReference = await first.PublishAsync(child, "source-called-child");
            var parentReference = await first.PublishAsync(await ParentExecutableAsync(childReference, child.Identity), "source-called-parent");
            await first.StartPublishedAsync(parentReference, ParentExecutionId,
                partition: new WorkflowExecutionPartition(PersistenceScope.DefaultValue));
            for (var attempt = 0; attempt < 8 && cut.LastIntent is null; attempt++)
                await first.SweepAsync();
            if (cut.LastIntent is null)
            {
                await using var diagnostic = first.Services.CreateAsyncScope();
                var dispatches = await diagnostic.ServiceProvider.GetRequiredService<IWorkflowDispatchStore>().ListAsync(ParentExecutionId);
                Assert.Fail(JsonSerializer.Serialize(new
                {
                    parent = await first.ReadRunAsync(ParentExecutionId), dispatches,
                    incidents = await diagnostic.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(ParentExecutionId),
                    children = await Task.WhenAll(dispatches.Select(async item => await first.ReadRunAsync(item.ChildWorkflowExecutionId)))
                }, Json));
            }
            var intent = Assert.IsType<RuntimePostCommitIntent>(cut.LastIntent);
            originalIntent = JsonSerializer.Serialize(intent, Json);
            var payload = intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!;
            childId = payload.ChildWorkflowExecutionId;
            var control = originalControl = Assert.IsType<RuntimeChildControlTransport>(payload.Result.Control);
            sourceId = control.SourceOutboxItemId;
            Assert.Equal(RuntimePostCommitOutboxIdentity.CreateLogicalValue(control.SourceCommitId, control.SourceIntentId), sourceId);
            Assert.True(control.Batch.IsTerminal);
            Assert.Equal(0, control.Batch.Ordinal);
            Assert.Empty(control.Batch.Notifications);
            Assert.Equal(authoredCode, control.Batch.AuthoredFailureCode);
            Assert.Equal(childId, control.Batch.ChildWorkflowExecutionId);
            Assert.Equal(WorkflowDispatchStatus.Faulted, payload.Result.Status);
            Assert.Equal(DispatchWorkflowDiagnostics.FaultedCode, payload.Result.DiagnosticMetadata["code"]);
            Assert.DoesNotContain(authoredCode, JsonSerializer.Serialize(payload.Result.DiagnosticMetadata, Json));
            Assert.Equal(WorkflowExecutionStatus.Faulted, (await first.ReadRunAsync(childId)).WorkflowState!.Status);
            Assert.Equal(childPin, (await first.ReadRunAsync(childId)).WorkflowState!.PinnedExecutable);
            Assert.Equal(WorkflowExecutionStatus.Running, (await first.ReadRunAsync(ParentExecutionId)).WorkflowState!.Status);
            Assert.Equal(ActivityExecutionStatus.Suspended, (await first.ReadRunAsync(ParentExecutionId)).State(DispatchNodeId).Status);
            Assert.Equal(mode.ToString(), (await first.ReadRunAsync(ParentExecutionId)).WorkflowState!.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
            Assert.Equal(WorkflowExecutionHarness.Identity, (await first.ReadRunAsync(ParentExecutionId)).WorkflowState!.PinnedExecutable);
            var pending = await SourceAsync(first, sourceId);
            Assert.Equal(originalIntent, JsonSerializer.Serialize(pending.Intent, Json));
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, pending.Status);
            Assert.Equal(1, pending.DeliveryAttemptCount);
            originalChildFacts = await ChildFactsAsync(first, childId, authoredCode, childPin, mode);
        }

        _clock.Advance(TimeSpan.FromMinutes(1));
        var lostAck = new CalledResumeCut { LoseAcknowledgement = true };
        string stableParent;
        await using (var recovered = await StartAsync(mode, lostAck, "recovered"))
        {
            Assert.Equal(originalIntent, JsonSerializer.Serialize((await SourceAsync(recovered, sourceId)).Intent, Json));
            Assert.Equal(originalChildFacts, await ChildFactsAsync(recovered, childId, authoredCode, childPin, mode));
            for (var attempt = 0; attempt < 8; attempt++)
            {
                await recovered.SweepAsync();
                if ((await recovered.ReadRunAsync(ParentExecutionId)).WorkflowState!.Status.IsTerminal())
                    break;
            }
            var parent = await recovered.ReadRunAsync(ParentExecutionId);
            Assert.Equal(expectedCallerStatus, parent.WorkflowState!.Status);
            Assert.Equal(ActivityExecutionStatus.Completed, parent.State(DispatchNodeId).Status);
            Assert.Null(parent.State(DispatchNodeId).Fault);
            var inbox = Assert.IsType<RuntimeChildControlInbox>(parent.State(DispatchNodeId).ChildControlInbox);
            Assert.True(inbox.IsTerminalConsumed);
            Assert.Equal(1, inbox.NextOrdinal);
            Assert.Empty(inbox.Pending);
            Assert.True(inbox.Target.MatchesAddress(originalControl));
            Assert.True(inbox.HasAccepted(originalControl));
            Assert.False(string.IsNullOrWhiteSpace(parent.State(DispatchNodeId).Metadata.GetValueOrDefault(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId)));
            parent.AssertOutcomes(DispatchNodeId, "Faulted");
            if (authoredCode == "ORDER")
            {
                parent.AssertCompleted(HandlerNodeId);
                await using var callerScope = recovered.Services.CreateAsyncScope();
                Assert.Empty(await callerScope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(ParentExecutionId));
            }
            else
            {
                parent.AssertDidNotRun(HandlerNodeId);
                Assert.Equal(authoredCode, parent.State(F.ProcessNodeId).Fault!.Metadata.GetValueOrDefault(ActivityFault.FaultTypeMetadataKey));
            }
            Assert.Equal(originalChildFacts, await ChildFactsAsync(recovered, childId, authoredCode, childPin, mode));
            Assert.Equal(childPin, (await recovered.ReadRunAsync(childId)).WorkflowState!.PinnedExecutable);
            Assert.True(lostAck.CallerCommittedBeforeAcknowledgement);
            Assert.Equal(originalIntent, JsonSerializer.Serialize(lostAck.LastIntent, Json));
            var awaitingRetry = await SourceAsync(recovered, sourceId);
            Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, awaitingRetry.Status);
            Assert.Equal(2, awaitingRetry.DeliveryAttemptCount);
            stableParent = await CallerFactsAsync(recovered, expectedCallerStatus, mode);
            await using var scope = recovered.Services.CreateAsyncScope();
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(ParentExecutionId));
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(childId));
        }

        _clock.Advance(TimeSpan.FromMinutes(1));
        await using (var retried = await StartAsync(mode, new CalledResumeCut(), "retried"))
        {
            Assert.Equal(stableParent, await CallerFactsAsync(retried, expectedCallerStatus, mode));
            Assert.Equal(originalChildFacts, await ChildFactsAsync(retried, childId, authoredCode, childPin, mode));
            await retried.SweepAsync();
            var delivered = await SourceAsync(retried, sourceId);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
            Assert.Equal(3, delivered.DeliveryAttemptCount);
            Assert.Equal(originalIntent, JsonSerializer.Serialize(delivered.Intent, Json));
            Assert.Equal(stableParent, await CallerFactsAsync(retried, expectedCallerStatus, mode));
            Assert.Equal(originalChildFacts, await ChildFactsAsync(retried, childId, authoredCode, childPin, mode));
            await using var scope = retried.Services.CreateAsyncScope();
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(ParentExecutionId));
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(childId));
        }
    }

    private async Task<WorkflowExecutionHarness> StartAsync(PersistenceMode mode, CalledResumeCut cut, string generation,
        CalledClaimCut? claimCut = null)
    {
        var host = WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Events.EventsFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesBpmnFeature().ConfigureServices(services))
            .WithFeature(services => new Elsa.Activities.Bpmn.Design.ActivitiesBpmnDesignFeature().ConfigureServices(services))
            .WithFeature(services => new DispatchWorkflowRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                services.AddSingleton<TimeProvider>(_clock);
                services.Replace(ServiceDescriptor.Scoped<IRuntimePostCommitIntentDispatcher>(provider =>
                {
                    IRuntimePostCommitIntentDispatcher dispatcher = new CalledResumeCutDispatcher(
                        ActivatorUtilities.CreateInstance<RuntimePostCommitIntentDispatcher>(provider), cut, provider);
                    return claimCut is null ? dispatcher : new CalledClaimCutDispatcher(dispatcher, claimCut);
                }));
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite", ConnectionString = $"Data Source={_databasePath};Pooling=False",
                    RecoveryContinuationSigningKey = "called-control-ef-recovery-signing-key-32",
                    HierarchyCursorSigningKey = "called-control-ef-hierarchy-signing-key-32"
                }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                var runtime = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBpmnProcessRuntime));
                services.Remove(runtime);
                services.Add(new ServiceDescriptor(typeof(IBpmnProcessRuntime), provider => new ObservedCalledBpmnRuntime(
                    (IBpmnProcessRuntime)ActivatorUtilities.CreateInstance(provider, runtime.ImplementationType!), cut), runtime.Lifetime));
            })
            .WithPersistenceMode(mode)
            .Build(WorkflowExecutionHarness.Identity, ParentExecutionId,
                Enumerable.Range(1, 80).Select(index => $"called-{generation}-{index}"));
        foreach (var initializer in host.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        host.InitializeActivityTypes();
        return host;
    }

    private static async Task<WorkflowExecutable> ChildExecutableAsync(string authoredCode)
    {
        await using var builder = await F.CreateAsync([]);
        var graph = builder.NewExecutable([], [F.StartEvent(),
                new BpmnElement("error-end", BpmnElementTypes.EndEvent,
                    eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Error,
                        new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = authoredCode })])],
            [F.Flow("raise", "start", "error-end")]);
        return new WorkflowExecutable(new WorkflowExecutableIdentity("artifact-called-child", "definition-called-child", "version-called-child", "1.0.0", "sha256:called-child"),
            graph.RootActivity, graph.ResumeTargets, graph.CreatedAt, graph.CompatibilityMetadata,
            new WorkflowExecutableInputContract(WorkflowExecutableInputContract.CurrentVersion, []), [], graph.IncidentStrategy);
    }

    private static async Task<WorkflowExecutable> ParentExecutableAsync(WorkflowExecutableSourceReference childReference, WorkflowExecutableIdentity child)
    {
        await using var builder = await F.CreateAsync([]);
        var contract = ClrActivityContractTestBuilder.BuildContract(typeof(DispatchActivity));
        var dispatch = new ExecutableNode(DispatchNodeId, "authored-call", DispatchWorkflowConstants.ActivityType, "1.0.0",
            contract.DescriptorKind, contract.DescriptorPayload,
            ClrActivityContractTestBuilder.CompleteInputBindings(contract, new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(DispatchActivity.WorkflowDefinitionId)] = Literal(nameof(DispatchActivity.WorkflowDefinitionId), child.DefinitionId),
                [nameof(DispatchActivity.WaitForCompletion)] = Literal(nameof(DispatchActivity.WaitForCompletion), true)
            }), new Dictionary<string, string>
            {
                [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(new DispatchWorkflowPin(child, WorkflowExecutableSourceProvenance.From(childReference)), Json)
            }, activityContract: contract);
        var boundary = new BpmnElement("error-boundary", BpmnElementTypes.BoundaryEvent, attachedToRef: "call", cancelActivity: true,
            eventDefinitions: [new BpmnEventDefinition(BpmnEventDefinitionTypes.Error,
                new Dictionary<string, string> { [BpmnEventDefinitionProperties.Code] = "ORDER" })]);
        return builder.NewExecutable([dispatch, builder.NewProbeNode(HandlerNodeId)],
            [F.StartEvent(), F.CallActivity("call", DispatchNodeId), boundary, F.Task("handler", HandlerNodeId), F.EndEvent("normal-end"), F.EndEvent("caught-end")],
            [F.Flow("begin", "start", "call"), F.Flow("normal", "call", "normal-end"), F.Flow("catch", "error-boundary", "handler"), F.Flow("caught", "handler", "caught-end")],
            [F.NodeResumeTarget(DispatchNodeId, DispatchWorkflowConstants.CompletionResumeTargetId)], controlCapable: true);
    }

    private static RuntimeInputBinding Literal(string key, object value)
    {
        var type = new ValueTypeDescriptor(value is bool ? "Boolean" : "String");
        return new(key, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    private static async Task<RuntimePostCommitOutboxItem> SourceAsync(WorkflowExecutionHarness host, string id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return Assert.IsType<RuntimePostCommitOutboxItem>(await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindAsync(id));
    }

    private static async Task<string> ChildFactsAsync(WorkflowExecutionHarness host, string id, string code,
        WorkflowExecutableIdentity expectedPin, PersistenceMode mode)
    {
        var run = await host.ReadRunAsync(id);
        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState!.Status);
        Assert.Equal(expectedPin, run.WorkflowState.PinnedExecutable);
        Assert.Equal(mode.ToString(), run.WorkflowState.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
        var root = run.State(F.ProcessNodeId);
        Assert.Equal(ActivityExecutionStatus.Faulted, root.Status);
        Assert.Equal("bpmn.error", root.Fault!.Metadata[ActivityFault.CategoryMetadataKey]);
        Assert.Equal(code, root.Fault.Metadata[ActivityFault.FaultTypeMetadataKey]);
        await using var scope = host.Services.CreateAsyncScope();
        var incident = Assert.Single(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(id));
        Assert.True(incident.IsBlocking);
        Assert.Equal("ActivityReturnedFault", incident.FailureType);
        Assert.Equal(root.InvocationId, incident.ActivityExecutionId);
        Assert.NotEqual(IncidentResolutionActionKinds.AbsorbFault, incident.ResolutionOutcome?.ActionKind);
        return JsonSerializer.Serialize(new { activities = run.ActivityStates, incident }, Json);
    }

    private static async Task<string> CallerFactsAsync(WorkflowExecutionHarness host, WorkflowExecutionStatus expectedStatus, PersistenceMode mode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var workflow = Assert.IsType<WorkflowExecutionState>(await provider.GetRequiredService<EfWorkflowExecutionStateStore>().FindAsync(ParentExecutionId));
        Assert.Equal(expectedStatus, workflow.Status);
        Assert.Equal(WorkflowExecutionHarness.Identity, workflow.PinnedExecutable);
        Assert.Equal(mode.ToString(), workflow.SystemMetadata[RuntimeMetadataKeys.PersistenceMode]);
        var activities = await provider.GetRequiredService<EfActivityExecutionStateStore>().ListAllAsync(ParentExecutionId);
        var incidents = await provider.GetRequiredService<EfIncidentStateStore>().ListAsync(ParentExecutionId);
        var bookmarks = await provider.GetRequiredService<EfBookmarkStateStore>().ListAllBookmarkStatesAsync(ParentExecutionId);
        Assert.Empty(bookmarks);
        return JsonSerializer.Serialize(new
        {
            workflow.Status, workflow.PinnedExecutable,
            persistedMode = workflow.SystemMetadata[RuntimeMetadataKeys.PersistenceMode],
            activities = activities.OrderBy(state => state.InvocationId, StringComparer.Ordinal),
            incidents = incidents.OrderBy(incident => incident.IncidentId, StringComparer.Ordinal)
        }, Json);
    }

    public void Dispose()
    {
        foreach (var file in new[] { _databasePath, _databasePath + "-wal", _databasePath + "-shm" })
            File.Delete(file);
    }

    private sealed record CalledControlObservation(string WorkflowExecutionId, string CallingActivityExecutionId, long[] Ordinals);
    private sealed record CalledControlVerdict(string WorkflowExecutionId, RuntimeStructuralContinuationKind Kind, string[] ScheduledNodes, string[] CancelledActivities);

    private sealed class ObservedCalledBpmnRuntime(IBpmnProcessRuntime inner, CalledResumeCut observation) : IBpmnProcessRuntime
    {
        public ValueTask<RuntimeStructuralContinuation> StartAsync(IRuntimeActivityExecutionContext context) => inner.StartAsync(context);
        public ValueTask<RuntimeStructuralContinuation> CompleteAsync(IRuntimeActivityExecutionContext context, ActivityChildCompletedContext completion) => inner.CompleteAsync(context, completion);
        public ValueTask<RuntimeStructuralContinuation> FaultAsync(IRuntimeActivityExecutionContext context, ActivityChildFaultedContext fault) => inner.FaultAsync(context, fault);
        public ValueTask<RuntimeStructuralContinuation> SignalAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext signal) => inner.SignalAsync(context, signal);
        public ValueTask<RuntimeStructuralContinuation> OutcomeAsync(IRuntimeActivityExecutionContext context, ActivityChildOutcomeContext outcome) => inner.OutcomeAsync(context, outcome);
        public async ValueTask<RuntimeStructuralContinuation> ControlAsync(IRuntimeActivityExecutionContext context, RuntimeChildControlContext control)
        {
            observation.ControlCalls.Enqueue(new(context.WorkflowExecutionId, control.CallingActivityExecutionId,
                control.Batches.Select(batch => batch.Batch.Ordinal).ToArray()));
            var result = await inner.ControlAsync(context, control);
            observation.ControlVerdicts.Enqueue(new(context.WorkflowExecutionId, result.Kind,
                context.GetChildActivityScheduleRequests().Select(request => request.ExecutableNodeId).ToArray(),
                context.GetChildSubtreeCancellationRequests().Select(request => request.ChildActivityExecutionId).ToArray()));
            return result;
        }
    }

    private sealed class CalledResumeCut
    {
        public ConcurrentQueue<RuntimePostCommitIntent> ObservedIntents { get; } = new();
        public ConcurrentQueue<CalledControlObservation> ControlCalls { get; } = new();
        public ConcurrentQueue<CalledControlVerdict> ControlVerdicts { get; } = new();
        public bool HoldNotifications { get; set; }
        public RuntimePostCommitIntent? LastHeldNotification { get; set; }
        public bool RefuseDelivery { get; init; }
        public bool LoseAcknowledgement { get; init; }
        public bool CallerCommittedBeforeAcknowledgement { get; set; }
        public RuntimePostCommitIntent? LastIntent { get; set; }
    }

    private sealed class CalledResumeCutDispatcher(IRuntimePostCommitIntentDispatcher inner, CalledResumeCut cut,
        IServiceProvider provider) : IRuntimePostCommitIntentDispatcher
    {
        public bool RequiresDurableSource(string kind) => inner.RequiresDurableSource(kind);
        public async ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            CheckBefore(intent);
            await inner.DispatchAsync(intent, cancellationToken);
            await CheckAfterAsync(intent, cancellationToken);
        }
        public async ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        {
            CheckBefore(source.Intent);
            await inner.DispatchAsync(source, cancellationToken);
            await CheckAfterAsync(source.Intent, cancellationToken);
        }
        private void CheckBefore(RuntimePostCommitIntent intent)
        {
            cut.ObservedIntents.Enqueue(intent);
            if (cut.HoldNotifications && intent.Kind == NotifyControlIntentKind)
            {
                cut.LastHeldNotification = intent;
                throw new TimeoutException("Controlled delayed nonterminal control before dispatch.");
            }
            if (!cut.RefuseDelivery || intent.Kind != DispatchWorkflowConstants.ResumeParentIntentKind) return;
            cut.LastIntent = intent;
            throw new TimeoutException("Controlled outage before actual called-workflow terminal delivery.");
        }
        private async Task CheckAfterAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken)
        {
            if (!cut.LoseAcknowledgement || intent.Kind != DispatchWorkflowConstants.ResumeParentIntentKind) return;
            cut.LastIntent = intent;
            var payload = intent.Payload!.Value.Deserialize<WorkflowDispatchParentResumePayload>(Json)!;
            var control = payload.Result.Control!;
            for (var attempt = 0; attempt < 3000; attempt++)
            {
                await using var scope = provider.CreateAsyncScope();
                var call = await scope.ServiceProvider.GetRequiredService<EfActivityExecutionStateStore>()
                    .FindAsync(payload.ParentWorkflowExecutionId, payload.ParentActivityExecutionId, cancellationToken);
                if (call?.ChildControlInbox is { IsTerminalConsumed: true } inbox && inbox.HasAccepted(control))
                {
                    var source = Assert.IsType<RuntimePostCommitOutboxItem>(await scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>()
                        .FindAsync(control.SourceOutboxItemId, cancellationToken));
                    Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, source.Status);
                    Assert.True(source.DeliveryFencingToken > 0);
                    Assert.Equal(1, source.DeliveryAttemptCount);
                    Assert.Equal(JsonSerializer.Serialize(intent, Json), JsonSerializer.Serialize(source.Intent, Json));
                    cut.CallerCommittedBeforeAcknowledgement = true;
                    throw new TimeoutException("Controlled acknowledgement loss after the actual caller control checkpoint committed.");
                }
                await Task.Delay(10, cancellationToken);
            }
            Assert.Fail("Caller control did not commit before the acknowledgement cut.");
        }
    }
}
