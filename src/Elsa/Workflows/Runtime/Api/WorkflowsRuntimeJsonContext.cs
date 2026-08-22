using Elsa.Workflows.Runtime.Api.Commands;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Models.Alterations;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Api.Requests.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = false, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(GetWorkflowInstance))]
[JsonSerializable(typeof(ListWorkflowInstances))]
[JsonSerializable(typeof(ListWorkflowExecutables))]
[JsonSerializable(typeof(GetWorkflowExecutable))]
[JsonSerializable(typeof(GetWorkflowExecutableInputSources))]
[JsonSerializable(typeof(GetWorkflowExecutableProvenance))]
[JsonSerializable(typeof(WorkflowActivationSlotListView))]
[JsonSerializable(typeof(WorkflowActivationSlotView))]
[JsonSerializable(typeof(ExecuteWorkflow))]
[JsonSerializable(typeof(DispatchStimulus))]
[JsonSerializable(typeof(ListWorkflowDispatches))]
[JsonSerializable(typeof(GetWorkflowDispatch))]
[JsonSerializable(typeof(RedriveWorkflowDispatch))]
[JsonSerializable(typeof(GetActivityExecution))]
[JsonSerializable(typeof(GetActivityExecutionDescendants))]
[JsonSerializable(typeof(GetActivityExecutionLayout))]
[JsonSerializable(typeof(GetActivityExecutionValuePayload))]
[JsonSerializable(typeof(ListIncidents))]
[JsonSerializable(typeof(GetRuntimeDiagnosticsSettings))]
[JsonSerializable(typeof(SaveRuntimeDiagnosticsSettings))]
[JsonSerializable(typeof(SubmitWorkflowAlterationPlan))]
[JsonSerializable(typeof(GetWorkflowAlterationPlan))]
[JsonSerializable(typeof(PageWorkflowAlterationJobs))]
[JsonSerializable(typeof(GetWorkflowAlterationJob))]
[JsonSerializable(typeof(CancelWorkflowAlterationPlan))]
[JsonSerializable(typeof(WorkflowExecutionStartDispatchView))]
[JsonSerializable(typeof(DispatchStimulusResponse))]
[JsonSerializable(typeof(IReadOnlyCollection<WorkflowDispatchView>))]
[JsonSerializable(typeof(WorkflowDispatchView))]
[JsonSerializable(typeof(WorkflowDispatchRedriveView))]
[JsonSerializable(typeof(GetWorkflowInstanceResponse))]
[JsonSerializable(typeof(GetWorkflowDispatchResponse))]
[JsonSerializable(typeof(WorkflowInstanceListView))]
[JsonSerializable(typeof(IReadOnlyCollection<WorkflowInstanceSummaryView>))]
[JsonSerializable(typeof(WorkflowExecutablesListView))]
[JsonSerializable(typeof(WorkflowExecutableDetailsView))]
[JsonSerializable(typeof(WorkflowExecutableInputSourcesView))]
[JsonSerializable(typeof(ExecutableProvenanceView))]
[JsonSerializable(typeof(GetActivityExecutionResponse))]
[JsonSerializable(typeof(ActivityExecutionInspectionView))]
[JsonSerializable(typeof(GetActivityExecutionDescendantsResponse))]
[JsonSerializable(typeof(ActivityExecutionHierarchyPageView))]
[JsonSerializable(typeof(GetActivityExecutionLayoutResponse))]
[JsonSerializable(typeof(ActivityExecutionLayoutView))]
[JsonSerializable(typeof(GetActivityExecutionValuePayloadResponse))]
[JsonSerializable(typeof(ActivityExecutionValuePayloadReadResult), TypeInfoPropertyName = "ValuePayloadReadResult")]
[JsonSerializable(typeof(ActivityExecutionValuePayloadView))]
[JsonSerializable(typeof(ListIncidentsResponse))]
[JsonSerializable(typeof(RuntimeDiagnosticsSettingsView))]
[JsonSerializable(typeof(WorkflowAlterationPlanSubmissionView))]
[JsonSerializable(typeof(WorkflowAlterationPlanView))]
[JsonSerializable(typeof(WorkflowAlterationJobPageView))]
[JsonSerializable(typeof(WorkflowAlterationJobView))]
[JsonSerializable(typeof(WorkflowAlterationPlanCancellationView))]
[JsonSerializable(typeof(WorkflowAlterationProblemView))]
[JsonSerializable(typeof(RuntimeProblemDetails))]
[JsonSerializable(typeof(RuntimeValidationProblemDetails))]
[JsonSerializable(typeof(ActivityExecutionProblemDetailsView))]
[JsonSerializable(typeof(ActivityExecutionCursorProblemView))]
[JsonSerializable(typeof(ActivityExecutionProblemDiagnosticView))]
internal partial class WorkflowsRuntimeJsonContext : JsonSerializerContext;

internal sealed record RuntimeProblemDetails(
    string Type,
    string Title,
    int Status,
    string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instance,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Code = null);

internal sealed record RuntimeValidationProblemDetails(
    IReadOnlyDictionary<string, string[]> Errors,
    string Message,
    int StatusCode);
