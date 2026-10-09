using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Testing;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Diagnostics;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using ActivityListener = System.Diagnostics.ActivityListener;
using ActivitySamplingResult = System.Diagnostics.ActivitySamplingResult;
using ActivitySource = System.Diagnostics.ActivitySource;
using Span = System.Diagnostics.Activity;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// End-to-end guard for the <c>Fault</c> leaf activity: running a workflow whose only activity is a
/// <c>Fault</c> must record a blocking incident through the engine incident model rather than throwing
/// out to the host. The agent accepts the command, the run does not surface the exception to the caller,
/// the activity state is Faulted, and an <c>IncidentState</c> is persisted for inspection/intervention.
/// </summary>
/// <remarks>
/// It also guards masking (spec 188, slice 8: T073, T094): a value resolved from a secret for an activity execution
/// never reaches that execution's recorded fault or incident, whether activity code throws it or returns it in a fault,
/// on the invoke, bookmark resume, parent completion and parent notification paths. The text shows
/// <c>[secret:&lt;name&gt;]</c> instead, the original exception type name is kept, and masking changes neither a
/// fault's classification nor an activation failure's.
/// </remarks>
public sealed class FaultIncidentExecutionTests
{
    private const string SecretNodeId = "node-secret";
    private const string SecretExecutionId = "actexec-secret";
    private const string ParentNodeId = "node-parent";
    private const string ParentExecutionId = "actexec-parent";
    private const string Marker = "[secret:payments.api-key]";

    private readonly DateTimeOffset _now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeRuntimeSecretResolver _resolver = new();
    private readonly RecordingFaultCapturePolicy _capturePolicy = new();
    private readonly ObservedSecretMask _secretMask = new();
    private readonly string _value = $"canary{Guid.NewGuid():N}";

    public FaultIncidentExecutionTests() => _resolver.Respond = (_, _) => RuntimeSecretResolution.Success(_value);

    [Fact]
    public async Task FaultActivity_RecordsBlockingIncident_WithoutThrowingToHost()
    {
        await using var provider = NewProvider(["actexec-fault"]);
        var executable = NewExecutable("Boom!");

        // The agent must accept and drain without fault control flow propagating to the caller.
        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("wfexec-1");
        var faultState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-fault");
        Assert.Equal(ActivityExecutionStatus.Faulted, faultState.Status);
        Assert.Equal("ActivityReturnedFault", faultState.SubStatus);
        Assert.Equal("workflow.fault", faultState.Fault!.Code);

        var incidents = await provider.GetRequiredService<IIncidentStateStore>().ListBlockingAsync("wfexec-1");
        var incident = Assert.Single(incidents);
        Assert.True(incident.IsBlocking);
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal(IncidentSeverity.Error, incident.Severity);
        Assert.Equal("Boom!", incident.Message);
        Assert.Equal("node-fault", incident.ExecutableNodeId);

        // RT-1 acceptance: the blocking incident drives the workflow out of Running to a queryable Faulted status
        // (the fault observer commits a WorkflowFaulted checkpoint post-drain).
        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        Assert.NotNull(workflowState);
        Assert.Equal(WorkflowExecutionStatus.Faulted, workflowState!.Status);
    }

    [Fact]
    public async Task FaultActivity_UsesDefaultMessage_WhenNoMessageIsBound()
    {
        await using var provider = NewProvider(["actexec-fault"]);
        var executable = NewExecutable(message: null);

        await ExecuteAsync(provider, executable);

        var incidents = await provider.GetRequiredService<IIncidentStateStore>().ListBlockingAsync("wfexec-1");
        var incident = Assert.Single(incidents);
        Assert.Equal("The workflow faulted.", incident.Message);
    }

