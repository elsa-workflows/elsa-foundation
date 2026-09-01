using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Workflows.Publishing.Api;

internal sealed class CamelCaseEnumConverter : JsonStringEnumConverter
{
    public CamelCaseEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
}

internal static class WorkflowsPublishingJsonOptions
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    /// <summary>The owner context bound to the effective wire options, shared by the mapper and the problem writer.</summary>
    internal static WorkflowsPublishingJsonContext WireContext { get; } = new(Create());

    public static void Configure(JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        // The former Minimal API JsonResult path wrote with the host HTTP options, whose default
        // encoder is relaxed; the owner options keep that wire shape.
        options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

        if (!options.TypeInfoResolverChain.Any(resolver => resolver is JsonTypeInfoResolver))
            options.TypeInfoResolverChain.Insert(0, new JsonTypeInfoResolver());
        if (!options.Converters.Any(converter => converter is CamelCaseEnumConverter))
            options.Converters.Add(new CamelCaseEnumConverter());
    }
}

/// <summary>
/// Owner-local source-generated metadata for every Publishing accepts/produces contract.
/// The generated resolver is attached to the host's effective HTTP JSON options and is the only
/// resolver allowed to describe Publishing API types; the fallback resolver may handle unrelated
/// host payloads but must never be required for this route contract graph.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]

// Requests accepted by the 23 Publishing registrations.
[JsonSerializable(typeof(ConstructActivity))]
[JsonSerializable(typeof(ListConstructableActivities))]
[JsonSerializable(typeof(ListIncidentStrategies))]
[JsonSerializable(typeof(ListValueConversionProfiles))]
[JsonSerializable(typeof(PreflightActivityDraftPublication))]
[JsonSerializable(typeof(GetActivityPublicationReceipt))]
[JsonSerializable(typeof(GetWorkflowPublicationPolicy))]
[JsonSerializable(typeof(SetWorkflowPublicationPolicy))]
[JsonSerializable(typeof(PreflightWorkflowPublication))]
[JsonSerializable(typeof(PreflightWorkflowPublicationSnapshot))]
[JsonSerializable(typeof(PublishWorkflowRequest))]
[JsonSerializable(typeof(UnpublishPublicationSlotRequest))]
[JsonSerializable(typeof(RestorePublicationSlotRequest))]
[JsonSerializable(typeof(UnpublishPublicationSlot))]
[JsonSerializable(typeof(RestorePublicationSlot))]
[JsonSerializable(typeof(PublishActivityDraft))]
[JsonSerializable(typeof(RunRuntimeRequirementPreflight))]
[JsonSerializable(typeof(StartWorkflowTestRun))]
[JsonSerializable(typeof(StartWorkflowDraftTestRun))]
[JsonSerializable(typeof(StartActivityDraftTestRun))]
[JsonSerializable(typeof(GetActivityDraftTestRun))]
[JsonSerializable(typeof(GetActivityDraftTestRunByIdempotencyKey))]
[JsonSerializable(typeof(CancelActivityDraftTestRun))]

// Responses emitted by the owner mapper and explicit roots commonly nested behind collections,
// dictionaries, nullable values, opaque JsonElement payloads, or shared domain projections.
[JsonSerializable(typeof(IEnumerable<ConstructableActivityView>))]
[JsonSerializable(typeof(ConstructableActivityView))]
[JsonSerializable(typeof(ConstructedActivityView))]
[JsonSerializable(typeof(ArgumentView))]
[JsonSerializable(typeof(IncidentStrategyReferenceView))]
[JsonSerializable(typeof(IncidentStrategyDescriptorView))]
[JsonSerializable(typeof(IncidentStrategiesResponse))]
[JsonSerializable(typeof(ActivityPublicationDependencyEvidenceView))]
[JsonSerializable(typeof(ActivityPublicationCapabilityReadinessView))]
[JsonSerializable(typeof(ActivityPublicationPreflightView))]
[JsonSerializable(typeof(ActivityPublicationReceiptView))]
[JsonSerializable(typeof(ActivityDraftTestRunInput))]
[JsonSerializable(typeof(ActivityDraftTestRunView))]
[JsonSerializable(typeof(ActivityDraftTestRunFailureView))]
[JsonSerializable(typeof(ActivityDraftTestRunExpirationView))]
[JsonSerializable(typeof(ActivityDraftTestRunCancellationView))]
[JsonSerializable(typeof(PublicationView))]
[JsonSerializable(typeof(PublicationSlotView))]
[JsonSerializable(typeof(PublicationPolicyView))]
[JsonSerializable(typeof(PublicationPreflightView))]
[JsonSerializable(typeof(PublicationTriggerClaimView))]
[JsonSerializable(typeof(PublicationTriggerChangeView))]
[JsonSerializable(typeof(PublicationTriggerConflictView))]
[JsonSerializable(typeof(PublicationSnapshotPreflightView))]
[JsonSerializable(typeof(ActivityPublishingDiagnosticView))]
[JsonSerializable(typeof(ActivityPublishingProblemDetails))]
[JsonSerializable(typeof(Endpoints.WorkflowPublishingLegacyProblem))]
[JsonSerializable(typeof(Endpoints.WorkflowPublishingLegacyProblemError))]
[JsonSerializable(typeof(ExpressionPublicationValidationDiagnosticView))]
[JsonSerializable(typeof(ExpressionPublicationValidationProblemDetails))]
[JsonSerializable(typeof(RuntimePreflightProblemDetails))]
[JsonSerializable(typeof(RuntimeRequirementPreflightView))]
[JsonSerializable(typeof(RuntimeRequirementPreflightItemView))]
[JsonSerializable(typeof(ValueConversionProfileReferenceView))]
[JsonSerializable(typeof(ValueConversionProfileView))]
[JsonSerializable(typeof(ValueConversionProfilesResponse))]
[JsonSerializable(typeof(WorkflowTestRunView))]
[JsonSerializable(typeof(PublishedWorkflowView))]

