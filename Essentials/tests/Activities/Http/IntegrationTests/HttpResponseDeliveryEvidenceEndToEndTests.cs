using System.Net;
using Elsa.Activities.Http.Models;
using Elsa.Http.Core;
using Elsa.Activities.Http.Services;
using Elsa.Activities.Scheduling.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Http.IntegrationTests;

public sealed class HttpResponseDeliveryEvidenceEndToEndTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsupported_or_failing_capture_leaves_delivery_unknown_without_breaking_the_response(bool throws)
    {
        await using var fixture = await HttpEndpointHostFixture.StartAsync(services =>
            services.AddScoped<IActivityExecutionInspectionWriter>(sp => new UnavailableObservationWriter(
                sp.GetRequiredService<Elsa.Workflows.Runtime.Services.ActivityExecutions.InMemoryActivityExecutionInspectionStore>(), throws)));
        await fixture.PublishSyncEndpointWithWriteResponseWorkflowAsync("unknown", "evidence/unknown", "GET", "result", 200, "ok", "text/plain");
        var response = await fixture.Client.GetAsync("/workflows/http/evidence/unknown");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        var run = Assert.Single(await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync());
        var states = await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(run.WorkflowExecutionId);
        var write = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-write-response");
        Assert.Equal(ActivityExecutionStatus.Completed, write.Status);
        Assert.Empty(write.IncidentIds);
        Assert.Empty((await fixture.Services.GetRequiredService<IActivityExecutionInspectionStore>()
            .FindAsync(run.WorkflowExecutionId, write.InvocationId))!.Observations);
    }

    [Fact]
    public async Task Async_acceptance_records_a_warning_without_claiming_a_write()
    {
        await using var fixture = await HttpEndpointHostFixture.StartAsync();
        await fixture.PublishAsyncResponseEvidenceWorkflowAsync();
        var response = await fixture.Client.GetAsync("/workflows/http/evidence/async");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = Assert.Single(await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync());
        var states = await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(run.WorkflowExecutionId);
        var write = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-write-response");
        Assert.Equal(ActivityExecutionStatus.Completed, write.Status);
        var facts = (await fixture.Services.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync(run.WorkflowExecutionId, write.InvocationId))!.Observations;
        Assert.Equal("http.response.undelivered", Assert.Single(facts).Code);
        Assert.Contains("background", facts[0].Message);
    }

    [Fact]
    public async Task Multiple_endpoint_origins_remain_unknown_when_no_exact_write_was_recorded()
    {
        await using var fixture = await HttpEndpointHostFixture.StartAsync();
        await fixture.PublishAmbiguousResponseEvidenceWorkflowAsync();
        Assert.Equal(HttpStatusCode.Accepted, (await fixture.Client.GetAsync("/workflows/http/evidence/first")).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await fixture.Client.GetAsync("/workflows/http/evidence/second")).StatusCode);
        var run = Assert.Single(await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync());
        var states = await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(run.WorkflowExecutionId);
        var write = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-write-response");
        Assert.Equal(ActivityExecutionStatus.Completed, write.Status);
        await fixture.Services.GetRequiredService<HttpResponseDeliveryEvidence>().ReconcileAsync(run.WorkflowExecutionId);
        Assert.Empty((await fixture.Services.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync(run.WorkflowExecutionId, write.InvocationId))!.Observations);
    }

    [Fact]
    public async Task Inline_response_records_exact_write_without_fault_or_warning()
    {
        await using var fixture = await HttpEndpointHostFixture.StartAsync();
        await fixture.PublishSyncEndpointWithWriteResponseWorkflowAsync("written", "evidence/written", "GET", "result", 200, "ok", "text/plain");
        var response = await fixture.Client.GetAsync("/workflows/http/evidence/written");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
        var run = Assert.Single(await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync());
        var states = await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(run.WorkflowExecutionId);
        var write = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-write-response");
        var inspection = await fixture.Services.GetRequiredService<IActivityExecutionInspectionStore>().FindAsync(run.WorkflowExecutionId, write.InvocationId);
        Assert.Equal("http.response.written", Assert.Single(inspection!.Observations).Code);
        Assert.Equal(ActivityExecutionStatus.Completed, write.Status);
        Assert.Empty(write.IncidentIds);
    }

    [Fact]
    public async Task Timer_resume_after_the_original_exchange_closed_retains_a_warning_on_the_exact_completed_response()
    {
        // Load the activity assembly before the fixture runs the production type-registration startup task.
        _ = typeof(Elsa.Activities.Scheduling.Activities.Delay).Assembly;
        await using var fixture = await HttpEndpointHostFixture.StartAsync(services => new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services));
        await fixture.PublishDelayResponseEvidenceWorkflowAsync();
        var response = await fixture.Client.GetAsync("/workflows/http/evidence/delay");
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var run = Assert.Single(await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().ListAllAsync());
        // Fire the real durable timer through the production pump, after the originating request finished.
        await Task.Delay(30);
        var pump = Assert.Single(fixture.Services.GetServices<IRecurringTask>().OfType<DurableTimerPumpTask>());
        await pump.ExecuteAsync(CancellationToken.None);
        var states = await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(run.WorkflowExecutionId);
        var write = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-write-response");
        Assert.Equal(ActivityExecutionStatus.Completed, write.Status);
        Assert.Empty(write.IncidentIds);
        var reader = fixture.Services.GetRequiredService<IActivityExecutionInspectionStore>();
        var inspection = await reader.FindAsync(run.WorkflowExecutionId, write.InvocationId);
        var warning = Assert.Single(inspection!.Observations);
        Assert.Equal("http.response.undelivered", warning.Code);
        Assert.Equal(IncidentSeverity.Warning, warning.Severity);
        Assert.Contains("original HTTP exchange is closed", warning.Message);
        await fixture.Services.GetRequiredService<HttpResponseDeliveryEvidence>().ReconcileAsync(run.WorkflowExecutionId);
        Assert.Single((await reader.FindAsync(run.WorkflowExecutionId, write.InvocationId))!.Observations);
        var summary = await reader.ListSummariesPageAsync(new(run.WorkflowExecutionId));
        Assert.Single(summary.Items.Single(item => item.ActivityExecutionId == write.InvocationId).Observations);
    }

    private sealed class UnavailableObservationWriter(IActivityExecutionInspectionWriter inner, bool throws) : IActivityExecutionInspectionWriter
    {
        public ValueTask SaveAsync(ActivityExecutionInspectionProjection projection, CancellationToken cancellationToken = default) => inner.SaveAsync(projection, cancellationToken);
        public ValueTask<bool> TryAppendObservationAsync(string workflowExecutionId, string activityExecutionId,
            ActivityExecutionObservation observation, CancellationToken cancellationToken = default) =>
            throws ? throw new IOException("Injected observation persistence failure") : ValueTask.FromResult(false);
    }
}