    /// <summary>
    /// Establishes <b>which</b> collaborator decides that an activity fault terminates the workflow: the authored
    /// <c>IIncidentStrategy</c>, applied by <c>IncidentStrategyResolutionDrainObserver</c> at quiescence, not
    /// <c>BlockingIncidentWorkflowFaultObserver</c>. The recorded outcome carries the strategy reference and the
    /// <c>FaultWorkflow</c> action kind; the fault observer never writes a resolution outcome. See
    /// <c>docs/runtime-fault-behavior.md</c>.
    /// </summary>
    [Fact]
    public async Task FaultActivity_WorkflowFault_IsAuthoredByTheIncidentStrategy()
    {
        await using var provider = NewProvider(["actexec-fault"]);

        await ExecuteAsync(provider, NewExecutable("Boom!"));

        var incident = Assert.Single(await provider.GetRequiredService<IIncidentStateStore>().ListAsync("wfexec-1"));
        var outcome = Assert.IsType<IncidentResolutionOutcome>(incident.ResolutionOutcome);
        Assert.Equal(IncidentResolutionActionKinds.FaultWorkflow, outcome.ActionKind);
        Assert.Equal(IncidentStrategyBuiltIns.FaultReference, outcome.Strategy);
        Assert.Null(outcome.SystemSource);
    }

    /// <summary>
    /// The counterpart to <see cref="FaultActivity_WorkflowFault_IsAuthoredByTheIncidentStrategy"/>: authoring
    /// <c>ContinueWithIncidents</c> on the same fault leaves the workflow Running. A blocking incident is therefore
    /// not by itself a workflow fault, which is the confusion <c>docs/runtime-fault-behavior.md</c> exists to fix.
    /// </summary>
    [Fact]
    public async Task FaultActivity_WithContinueWithIncidentsStrategy_LeavesTheWorkflowRunning()
    {
        await using var provider = NewProvider(["actexec-fault"]);

        await ExecuteAsync(provider, NewExecutable("Boom!", IncidentStrategyBuiltIns.ContinueWithIncidentsReference));

        var incident = Assert.Single(await provider.GetRequiredService<IIncidentStateStore>().ListAsync("wfexec-1"));
        Assert.Equal(IncidentStatus.Open, incident.Status);
        Assert.Equal(IncidentResolutionActionKinds.ContinueWithIncidents, incident.ResolutionOutcome!.ActionKind);

        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        Assert.Equal(WorkflowExecutionStatus.Running, workflowState!.Status);
    }

    [Fact]
    public async Task An_exception_carrying_a_resolved_value_is_recorded_masked_on_the_invoke_path()
    {
        await using var harness = NewMaskingHarness(new SecretFailurePlan(), [SecretExecutionId]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(SecretNodeId, typeof(SecretFailingActivity))));