// Stable shared contracts reachable from Publishing request/response graphs.
[JsonSerializable(typeof(ActivityDiagnostic))]
[JsonSerializable(typeof(ActivityDependencyPathItem))]
[JsonSerializable(typeof(ActivityNode))]
[JsonSerializable(typeof(ActivityNodeOrigin))]
[JsonSerializable(typeof(ActivityNodeStructure))]
[JsonSerializable(typeof(ActivityVersionBump))]
[JsonSerializable(typeof(ActivityVersionCompatibility))]
[JsonSerializable(typeof(ActivityVersionDiff))]
[JsonSerializable(typeof(ActivityVersionDiffSummary))]
[JsonSerializable(typeof(ActivityVersionChange))]
[JsonSerializable(typeof(ActivityVersionChangeSubject))]
[JsonSerializable(typeof(ActivityVersionIdentity))]
[JsonSerializable(typeof(ActivityVersionProviderDiff))]
[JsonSerializable(typeof(ActivityPublicationOutcome))]
[JsonSerializable(typeof(ArgumentState))]
[JsonSerializable(typeof(ArgumentValue))]
[JsonSerializable(typeof(AuthoredValueConversionLimits))]
[JsonSerializable(typeof(AuthoredValueConversionMode))]
[JsonSerializable(typeof(AuthoredValueConversionProfile))]
[JsonSerializable(typeof(AuthoredValueConversionRequest))]
[JsonSerializable(typeof(AuthoredWorkflowIntrinsic))]
[JsonSerializable(typeof(AuthoredWorkflowIntrinsicKind))]
[JsonSerializable(typeof(CollectionKind))]
[JsonSerializable(typeof(IncidentStrategyReference))]
[JsonSerializable(typeof(PublicationFailure))]
[JsonSerializable(typeof(WorkflowDefinitionState))]
[JsonSerializable(typeof(DesignMetadataRecord))]
[JsonSerializable(typeof(ActivityPresentationRecord))]
[JsonSerializable(typeof(InputDefinition))]
[JsonSerializable(typeof(OutputDefinition))]
[JsonSerializable(typeof(TypeReference))]
[JsonSerializable(typeof(ValueRepresentation))]
[JsonSerializable(typeof(VariableDefinition))]
[JsonSerializable(typeof(VariableReference))]
[JsonSerializable(typeof(WorkflowCheckpointCadenceOptions))]
[JsonSerializable(typeof(WorkflowStrategyOptions))]
[JsonSerializable(typeof(JsonElement))]
internal partial class WorkflowsPublishingJsonContext : JsonSerializerContext
{
}

/// <summary>Resolves generated metadata using the host's exact mutable serializer options.</summary>
internal sealed class JsonTypeInfoResolver : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        // API Explorer modifies metadata using the host options. A fresh generated context supplies
        // factories without becoming a process-global metadata cache that could retain host state.
        return ((IJsonTypeInfoResolver)new WorkflowsPublishingJsonContext(WorkflowsPublishingJsonOptions.Create()))
            .GetTypeInfo(type, options);
    }
}
