using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Api;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(AddDefinition))]
[JsonSerializable(typeof(AddVersion))]
[JsonSerializable(typeof(DeleteDefinition))]
[JsonSerializable(typeof(DeleteDefinitionPermanently))]
[JsonSerializable(typeof(DiscardDraft))]
[JsonSerializable(typeof(PromoteDraft))]
[JsonSerializable(typeof(ReplaceDraft))]
[JsonSerializable(typeof(RestoreDefinition))]
[JsonSerializable(typeof(SoftDeleteDefinition))]
[JsonSerializable(typeof(SubmitDefinition))]
[JsonSerializable(typeof(UpdateDefinition))]
[JsonSerializable(typeof(UpdateDefinitionMetadata))]
[JsonSerializable(typeof(AnalyzeScopedVariablesRequest))]
[JsonSerializable(typeof(ActivityInputOptionsRequest))]
[JsonSerializable(typeof(ExpressionToolingContextRequest))]
[JsonSerializable(typeof(ExpressionToolingCompletionRequest))]
[JsonSerializable(typeof(ExpressionToolingHoverRequest))]
[JsonSerializable(typeof(ExpressionToolingSourceRequest))]
[JsonSerializable(typeof(WorkflowDefinitionDetailsView))]
[JsonSerializable(typeof(WorkflowDefinitionListView))]
[JsonSerializable(typeof(WorkflowDefinitionVersionDetailsView))]
[JsonSerializable(typeof(SubmittedWorkflowDefinitionView))]
[JsonSerializable(typeof(WorkflowDefinitionSubmitSchemaView))]
[JsonSerializable(typeof(WorkflowDraftView))]
[JsonSerializable(typeof(PromotionPreflightAssessmentView))]
[JsonSerializable(typeof(DraftValidationsView))]
[JsonSerializable(typeof(ActivityStructuresResponse))]
[JsonSerializable(typeof(ScopedVariableAnalysisResponse))]
[JsonSerializable(typeof(ActivityInputOptionsResponse))]
[JsonSerializable(typeof(ExpressionToolingContextResponse))]
[JsonSerializable(typeof(ExpressionToolingDescriptorsResponse))]
[JsonSerializable(typeof(ExpressionToolingOperationResponse<ExpressionToolingItems>))]
[JsonSerializable(typeof(ExpressionToolingOperationResponse<ExpressionHover>))]
[JsonSerializable(typeof(ExpressionToolingOperationResponse<ExpressionDiagnosticSet>))]
[JsonSerializable(typeof(IEnumerable<WorkflowDefinitionVersionSummary>))]
[JsonSerializable(typeof(WorkflowDesignError))]
internal partial class WorkflowsDesignJsonContext : JsonSerializerContext
{
}