        await AssertThrownFaultMaskedAsync(harness, run.State(SecretNodeId), "ActivityFaulted");
    }

    [Fact]
    public async Task A_fault_returned_with_a_resolved_value_is_recorded_masked_on_the_invoke_path()
    {
        await using var harness = NewMaskingHarness(new SecretFailurePlan { ReturnFault = true }, [SecretExecutionId]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(SecretNodeId, typeof(SecretFailingActivity))));

        await AssertReturnedFaultMaskedAsync(harness, run.State(SecretNodeId));
    }

    [Fact]
    public async Task An_exception_carrying_a_resolved_value_is_recorded_masked_on_the_resume_path()
    {
        await using var harness = NewMaskingHarness(new SecretFailurePlan { FailOnResume = true }, [SecretExecutionId]);

        var state = await RunAndResumeAsync(harness);

        await AssertThrownFaultMaskedAsync(harness, state, "ActivityResumeFaulted");
    }

    [Fact]
    public async Task A_fault_returned_with_a_resolved_value_is_recorded_masked_on_the_resume_path()
    {
        await using var harness = NewMaskingHarness(new SecretFailurePlan { FailOnResume = true, ReturnFault = true }, [SecretExecutionId]);

        var state = await RunAndResumeAsync(harness);

        await AssertReturnedFaultMaskedAsync(harness, state);
    }

    [Fact]
    public async Task An_exception_carrying_a_resolved_value_is_recorded_masked_on_the_parent_completion_path()
    {
        await using var harness = NewMaskingHarness(new SecretFailurePlan(), [ParentExecutionId, "actexec-leaf"]);

        var run = await harness.RunAsync(StructuralExecutionTestSupport.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(ParentNodeId, typeof(SecretFailingParentActivity), WorkflowExecutionHarness.NewProbeNode("node-leaf"))));

        await AssertThrownFaultMaskedAsync(harness, run.State(ParentNodeId), "ParentCompletionFaulted");
    }

    [Fact]
    public async Task A_fault_returned_with_a_resolved_value_is_recorded_masked_on_the_parent_completion_path()
    {
        await using var harness = NewMaskingHarness(new SecretFailurePlan { ReturnFault = true }, [ParentExecutionId, "actexec-leaf"]);

        var run = await harness.RunAsync(StructuralExecutionTestSupport.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(ParentNodeId, typeof(SecretFailingParentActivity), WorkflowExecutionHarness.NewProbeNode("node-leaf"))));

        await AssertReturnedFaultMaskedAsync(harness, run.State(ParentNodeId));
    }

    [Fact]
    public async Task An_exception_carrying_a_resolved_value_is_recorded_masked_on_the_parent_notification_path()
    {
        await using var harness = NewMaskingHarness(new SecretFailurePlan(), [ParentExecutionId, "actexec-child", "actexec-leaf"], NotifyOnce);

        var run = await harness.RunAsync(NewNotifyingExecutable());

        await AssertThrownFaultMaskedAsync(harness, run.State(ParentNodeId), "ParentNotificationFaulted");
    }

    [Fact]
    public async Task A_fault_returned_with_a_resolved_value_is_recorded_masked_on_the_parent_notification_path()
    {
        await using var harness = NewMaskingHarness(new SecretFailurePlan { ReturnFault = true }, [ParentExecutionId, "actexec-child", "actexec-leaf"], NotifyOnce);

        var run = await harness.RunAsync(NewNotifyingExecutable());

        await AssertReturnedFaultMaskedAsync(harness, run.State(ParentNodeId));
    }

    /// <summary>
    /// T094 (research R9): masking replaces text, never classification. The first input resolves, so its value is
    /// registered and the second input's failure is masked; it still records as retryable, with its code and its type.
    /// </summary>
    [Fact]
    public async Task A_masked_store_outage_stays_retryable_with_its_code_and_original_type_name()
    {
        _resolver.Respond = (request, _) => request.Reference.Name == SecretResolutionTestSupport.ReferenceName
            ? RuntimeSecretResolution.Success(_value)
            : RuntimeSecretResolution.Failure("StoreUnavailable", isRetryable: true);
        await using var harness = NewMaskingHarness(new SecretFailurePlan(), [SecretExecutionId]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            TwoSecretInputActivity.NewNode(SecretNodeId, SecretResolutionTestSupport.ReferenceName, "payments.webhook-key")));

        var state = run.State(SecretNodeId);
        Assert.Equal(["payments.api-key", "payments.webhook-key"], _resolver.Requests.Select(request => request.Reference.Name));
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityConstructionFailed", state.SubStatus);
        Assert.True(state.Fault!.IsRetryable);
        Assert.Equal("StoreUnavailable", state.Fault.Code);
        Assert.Equal(typeof(RuntimeSecretResolutionException).FullName, state.Fault.ExceptionType);
        Assert.Equal("Secret 'payments.webhook-key' could not be resolved (StoreUnavailable).", state.Fault.Message);
        AssertRegisteredAndReleased(state);
        await AssertValueAbsentAsync(harness);
    }

    /// <summary>
    /// A missing durable-value storage driver is a deployment problem that parks the activity (constitution §E2.6.1),
    /// classified by its exception type. A registered value must not turn it into a fault by masking it away.
    /// </summary>
    [Fact]
    public async Task A_missing_storage_driver_still_parks_the_activity_when_a_resolved_value_is_registered()
    {
        var plan = new SecretFailurePlan { Exception = _ => new RuntimeDurableValueStorageDriverNotFoundException("missing-driver") };
        await using var harness = NewMaskingHarness(plan, [SecretExecutionId]);

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(SecretNodeId, typeof(SecretFailingActivity))));

        var state = run.State(SecretNodeId);
        Assert.Equal(ActivityExecutionStatus.Waiting, state.Status);
        Assert.Equal(ActivityActivationFailureHandler.IncidentFailureType, state.SubStatus);
        var incident = await SingleIncidentAsync(harness, state);
        Assert.Equal(ActivityActivationFailureHandler.IncidentFailureType, incident.FailureType);
        Assert.Equal("missing-driver", incident.Metadata[ActivityActivationFailureHandler.StorageDriverKeyMetadataKey]);
    }

    /// <summary>
    /// Runtime spans carry identifiers, kinds and the exception type only (contract: withheld values and masking); a test
    /// pins that no fault text, masked or not, reaches a span.
    /// </summary>
    [Fact]
    public async Task Runtime_spans_of_a_masked_fault_carry_no_fault_text()
    {
        using var source = new ActivitySource(WorkflowEngineTelemetry.ActivitySourceName);
        var spans = new List<Span>();
        using var listener = new ActivityListener
        {
            // ActivityListener is process-global; ignore same-name sources owned by parallel tests.
            ShouldListenTo = candidate => ReferenceEquals(candidate, source),
            Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span =>
            {
                lock (spans)
                    spans.Add(span);
            }
        };
        ActivitySource.AddActivityListener(listener);
        await using var harness = NewMaskingHarness(new SecretFailurePlan(), [SecretExecutionId],
            services => services.Replace(ServiceDescriptor.Singleton<IWorkflowEngineTracer>(new ActivitySourceWorkflowEngineTracer(source))));

        var run = await harness.RunAsync(WorkflowExecutionHarness.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(SecretNodeId, typeof(SecretFailingActivity))));

        Assert.Equal(ActivityExecutionStatus.Faulted, run.State(SecretNodeId).Status);
        Span[] recorded;
        lock (spans)
            recorded = [.. spans];
        // Precondition: the faulting activity's execution was traced.
        Assert.Contains(recorded, span => span.OperationName == WorkflowEngineTelemetry.ActivityExecuteSpanName);
        Assert.All(recorded.Select(Render), text =>
        {
            Assert.DoesNotContain(_value, text, StringComparison.Ordinal);
            Assert.DoesNotContain("refused", text, StringComparison.Ordinal);
            Assert.DoesNotContain(Marker, text, StringComparison.Ordinal);
        });
    }

    private WorkflowExecutionHarness NewMaskingHarness(
        SecretFailurePlan plan,
        IReadOnlyCollection<string> activityExecutionIds,
        Action<IServiceCollection>? configure = null) =>
        SecretResolutionTestSupport.NewHarness(_resolver, new SecretValueRecorder(), activityExecutionIds, services =>
        {
            services.AddSingleton(plan);
            services.Replace(ServiceDescriptor.Singleton<IRuntimeFaultCapturePolicy>(_capturePolicy));
            // One mask for every scope, so the test can see what each work handler left registered.
            services.Replace(ServiceDescriptor.Singleton<IRuntimeSecretMask>(_secretMask));
            configure?.Invoke(services);
        });

    private static void NotifyOnce(IServiceCollection services) =>
        services.AddSingleton(new ParentNotificationDirective { InvokeCodes = ["escalate"] });

    private static WorkflowExecutable NewNotifyingExecutable() =>
        StructuralExecutionTestSupport.NewExecutable(
            SecretResolutionTestSupport.NewSecretNode(ParentNodeId, typeof(SecretFailingParentActivity),
                StructuralExecutionTestSupport.NewStructuralNode("node-child", typeof(NotifyingStructuralChildActivity),
                    WorkflowExecutionHarness.NewProbeNode("node-leaf"))));

    /// <summary>Runs a <see cref="SecretFailingActivity"/> that suspends, then resumes it, and returns its state.</summary>
    private static async Task<ActivityExecutionState> RunAndResumeAsync(WorkflowExecutionHarness harness)
    {
        var suspended = (await harness.RunAsync(SecretResolutionTestSupport.NewWaitingExecutable(SecretNodeId, typeof(SecretFailingActivity)))).State(SecretNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, suspended.Status);

        var resumed = await harness.ResumeAsync(
            WorkflowExecutionHarness.Identity,
            Assert.Single(suspended.BookmarkIds),
            suspended.InvocationId,
            SecretNodeId,
            SecretResolutionTestSupport.WaitResumeTargetId(SecretNodeId),
            SecretWaitingActivity.StimulusType,
            SecretWaitingActivity.StimulusHash,
            JsonSerializer.SerializeToElement(new WaitTrigger(true)));
        return resumed.State(SecretNodeId);
    }

    /// <summary>
    /// Asserts the exception <see cref="SecretFailurePlan"/> throws by default was recorded with its value masked in the
    /// fault, the incident and its inner exception's metadata, under the original type names.
    /// </summary>
    private async Task AssertThrownFaultMaskedAsync(WorkflowExecutionHarness harness, ActivityExecutionState state, string subStatus)
    {
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal(subStatus, state.SubStatus);
        Assert.Equal($"refused {Marker}", state.Fault!.Message);
        Assert.Equal(typeof(InvalidOperationException).FullName, state.Fault.ExceptionType);
        var incident = await SingleIncidentAsync(harness, state);
        Assert.Equal($"refused {Marker}", incident.Message);
        Assert.Equal($"refused {Marker}", incident.Metadata[RuntimeMetadataKeys.FaultMessage]);
        Assert.Equal(typeof(InvalidOperationException).FullName, incident.Metadata[RuntimeMetadataKeys.FaultType]);
        Assert.Equal($"inner {Marker}", incident.Metadata[RuntimeMetadataKeys.FaultInnerMessage]);
        Assert.Equal(typeof(FormatException).FullName, incident.Metadata[RuntimeMetadataKeys.FaultInnerType]);
        AssertRegisteredAndReleased(state);
        await AssertValueAbsentAsync(harness);
        AssertLoggedMasked();
    }

    /// <summary>Asserts the fault <see cref="SecretFailurePlan"/> returns by default was recorded with its value masked.</summary>
    private async Task AssertReturnedFaultMaskedAsync(WorkflowExecutionHarness harness, ActivityExecutionState state)
    {
        Assert.Equal(ActivityExecutionStatus.Faulted, state.Status);
        Assert.Equal("ActivityReturnedFault", state.SubStatus);
        Assert.Equal("secret.echoed", state.Fault!.Code);
        Assert.Equal($"returned {Marker}", state.Fault.Message);
        Assert.Equal(typeof(ActivityFault).FullName, state.Fault.ExceptionType);
        var incident = await SingleIncidentAsync(harness, state);
        Assert.Equal($"returned {Marker}", incident.Message);
        AssertRegisteredAndReleased(state);
        await AssertValueAbsentAsync(harness);
        AssertLoggedMasked();
    }

    /// <summary>
    /// Asserts activation registered a value for <paramref name="state"/>'s execution and its work handler released it once
    /// the outcome was recorded, so the mask held it no longer than that.
    /// </summary>
    private void AssertRegisteredAndReleased(ActivityExecutionState state)
    {
        Assert.True(_secretMask.WasRegistered(state.Execution.ActivityExecutionId));
        Assert.False(_secretMask.HasRegistrations(state.Execution.ActivityExecutionId));
    }

    private static async Task<IncidentState> SingleIncidentAsync(WorkflowExecutionHarness harness, ActivityExecutionState state) =>
        Assert.Single(
            await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(harness.ExecutionId),
            incident => incident.ActivityExecutionId == state.Execution.ActivityExecutionId);

    /// <summary>Asserts the value is in no persisted activity state, inspection projection or incident of the run.</summary>
    private async Task AssertValueAbsentAsync(WorkflowExecutionHarness harness)
    {
        await SecretResolutionTestSupport.AssertNotPersistedAsync(harness, [_value]);
        var incidents = JsonSerializer.Serialize(await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(harness.ExecutionId));
        Assert.DoesNotContain(_value, incidents, StringComparison.Ordinal);
    }

    /// <summary>
    /// Renders every exception the fault boundary handed to the fault capture policy through a
    /// <see cref="RecordingLogger"/>, as a runtime log line that includes the exception would, and asserts each line
    /// shows the marker and not the value.
    /// </summary>
    private void AssertLoggedMasked()
    {
        var logger = new RecordingLogger();
        foreach (var exception in _capturePolicy.Captured)
            logger.LogError(exception, "Activity fault recorded: {FaultMessage}", exception.Message);

        var lines = logger.Entries.Select(entry => $"{entry.Message}{Environment.NewLine}{entry.Exception}").ToArray();
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.DoesNotContain(_value, line, StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains(Marker, StringComparison.Ordinal));
    }

    private static string Render(Span span) =>
        string.Join(
            Environment.NewLine,
            [
                span.DisplayName,
                span.StatusDescription ?? string.Empty,
                .. span.TagObjects.Select(tag => $"{tag.Key}={Convert.ToString(tag.Value, CultureInfo.InvariantCulture)}"),
                .. span.Events.SelectMany(spanEvent => spanEvent.Tags.Select(tag => $"{spanEvent.Name}:{tag.Key}={Convert.ToString(tag.Value, CultureInfo.InvariantCulture)}"))
            ]);

    private ServiceProvider NewProvider(IEnumerable<string> activityExecutionIds)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeExecutionIdGenerator>(new DeterministicRuntimeExecutionIdGenerator(activityExecutionIds));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        services.AddSingleton<IActivityActivator, FaultActivityActivator>();

        return services.BuildServiceProvider();
    }

    private async Task ExecuteAsync(ServiceProvider provider, WorkflowExecutable executable)
    {
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await provider.GetRequiredService<IWorkflowExecutionActorProvider>()
            .GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Empty(await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    private WorkflowExecutable NewExecutable(string? message, IncidentStrategyReference? incidentStrategy = null)
    {
        var stringType = new ValueTypeDescriptor("String");
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["message"] = new RuntimeInputBinding(
                inputKey: "message",
                targetType: stringType,
                effectivePolicy: ValueProtectionPolicy.InstanceInline,
                source: RuntimeInputBindingSource.Literal,
                literal: message is null
                    ? ValueEnvelope.Null(stringType, ValueProtectionPolicy.InstanceInline)
                    : ValueEnvelope.Inline(stringType, JsonSerializer.SerializeToElement(message), ValueProtectionPolicy.InstanceInline))
        };
        using var descriptor = JsonDocument.Parse("""{"type":"fault"}""");
        var contract = new ActivityContract(
            typeof(Fault).FullName!,
            "1.0.0",
            "test/fault",
            descriptor.RootElement,
            [new ActivityInputContract("message", nameof(Fault.Message), stringType, false, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement("test/fault", typeof(Fault).FullName!));

        var root = new ExecutableNode(
            executableNodeId: "node-fault",
            authoredActivityId: "authored-fault",
            activityType: typeof(Fault).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/fault",
            descriptorPayload: descriptor.RootElement,
            inputBindings: inputBindings,
            metadata: new Dictionary<string, string>(),
            activityContract: contract);

        return new WorkflowExecutable(
            identity: NewIdentity(),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            compatibilityMetadata: new Dictionary<string, string>(),
            incidentStrategy: incidentStrategy ?? IncidentStrategyBuiltIns.FaultReference);
    }

    private WorkflowExecutionActorActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: _now,
            requestedBy: "fault-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: _now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-start",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:start:artifact-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: 1,
            metadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class FaultActivityActivator : IActivityActivator
    {
        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            var value = request.Inputs.Values["message"];
            var activity = new Fault
            {
                Message = value.Presence == ValuePresence.ExplicitNull
                    ? null
                    : value.InlineValue!.Value.GetString()
            };
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
    }

    private sealed class DeterministicRuntimeExecutionIdGenerator(IEnumerable<string> activityExecutionIds) : IRuntimeExecutionIdGenerator
    {
        private readonly Queue<string> _activityExecutionIds = new(activityExecutionIds);

        public string NewWorkflowExecutionId() => "wfexec-1";
        public string NewWorkflowExecutionCommandId() => "command-generated";
        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-generated";

        public string NewActivityExecutionId() =>
            _activityExecutionIds.TryDequeue(out var activityExecutionId)
                ? activityExecutionId
                : throw new InvalidOperationException("No deterministic activity execution ID is available.");
    }
}