public sealed partial class HttpEndpointHostFixture
{
    internal Task PublishAmbiguousResponseEvidenceWorkflowAsync()
    {
        var first = NewHttpEndpointNode("evidence-first", "evidence/first", "first-request", ["GET"],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: true, isTriggerNode: true,
            nodeId: "node-first-endpoint", responseMode: ResponseMode.Async);
        var second = NewHttpEndpointNode("evidence-second", "evidence/second", "second-request", ["GET"],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: false, isTriggerNode: false,
            nodeId: "node-second-endpoint", responseMode: ResponseMode.Async);
        return PublishTriggerSequenceAsync("evidence-ambiguous", first, second,
            NewWriteHttpResponseNode("node-write-response", 200, "late", "text/plain", null), resumeTargetNodeId: second.ExecutableNodeId);
    }

    internal Task PublishAsyncResponseEvidenceWorkflowAsync()
    {
        var endpoint = NewHttpEndpointNode("evidence-async-endpoint", "evidence/async", "request", ["GET"],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: true, isTriggerNode: true,
            nodeId: "node-async-endpoint", responseMode: ResponseMode.Async);
        return PublishTriggerSequenceAsync("evidence-async", endpoint, NewWriteHttpResponseNode("node-write-response", 200, "late", "text/plain", null));
    }

    internal Task PublishDelayResponseEvidenceWorkflowAsync()
    {
        var delayTemplate = DelayTestExecutable.Create(TimeSpan.FromMilliseconds(5), DateTimeOffset.UtcNow);
        var endpoint = NewHttpEndpointNode("evidence-delay-endpoint", "evidence/delay", "request", ["GET"],
            authorize: null, policy: null, requestSizeLimit: null, canStartWorkflow: true, isTriggerNode: true,
            nodeId: "node-sync-endpoint", responseMode: ResponseMode.Sync);
        var response = NewWriteHttpResponseNode("node-write-response", 200, "late", "text/plain", null);
        return PublishAsync(new WorkflowExecutable(NewIdentity("evidence-delay"),
            NewSequenceNode("node-sequence", new[] { endpoint,
                new ExecutableNode(delayTemplate.RootActivity.ExecutableNodeId, delayTemplate.RootActivity.AuthoredActivityId,
                    delayTemplate.RootActivity.ActivityType, delayTemplate.RootActivity.ActivityTypeVersion,
                    ClrRuntimeDescriptor(delayTemplate.RootActivity.DescriptorPayload), delayTemplate.RootActivity.InputBindings,
                    delayTemplate.RootActivity.Metadata, activityContract: delayTemplate.RootActivity.ActivityContract), response }),
            delayTemplate.ResumeTargets, DateTimeOffset.UtcNow, new Dictionary<string, string>(),
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference));
    }
}
