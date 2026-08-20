using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Requests;
using System.Runtime.CompilerServices;

// The public wire-contract types now live in Api.Core so native API-description metadata cannot retain a
// collectible Workflows Design implementation assembly: ASP.NET Core's API Explorer and OpenAPI document
// service hold on to each endpoint's request and response Type even after the endpoint data source is
// removed and drained (ADR 0069). Keep the old API assembly as a binary-compatible forwarding facade;
// implementation-only handlers, adapters, and mapping helpers intentionally remain here.
//
// The forwarded list is exhaustive and asserted by
// Every_contract_type_is_compiled_in_core_and_forwarded_by_the_legacy_api_assembly, so adding, removing,
// or renaming a public request/response/enum is an intentional compatibility decision rather than a
// silent source break.
[assembly: TypeForwardedTo(typeof(ActivityInputOptionsRequest))]
[assembly: TypeForwardedTo(typeof(ActivityInputOptionsResponse))]
[assembly: TypeForwardedTo(typeof(ActivityPresentationRecordView))]
[assembly: TypeForwardedTo(typeof(ActivityStructureView))]
[assembly: TypeForwardedTo(typeof(ActivityStructuresResponse))]
[assembly: TypeForwardedTo(typeof(AddDefinition))]
[assembly: TypeForwardedTo(typeof(AddVersion))]
[assembly: TypeForwardedTo(typeof(AnalyzeScopedVariablesRequest))]
[assembly: TypeForwardedTo(typeof(DiscardDraft))]
[assembly: TypeForwardedTo(typeof(DeleteDefinition))]
[assembly: TypeForwardedTo(typeof(DeleteDefinitionPermanently))]
[assembly: TypeForwardedTo(typeof(DraftValidationErrorView))]
[assembly: TypeForwardedTo(typeof(DraftValidationsView))]
[assembly: TypeForwardedTo(typeof(ExpressionToolingCompletionRequest))]
[assembly: TypeForwardedTo(typeof(ExpressionToolingContextRequest))]
[assembly: TypeForwardedTo(typeof(ExpressionToolingContextResponse))]
[assembly: TypeForwardedTo(typeof(ExpressionToolingDescriptor))]
[assembly: TypeForwardedTo(typeof(ExpressionToolingDescriptorsResponse))]
[assembly: TypeForwardedTo(typeof(ExpressionToolingHoverRequest))]
[assembly: TypeForwardedTo(typeof(ExpressionToolingOperationResponse<>))]
[assembly: TypeForwardedTo(typeof(ExpressionToolingSourceRequest))]
[assembly: TypeForwardedTo(typeof(GetDefinition))]
[assembly: TypeForwardedTo(typeof(GetDraft))]
[assembly: TypeForwardedTo(typeof(GetDraftValidations))]
[assembly: TypeForwardedTo(typeof(GetVersion))]
[assembly: TypeForwardedTo(typeof(GetWorkflowDefinitionSubmitSchema))]
[assembly: TypeForwardedTo(typeof(ListActivityStructures))]
[assembly: TypeForwardedTo(typeof(ListDefinitionVersions))]
[assembly: TypeForwardedTo(typeof(ListDefinitions))]
[assembly: TypeForwardedTo(typeof(PreflightDraftPromotion))]
[assembly: TypeForwardedTo(typeof(PromotionPreflightAssessmentView))]
[assembly: TypeForwardedTo(typeof(PromotionPreflightIssueView))]
[assembly: TypeForwardedTo(typeof(ReplaceDraft))]
[assembly: TypeForwardedTo(typeof(PromoteDraft))]
[assembly: TypeForwardedTo(typeof(ScopedVariableAnalysisResponse))]
[assembly: TypeForwardedTo(typeof(SoftDeleteDefinition))]
[assembly: TypeForwardedTo(typeof(SubmitDefinition))]
[assembly: TypeForwardedTo(typeof(SubmittedWorkflowDefinitionView))]
[assembly: TypeForwardedTo(typeof(UpdateDefinition))]
[assembly: TypeForwardedTo(typeof(UpdateDefinitionMetadata))]
[assembly: TypeForwardedTo(typeof(WorkflowDefinitionDetailsView))]
[assembly: TypeForwardedTo(typeof(WorkflowDefinitionLayoutRecordView))]
[assembly: TypeForwardedTo(typeof(WorkflowDefinitionListView))]
[assembly: TypeForwardedTo(typeof(WorkflowDefinitionStateView))]
[assembly: TypeForwardedTo(typeof(WorkflowDefinitionSubmitSchemaView))]
[assembly: TypeForwardedTo(typeof(WorkflowDefinitionVersionDetailsView))]
[assembly: TypeForwardedTo(typeof(WorkflowDefinitionView))]
[assembly: TypeForwardedTo(typeof(WorkflowDraftView))]
[assembly: TypeForwardedTo(typeof(RestoreDefinition))]
