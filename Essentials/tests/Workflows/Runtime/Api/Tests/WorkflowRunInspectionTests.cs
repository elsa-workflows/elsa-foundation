using System.Text.Json;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

// Spec 1193 slice G (D4, FR-029): the exact Run read for other domains through Workflows Runtime Core, over the services the
// run endpoints use, with their per-run authority and execution-evidence redaction, and unavailable data by reason.
public sealed class WorkflowRunInspectionTests
{
    private const string RunId = "wfexec-1";
    private const string ActivityId = "actexec-1";
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ReadAt = Now.AddMinutes(5);
    private static readonly string Sentinel = $"plain{Guid.NewGuid():N}";
    private readonly InMemoryWorkflowExecutionStateStore _workflows = new();
    private readonly InMemoryActivityExecutionInspectionStore _inspections = new();
    private readonly AuditSink _audit = new();

    [Fact]
    public async Task Reads_the_instance_incidents_and_activity_page_the_run_endpoint_returns()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        var run = await Create(details).GetAsync(new(RunId, ActivityPageSize: 500));

        Assert.NotNull(run);
        // The page size is clamped to the read's bound before the endpoint's service is asked.
        Assert.Equal(new GetWorkflowInstance(RunId, WorkflowRunInspectionRequest.MaxActivityPageSize, null), details.Requests.Single());
        Assert.Equal((RunId, "artifact-1", "sha256:test", "Faulted", true), (run.Instance.WorkflowExecutionId, run.Instance.ArtifactId, run.Instance.ArtifactHash,
            run.Instance.Status, run.Instance.IsTerminal));
        var incident = Assert.Single(run.Incidents);
        Assert.Equal(("incident-1", ActivityId, "The workflow faulted.", false, true), (incident.IncidentId, incident.ActivityExecutionId, incident.Message,
            incident.MessageRedacted, incident.IsBlocking));
        Assert.Equal([ActivityId], run.Activities.Items.Select(item => item.ActivityExecutionId));
        Assert.Equal((1L, "next-page"), (run.Activities.TotalCount, run.Activities.NextContinuationToken));
        Assert.Equal(("Durable", "activity-level", true, ReadAt), (run.PersistenceMode, run.InspectionGranularity, run.CanInspectValues, run.ReadAt));
        Assert.Null(run.Selection);
    }

    [Fact]
    public async Task A_run_still_executing_reads_as_not_terminal_at_its_read_time()
    {
        var run = await Create(await SeedAsync(WorkflowExecutionStatus.Running)).GetAsync(new(RunId));
        Assert.Equal(("Running", false, ReadAt), (run!.Instance.Status, run.Instance.IsTerminal, run.ReadAt));
    }

    [Fact]
    public async Task An_unknown_or_hidden_run_reads_as_absent()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        Assert.Null(await Create(details).GetAsync(new("other-run")));
        Assert.Null(await Create(details).GetAsync(new(" ")));
        Assert.Null(await Create(new Details(null)).GetAsync(new(RunId)));
    }

    [Fact]
    public async Task Withheld_and_sensitive_values_stay_withheld_and_a_captured_value_is_released_only_by_the_audited_payload_read()
    {
        var details = await SeedValuesAsync(WorkflowExecutionStatus.Faulted,
            Withheld("token"), Sensitive("password"), Captured("text", "hello"), Captured("pin", Sentinel, sensitive: true));
        var inspection = Create(details);

        var described = (await inspection.GetAsync(new(RunId, ActivityId)))!.Selection!.Execution!;
        Assert.Empty(_audit.Records);
        Assert.Equal(WorkflowRunValueDisclosure.Resolvable, Value(described, "text").Disclosure);

        var read = (await inspection.GetAsync(new(RunId, ActivityId, ResolveSelectedValues: true)))!.Selection!.Execution!;
        var token = Value(read, "token");
        Assert.Equal((WorkflowRunValueDisclosure.Withheld, "SecretReference", "payments.api-key", true, (JsonElement?)null),
            (token.Disclosure, token.WithheldKind, token.SecretReferenceName, token.IsSensitive, token.Value));
        var password = Value(read, "password");
        Assert.Equal((WorkflowRunValueDisclosure.Withheld, (WorkflowRunUnavailableReason?)null, (JsonElement?)null), (password.Disclosure, password.UnavailableReason, password.Value));
        // A sensitive value a capture policy did capture is still never released by this read, nor resolved.
        var pin = Value(read, "pin");
        Assert.Equal((WorkflowRunValueDisclosure.Withheld, true, (JsonElement?)null), (pin.Disclosure, pin.IsSensitive, pin.Value));
        var text = Value(read, "text");
        Assert.Equal((WorkflowRunValueDisclosure.Resolved, "hello"), (text.Disclosure, text.Value!.Value.GetString()));
        // Only the resolvable value was read through the payload boundary, and that read was audited.
        Assert.Equal([("resolved", text.EvidenceId)], _audit.Records.Select(record => (record.Outcome, record.EvidenceId)));
        Assert.DoesNotContain(Sentinel, JsonSerializer.Serialize(read), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, true, "redacted")]
    [InlineData(true, false, "resolutionPermissionRequired")]
    public async Task A_value_the_caller_may_not_inspect_or_resolve_is_not_permitted(bool canInspect, bool canResolve, string accessState)
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted, canInspect: canInspect, canResolve: canResolve, snapshots: [Captured("text", "hello")]);
        var text = Value((await Create(details).GetAsync(new(RunId, ActivityId, ResolveSelectedValues: true)))!.Selection!.Execution!, "text");
        Assert.Equal((WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotPermitted, accessState, (JsonElement?)null),
            (text.Disclosure, text.UnavailableReason, text.AccessState, text.Value));
    }

    [Fact]
    public async Task Without_value_inspection_incident_messages_are_the_endpoints_redaction()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted, canInspect: false);
        var run = (await Create(details).GetAsync(new(RunId)))!;
        Assert.False(run.CanInspectValues);
        var incident = Assert.Single(run.Incidents);
        Assert.Equal(("Incident details are redacted.", true), (incident.Message, incident.MessageRedacted));
    }

    [Fact]
    public async Task A_value_the_runtime_did_not_capture_or_a_backend_without_payload_reads_is_reported_by_reason()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted, snapshots: [NotCaptured("skipped"), Captured("text", "hello")]);
        var read = (await Create(details, payloads: false).GetAsync(new(RunId, ActivityId, ResolveSelectedValues: true)))!.Selection!.Execution!;
        Assert.Equal((WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.NotCaptured), (Value(read, "skipped").Disclosure, Value(read, "skipped").UnavailableReason));
        Assert.Equal((WorkflowRunValueDisclosure.Unavailable, WorkflowRunUnavailableReason.Unsupported), (Value(read, "text").Disclosure, Value(read, "text").UnavailableReason));
    }

    [Fact]
    public async Task A_selection_is_unsupported_without_activity_inspection_pruned_when_recorded_but_gone_and_absent_when_unknown()
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        Assert.Equal(new WorkflowRunInspectionSelection(ActivityId, null, WorkflowRunUnavailableReason.Unsupported),
            (await Create(details, activities: false).GetAsync(new(RunId, ActivityId)))!.Selection);

        // The Run's page and incident still name the activity execution after its inspection record was pruned.
        Assert.Equal(new WorkflowRunInspectionSelection(ActivityId, null, WorkflowRunUnavailableReason.Pruned),
            (await Create(details, pruned: true).GetAsync(new(RunId, ActivityId)))!.Selection);
        Assert.Equal(new WorkflowRunInspectionSelection("never-recorded", null, null), (await Create(details).GetAsync(new(RunId, "never-recorded")))!.Selection);
    }

    [Theory]
    [InlineData("none", false, WorkflowRunUnavailableReason.Unsupported)]
    [InlineData("missing", false, WorkflowRunUnavailableReason.NotCaptured)]
    [InlineData(ActivityStateView.StateCleared, false, WorkflowRunUnavailableReason.NotCaptured)]
    [InlineData(ActivityStateView.NoProjector, false, WorkflowRunUnavailableReason.Unsupported)]
    [InlineData("available", true, null)]
    public async Task The_state_view_is_the_endpoints_projection_or_unavailable_by_reason(string state, bool available, WorkflowRunUnavailableReason? reason)
    {
        var details = await SeedAsync(WorkflowExecutionStatus.Faulted);
        var view = state switch
        {
            "missing" => null,
            "available" => new ActivityStateView(RunId, ActivityId, "node-1", "Faulted", true, null, "test.view", 1, true, JsonSerializer.SerializeToElement(new { step = 2 })),
            _ => new ActivityStateView(RunId, ActivityId, "node-1", "Faulted", false, state, null, null, true, null)
        };
        var stateView = (await Create(details, stateViews: new StateViews(view), stateViewsSupported: state != "none").GetAsync(new(RunId, ActivityId)))!.Selection!.Execution!.StateView;
        Assert.Equal((available, reason), (stateView.Available, stateView.UnavailableReason));
        if (available) Assert.Equal(("test.view", 2), (stateView.ViewKind, stateView.View!.Value.GetProperty("step").GetInt32()));
    }

    [Fact]
    public void The_feature_registers_one_internal_sealed_run_inspection()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(IWorkflowRunInspection));
        Assert.Equal("WorkflowRunInspection", descriptor.ImplementationType!.Name);
        Assert.True(descriptor.ImplementationType.IsSealed);
        Assert.False(descriptor.ImplementationType.IsPublic);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void A_second_run_inspection_registration_fails_configuration()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWorkflowRunInspection, Duplicate>();
        services.AddScoped<IWorkflowRunInspection, Duplicate>();
        var error = Assert.Throws<InvalidOperationException>(() => new WorkflowsRuntimeApiFeature().ConfigureServices(services));
        Assert.Contains("IWorkflowRunInspection has multiple registrations", error.Message);
    }

    [Fact]
    public async Task A_second_run_inspection_registration_fails_shell_startup()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        services.AddScoped<IWorkflowRunInspection, Duplicate>();
        var task = services.Where(item => item.ServiceType == typeof(IStartupTask)).Select(item => item.ImplementationInstance)
            .OfType<IStartupTask>().Single(item => item.GetType().Name == "WorkflowRunInspectionStartupTask");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => task.ExecuteAsync(CancellationToken.None));
        Assert.Contains("IWorkflowRunInspection has multiple registrations", error.Message);
    }

    private static WorkflowRunInspectionValue Value(WorkflowRunInspectionActivityExecution execution, string name) => execution.Values.Single(item => item.Name == name);

    private Access _access = new(true, true);

    private IWorkflowRunInspection Create(Details details, bool activities = true, StateViews? stateViews = null, bool payloads = true, bool pruned = false,
        bool stateViewsSupported = true) =>
        (IWorkflowRunInspection)Activator.CreateInstance(RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.Services.WorkflowRunInspection")!,
            details,
            activities ? new ActivityExecutionInspectionService(_workflows, pruned ? new InMemoryActivityExecutionInspectionStore() : _inspections, _access) : null,
            stateViewsSupported
                ? stateViews ?? new StateViews(new ActivityStateView(RunId, ActivityId, "node-1", "Faulted", false, ActivityStateView.StateCleared, null, null, true, null))
                : null,
            payloads ? new ActivityExecutionValuePayloadReader(_workflows, _inspections, _access, _audit, new FakeTimeProvider(Now)) : null,
            new FakeTimeProvider(ReadAt))!;

    private async Task<Details> SeedValuesAsync(WorkflowExecutionStatus status, params ActivityExecutionInspectionValueSnapshot[] values) =>
        await SeedAsync(status, true, true, values);

    private async Task<Details> SeedAsync(WorkflowExecutionStatus status, bool canInspect = true, bool canResolve = true,
        ActivityExecutionInspectionValueSnapshot[]? snapshots = null)
    {
        _access = new(canInspect, canResolve);
        var state = new WorkflowExecutionState(RunId, new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            status, null, Now, Now, Now, null, null, null, null, new Dictionary<string, string>());
        await _workflows.SaveAsync(state);
        await _inspections.SaveAsync(new ActivityExecutionInspectionProjection(ActivityId, RunId, "node-1", "authored-1", "Test.Activity", "1.0.0",
            ActivityExecutionStatus.Faulted, null, 1, Now, Now, null, "checkpoint-1", "checkpoint-1", Now, ActivitySchedulingProvenance.Empty, [], [], [],
            snapshots ?? [], new Dictionary<string, string>()));
        var summary = new ActivityExecutionInspectionSummaryView(ActivityId, RunId, "node-1", "authored-1", "Test.Activity", "1.0.0", "Faulted", null, 1, Now, Now, null,
            "checkpoint-1", "checkpoint-1", Now, ActivitySchedulingProvenanceView.From(ActivitySchedulingProvenance.Empty), [], 0, 1, snapshots?.Length ?? 0,
            new Dictionary<string, string>());
        var incident = new IncidentStateView("incident-1", RunId, ActivityId, "node-1", "Error", "Active", null, "workflow.fault",
            canInspect ? "The workflow faulted." : "Incident details are redacted.", Now, null, true, new Dictionary<string, string>());
        return new(new WorkflowInstanceDetailsView(WorkflowInstanceSummaryView.From(state, 1, 1, canInspect), [summary], [incident],
            new Dictionary<string, WorkflowOutputView>(), "Durable", "activity-level", "next-page", new(false, canInspect)));
    }

    private static ActivityExecutionInspectionValueSnapshot Withheld(string name) =>
        ActivityExecutionInspectionValueSnapshot.FromDecision(name, ActivityExecutionInspectionValueSubject.ActivityInput,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.Payload, "Full payload captured by runtime diagnostics policy."),
            new RuntimeValueTypeDescriptor("alias", "String", null), Now, JsonSerializer.SerializeToElement(Sentinel), true,
            ActivityExecutionInspectionValueSnapshot.MarkWithheld(new Dictionary<string, string>(),
                WithheldValue.SecretReference(new RuntimeSecretReference("payments.api-key"), null)), inputKey: name);

    private static ActivityExecutionInspectionValueSnapshot Sensitive(string name) =>
        ActivityExecutionInspectionValueSnapshot.FromDecision(name, ActivityExecutionInspectionValueSubject.ActivityInput,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Sensitive runtime payloads are excluded by default."),
            new RuntimeValueTypeDescriptor("alias", "String", null), Now, JsonSerializer.SerializeToElement(Sentinel), true, inputKey: name);

    private static ActivityExecutionInspectionValueSnapshot NotCaptured(string name) =>
        ActivityExecutionInspectionValueSnapshot.FromDecision(name, ActivityExecutionInspectionValueSubject.ActivityInput,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Runtime diagnostics policy did not capture value evidence."),
            new RuntimeValueTypeDescriptor("alias", "String", null), Now, null, false, inputKey: name);

    private static ActivityExecutionInspectionValueSnapshot Captured(string name, string value, bool sensitive = false) =>
        ActivityExecutionInspectionValueSnapshot.FromDecision(name, ActivityExecutionInspectionValueSubject.ActivityInput,
            new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.DiagnosticSnapshot, "Diagnostic snapshot captured by runtime diagnostics policy."),
            new RuntimeValueTypeDescriptor("alias", "String", null), Now, JsonSerializer.SerializeToElement(value), sensitive, inputKey: name);

    private sealed class Details(WorkflowInstanceDetailsView? view) : IWorkflowInstanceDetailsService
    {
        public List<GetWorkflowInstance> Requests { get; } = [];

        public Task<WorkflowInstanceDetailsView?> GetAsync(GetWorkflowInstance request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(view?.Instance.WorkflowExecutionId == request.WorkflowExecutionId ? view : null);
        }
    }

    private sealed class StateViews(ActivityStateView? view) : IActivityExecutionStateViewService
    {
        public Task<ActivityStateView?> GetAsync(GetActivityExecutionStateView request, CancellationToken cancellationToken) => Task.FromResult(view);
    }

    private sealed class Access(bool canInspectSensitiveValues, bool canResolve) : IActivityInspectionContextAsync
    {
        public string TenantScope => "tenant-a";
        public string AuditSubject => "subject-1";
        public string RequestCorrelationId => "request-1";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("structure");
        public ValueTask<bool> CanInspectStructureAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanInspectSensitiveValuesAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canInspectSensitiveValues);
        public ValueTask<bool> CanResolveSensitiveValuePayloadsAsync(WorkflowExecutionState workflowExecution, CancellationToken cancellationToken = default) => ValueTask.FromResult(canResolve);
    }

    private sealed class AuditSink : IActivityExecutionValuePayloadAuditSink
    {
        public List<ActivityExecutionValuePayloadAuditRecord> Records { get; } = [];

        public ValueTask RecordAsync(ActivityExecutionValuePayloadAuditRecord record, CancellationToken cancellationToken = default)
        {
            Records.Add(record);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class Duplicate : IWorkflowRunInspection
    {
        public ValueTask<WorkflowRunInspectionDetails?> GetAsync(WorkflowRunInspectionRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<WorkflowRunInspectionDetails?>(null);
    }
}
