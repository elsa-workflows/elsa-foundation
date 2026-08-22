using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Workflows.Design.Api.Endpoints.Drafts;
using Elsa.Workflows.Design.Api.Endpoints.Versions;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Add;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Delete;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.DeletePermanently;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Get;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.List;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Restore;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.SoftDelete;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Submit;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Update;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.UpdateMetadata;

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
[JsonSerializable(typeof(ListDefinitions))]
[JsonSerializable(typeof(GetDefinition))]
[JsonSerializable(typeof(ListDefinitionVersions))]
[JsonSerializable(typeof(GetDraft))]
[JsonSerializable(typeof(GetDraftValidations))]
[JsonSerializable(typeof(GetVersion))]
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
[JsonSerializable(typeof(PreflightDraftPromotion))]
[JsonSerializable(typeof(WorkflowDesignError))]
internal partial class WorkflowsDesignJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Resolves Workflows Design metadata against the host's serializer options.
/// A new generated context is required for each resolution because OpenAPI adds
/// modifiers to the returned metadata and therefore freezes it in place.
/// </summary>
internal sealed class WorkflowsDesignJsonTypeInfoResolver : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        // The generated context's explicit resolver implementation dispatches to
        // its private Create_* factories with the caller's exact options. The
        // context instance only supplies those generated factories; its own
        // options are deliberately not used for the returned metadata.
        return ((IJsonTypeInfoResolver)new WorkflowsDesignJsonContext(new JsonSerializerOptions(JsonSerializerDefaults.Web)))
            .GetTypeInfo(type, options);
    }
}
