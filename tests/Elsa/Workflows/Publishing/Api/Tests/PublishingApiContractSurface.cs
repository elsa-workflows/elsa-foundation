using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// The reviewed classification of every public type in the Publishing API contract namespaces.
/// </summary>
/// <remarks>
/// Publishing wire contracts used to live in a separate <c>Api.Core</c> assembly, and the assembly
/// boundary itself recorded which public models were part of the HTTP contract. With one assembly
/// per domain these lists are the only carrier of that distinction, so every public type in
/// <c>Api.Models</c> and <c>Api.Requests</c> must appear in exactly one of them.
/// </remarks>
internal static class PublishingApiContractSurface
{
    /// <summary>Types reachable from a route as a request or response body.</summary>
    internal static readonly Type[] ContractTypes =
    [
        typeof(ActivityDraftTestRunInput),
        typeof(StartActivityDraftTestRun),
        typeof(ActivityDraftTestRunView),
        typeof(ActivityDraftTestRunFailureView),
        typeof(ActivityDraftTestRunExpirationView),
        typeof(ActivityDraftTestRunCancellationView),
        typeof(GetActivityDraftTestRun),
        typeof(GetActivityDraftTestRunByIdempotencyKey),
        typeof(CancelActivityDraftTestRun),
        typeof(ActivityPublicationDependencyEvidenceView),
        typeof(ActivityPublicationCapabilityReadinessView),
        typeof(ActivityPublicationPreflightView),
        typeof(ActivityPublicationReceiptView),
        typeof(ConstructableActivityView),
        typeof(ConstructedActivityView),
        typeof(ArgumentView),
        typeof(IncidentStrategyReferenceView),
        typeof(IncidentStrategyDescriptorView),
        typeof(IncidentStrategiesResponse),
        typeof(PublicationView),
        typeof(PublicationSlotView),
        typeof(PublicationPolicyView),
        typeof(PublicationPolicyDefaultActionView),
        typeof(PublicationPolicyContract),
        typeof(PublicationActionView),
        typeof(PublicationPolicySourceView),
        typeof(PublicationTriggerChangeKindView),
        typeof(PublicationTriggerCardinalityView),
        typeof(PublicationTriggerChangeView),
        typeof(PublicationTriggerConflictView),
        typeof(PublicationContract),
        typeof(PublicationIntentContract),
        typeof(PublicationPreflightView),
        typeof(PublicationTriggerClaimView),
        typeof(PublicationSnapshotPreflightView),
        typeof(ActivityPublishingDiagnosticView),
        typeof(ActivityPublishingProblemDetails),
        typeof(ExpressionPublicationValidationDiagnosticView),
        typeof(ExpressionPublicationValidationProblemDetails),
        typeof(RuntimePreflightProblemDetails),
        typeof(RuntimeRequirementPreflightView),
        typeof(RuntimeRequirementPreflightItemView),
        typeof(ValueConversionProfileReferenceView),
        typeof(ValueConversionProfileView),
        typeof(ValueConversionProfilesResponse),
        typeof(WorkflowTestRunView),
        typeof(ConstructActivity),
        typeof(ListConstructableActivities),
        typeof(ListIncidentStrategies),
        typeof(ListValueConversionProfiles),
        typeof(PreflightActivityDraftPublication),
        typeof(GetActivityPublicationReceipt),
        typeof(GetWorkflowPublicationPolicy),
        typeof(SetWorkflowPublicationPolicy),
        typeof(PreflightWorkflowPublication),
        typeof(PreflightWorkflowPublicationSnapshot),
        typeof(PublishWorkflowRequest),
        typeof(UnpublishPublicationSlotRequest),
        typeof(RestorePublicationSlotRequest),
        typeof(UnpublishPublicationSlot),
        typeof(RestorePublicationSlot),
        typeof(PublishActivityDraft),
        typeof(RunRuntimeRequirementPreflight),
        typeof(StartWorkflowTestRun),
        typeof(StartWorkflowDraftTestRun)
    ];

    /// <summary>
    /// Public models that support the implementation but never appear on the wire, so they carry no
    /// source-generated JSON metadata and are absent from the OpenAPI document.
    /// </summary>
    internal static readonly Type[] ImplementationOnlyModelTypes =
    [
        typeof(PublishedActivityProviderView),
        typeof(PublishedActivityVersionChangeSubjectView),
        typeof(PublishedActivityVersionChangeView),
        typeof(PublishedActivityDiffView),
        typeof(PublishedActivityRuntimeRequirementView),
        typeof(PublishedActivityDefinitionView)
    ];

    /// <summary>Every public type exported from the two contract namespaces.</summary>
    internal static IEnumerable<Type> ExportedContractNamespaceTypes() =>
        typeof(WorkflowsPublishingApiFeature).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace is "Elsa.Workflows.Publishing.Api.Models" or "Elsa.Workflows.Publishing.Api.Requests");
}
