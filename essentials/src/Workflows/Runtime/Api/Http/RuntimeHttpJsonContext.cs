using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;

namespace Elsa.Workflows.Runtime.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = false, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(ListWorkflowExecutables))]
[JsonSerializable(typeof(WorkflowExecutablesListView))]
[JsonSerializable(typeof(GetWorkflowExecutable))]
[JsonSerializable(typeof(WorkflowExecutableDetailsView))]
[JsonSerializable(typeof(GetWorkflowExecutableProvenance))]
[JsonSerializable(typeof(ExecutableProvenanceView))]
[JsonSerializable(typeof(GetWorkflowExecutableInputSources))]
[JsonSerializable(typeof(WorkflowExecutableInputSourcesView))]
[JsonSerializable(typeof(ExecuteWorkflow))]
[JsonSerializable(typeof(WorkflowExecutionStartDispatchView))]
[JsonSerializable(typeof(ListWorkflowActivationSlots))]
[JsonSerializable(typeof(WorkflowActivationSlotListView))]
[JsonSerializable(typeof(GetWorkflowActivationSlot))]
[JsonSerializable(typeof(WorkflowActivationSlotView))]
[JsonSerializable(typeof(ListWorkflowInstances))]
[JsonSerializable(typeof(IReadOnlyCollection<WorkflowInstanceSummaryView>))]
[JsonSerializable(typeof(WorkflowInstanceListView))]
[JsonSerializable(typeof(GetWorkflowInstance))]
[JsonSerializable(typeof(WorkflowInstanceDetailsView))]
[JsonSerializable(typeof(DeleteWorkflowInstance))]
[JsonSerializable(typeof(WorkflowInstanceDeletionView))]
[JsonSerializable(typeof(BrowseActivityExecutions))]
[JsonSerializable(typeof(ActivityExecutionBrowsePageView))]
[JsonSerializable(typeof(GetActivityExecution))]
[JsonSerializable(typeof(ActivityExecutionInspectionView))]
[JsonSerializable(typeof(GetRuntimeHistory))]
[JsonSerializable(typeof(RuntimeHistoryPageView))]
[JsonSerializable(typeof(GetRuntimeHistoryOccurrences))]
[JsonSerializable(typeof(RuntimeHistoryOccurrencePageView))]
[JsonSerializable(typeof(RuntimeProblemDetails))]
[JsonSerializable(typeof(RuntimeValidationProblemDetails))]
[JsonSerializable(typeof(ActivityExecutionProblemDetailsView))]
internal partial class RuntimeHttpJsonContext : JsonSerializerContext;

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
