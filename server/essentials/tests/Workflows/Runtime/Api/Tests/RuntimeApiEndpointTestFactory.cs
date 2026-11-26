using Elsa.Workflows.Runtime.Api.Authorization;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Commands;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Tests;

internal static class RuntimeApiEndpointTestFactory
{
    public static Type? FindType(string fullName) => typeof(WorkflowsRuntimeApiFeature).Assembly.GetType(fullName);

    private static readonly IReadOnlyDictionary<(string Method, string Route), RuntimeEndpointDescriptor> Endpoints =
        new Dictionary<(string Method, string Route), RuntimeEndpointDescriptor>
        {
            [("GET", "runtime/workflows/instances/{workflowExecutionId}")] = Endpoint<GetWorkflowInstance, WorkflowInstanceDetailsView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/instances")] = Endpoint<ListWorkflowInstances, IReadOnlyCollection<WorkflowInstanceSummaryView>>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/instances/page")] = Endpoint<ListWorkflowInstances, WorkflowInstanceListView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/executables")] = Endpoint<ListWorkflowExecutables, WorkflowExecutablesListView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/executables/{artifactId}")] = Endpoint<GetWorkflowExecutable, WorkflowExecutableDetailsView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/executables/{artifactId}/source-references/{sourceReferenceId}/input-sources")] = Endpoint<GetWorkflowExecutableInputSources, WorkflowExecutableInputSourcesView>(WorkflowRuntimePermissions.WorkflowPublishingRead),
            [("GET", "runtime/workflows/executables/{artifactId}/provenance")] = Endpoint<GetWorkflowExecutableProvenance, ExecutableProvenanceView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/activation-slots/{definitionId}")] = Endpoint<ListWorkflowActivationSlots, WorkflowActivationSlotListView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/activation-slots/{definitionId}/{slotName}")] = Endpoint<GetWorkflowActivationSlot, WorkflowActivationSlotView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("POST", "runtime/workflows/executables/{artifactId}/execute")] = Endpoint<ExecuteWorkflow, WorkflowExecutionStartDispatchView>(WorkflowRuntimePermissions.WorkflowRuntimeExecute),
            [("POST", "runtime/workflows/stimuli")] = Endpoint<DispatchStimulus, DispatchStimulusResponse>(WorkflowRuntimePermissions.WorkflowRuntimeExecute),
            [("GET", "runtime/workflows/dispatches")] = Endpoint<ListWorkflowDispatches, IReadOnlyCollection<WorkflowDispatchView>>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/dispatches/{dispatchId}")] = Endpoint<GetWorkflowDispatch, WorkflowDispatchView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("POST", "runtime/workflows/dispatches/{dispatchId}/redrive")] = Endpoint<RedriveWorkflowDispatch, WorkflowDispatchRedriveView>(WorkflowRuntimePermissions.WorkflowRuntimeManage),
            [("GET", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}")] = Endpoint<GetActivityExecution, ActivityExecutionInspectionView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/descendants")] = Endpoint<GetActivityExecutionDescendants, ActivityExecutionHierarchyPageView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/layout")] = Endpoint<GetActivityExecutionLayout, ActivityExecutionLayoutView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/value-evidence/{evidenceId}/payload")] = Endpoint<GetActivityExecutionValuePayload, ActivityExecutionValuePayloadReadResult>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/instances/{workflowExecutionId}/incidents")] = Endpoint<ListIncidents, ListIncidentsResponse>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/diagnostics/settings")] = Endpoint<GetRuntimeDiagnosticsSettings, RuntimeDiagnosticsSettingsView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("PUT", "runtime/workflows/diagnostics/settings")] = Endpoint<SaveRuntimeDiagnosticsSettings, RuntimeDiagnosticsSettingsView>(WorkflowRuntimePermissions.WorkflowRuntimeManage),
            [("POST", "runtime/workflows/alteration-plans")] = Endpoint<SubmitWorkflowAlterationPlan, WorkflowAlterationPlanSubmissionView>(WorkflowRuntimePermissions.WorkflowRuntimeManage),
            [("GET", "runtime/workflows/alteration-plans/{planId}")] = Endpoint<GetWorkflowAlterationPlan, WorkflowAlterationPlanView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/alteration-plans/{planId}/jobs/page")] = Endpoint<PageWorkflowAlterationJobs, WorkflowAlterationJobPageView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("GET", "runtime/workflows/alteration-plans/{planId}/jobs/{jobId}")] = Endpoint<GetWorkflowAlterationJob, WorkflowAlterationJobView>(WorkflowRuntimePermissions.WorkflowRuntimeRead),
            [("POST", "runtime/workflows/alteration-plans/{planId}/cancel")] = Endpoint<CancelWorkflowAlterationPlan, WorkflowAlterationPlanCancellationView>(WorkflowRuntimePermissions.WorkflowRuntimeManage)
        };

    public static RuntimeEndpointDescriptor FindByRoute(string route, string method = "GET")
    {
        Endpoints.TryGetValue((method, route), out var value);
        value ??= Endpoints.FirstOrDefault(pair => string.Equals(pair.Key.Route, route, StringComparison.Ordinal)).Value;
        var endpoint = Xunit.Assert.IsType<RuntimeEndpointDescriptor>(value);
        return endpoint with { Definition = endpoint.Definition with { Routes = [route] } };
    }

    public static (Type Request, Type Response) Contract(RuntimeEndpointDescriptor endpoint) =>
        (endpoint.Request, endpoint.Response);

    public static void AssertPermissionPolicy(RuntimeEndpointDescriptor endpoint, params string[] permissions) =>
        Xunit.Assert.Equal(string.Join(" ", permissions), endpoint.Permission);

    private static RuntimeEndpointDescriptor Endpoint<TRequest, TResponse>(string permission) =>
        new(typeof(TRequest), typeof(TResponse), permission, new EndpointDefinition([], null));

    public sealed record RuntimeEndpointDescriptor(Type Request, Type Response, string Permission, EndpointDefinition Definition);

    public sealed record EndpointDefinition(IReadOnlyList<string> Routes, IReadOnlyList<string>? AnonymousVerbs);
}
