using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Endpoints;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Activities.Design.Api;

internal sealed class ActivitiesDesignCamelCaseEnumConverter : JsonStringEnumConverter
{
    public ActivitiesDesignCamelCaseEnumConverter() : base(JsonNamingPolicy.CamelCase) { }
}

internal static class ActivitiesDesignJsonOptions
{
    public static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Configure(options);
        return options;
    }

    /// <summary>The owner context bound to the effective wire options, shared by the mapper and the failure writers.</summary>
    internal static ActivitiesDesignJsonContext WireContext { get; } = new(Create());

    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNameCaseInsensitive = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = JsonNamingPolicy.CamelCase;
        // The former Minimal API JsonResult path wrote with the host HTTP options, whose default
        // encoder is relaxed; the owner options keep that wire shape.
        options.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

        if (!options.TypeInfoResolverChain.Any(resolver => resolver is ActivitiesDesignJsonTypeInfoResolver))
            options.TypeInfoResolverChain.Insert(0, new ActivitiesDesignJsonTypeInfoResolver());
        if (!options.Converters.Any(converter => converter is ActivitiesDesignCamelCaseEnumConverter))
            options.Converters.Add(new ActivitiesDesignCamelCaseEnumConverter());
    }
}

/// <summary>
/// The owner-local source-generated contract for the Activities Design HTTP surface.
///
/// The options deliberately mirror <c>SerializationFastEndpointConfigurator</c>: web defaults,
/// case-insensitive input, camel-case properties and dictionary keys, and camel-case string enums.
/// Keeping this list explicit is important.  It makes the request/response graph a reviewable
/// lifetime boundary instead of allowing API Explorer or a JSON call to fall back to reflection
/// over a replaceable feature assembly.
/// </summary>
[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DictionaryKeyPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Metadata)]

// Requests and commands accepted by the 38 registrations, including the two legacy mediator
// commands retained for handler and binary-compatibility coverage.
[JsonSerializable(typeof(GetActivityAvailabilitySettings))]
[JsonSerializable(typeof(ListActivityAvailabilityDiagnostics))]
[JsonSerializable(typeof(SaveActivityAvailabilitySettings))]
[JsonSerializable(typeof(ListActivityAuthoringCatalog))]
[JsonSerializable(typeof(GetActivityAuthoringCapabilities))]
[JsonSerializable(typeof(ListDefinitions))]
[JsonSerializable(typeof(CreateReusableActivityDefinition))]
[JsonSerializable(typeof(PreviewReusableActivityFork))]
[JsonSerializable(typeof(ListReusableActivityDefinitions))]
[JsonSerializable(typeof(GetReusableActivityDefinition))]
[JsonSerializable(typeof(UpdateReusableActivityDefinition))]
[JsonSerializable(typeof(SetRecommendedReusableActivityVersion))]
[JsonSerializable(typeof(ListRecommendedActivityDefinitions))]
[JsonSerializable(typeof(ListReusableActivityDrafts))]
[JsonSerializable(typeof(ListReusableActivityVersions))]
[JsonSerializable(typeof(CreateReusableActivityDraft))]
[JsonSerializable(typeof(GetReusableActivityDraft))]
[JsonSerializable(typeof(ReplaceReusableActivityDraft))]
[JsonSerializable(typeof(UpdateReusableActivityDraftPresentation))]
[JsonSerializable(typeof(CreateReusableActivityDraftConflictCopy))]
[JsonSerializable(typeof(ValidateReusableActivityDraft))]
[JsonSerializable(typeof(MigrateReusableActivityDraft))]
[JsonSerializable(typeof(ProposeReusableActivityContract))]
[JsonSerializable(typeof(ApplyReusableActivityContractProposal))]
[JsonSerializable(typeof(DiscardReusableActivityDraft))]
[JsonSerializable(typeof(PreviewActivityDraftDiff))]
[JsonSerializable(typeof(ApplyReusableActivityFork))]
[JsonSerializable(typeof(GetReusableActivityForkStatus))]
[JsonSerializable(typeof(GetActivityDependencies))]
[JsonSerializable(typeof(CompareActivityVersions))]
[JsonSerializable(typeof(GetVersion))]
[JsonSerializable(typeof(RetireReusableActivityVersion))]
[JsonSerializable(typeof(RestoreReusableActivityVersion))]
[JsonSerializable(typeof(RevokeReusableActivityVersion))]
[JsonSerializable(typeof(CreateActivityUpgradePlan))]
[JsonSerializable(typeof(GetActivityUpgradePlan))]
[JsonSerializable(typeof(ApplyActivityUpgradePlan))]
[JsonSerializable(typeof(GetActivityUpgradeApplyReceipt))]
[JsonSerializable(typeof(RefreshActivityUpgradePlan))]
[JsonSerializable(typeof(ListDefinitionVersions))]
[JsonSerializable(typeof(GetDefinition))]
[JsonSerializable(typeof(GetReusableActivityVersion))]
[JsonSerializable(typeof(AddDefinition))]
[JsonSerializable(typeof(AddVersion))]

// Responses and shared error contracts emitted by the owner mapper.
[JsonSerializable(typeof(ActivityAvailabilitySettings))]
[JsonSerializable(typeof(ActivityAvailabilityDiagnostics))]
[JsonSerializable(typeof(ActivityAuthoringCatalogView))]
[JsonSerializable(typeof(ActivityAuthoringDescriptorView))]
[JsonSerializable(typeof(ActivityAuthoringProvenanceView))]
[JsonSerializable(typeof(ActivityAuthoringIntrinsicView))]
[JsonSerializable(typeof(ActivityInputDescriptorView))]
[JsonSerializable(typeof(ActivityOutputDescriptorView))]
[JsonSerializable(typeof(ActivityPortDescriptorView))]
[JsonSerializable(typeof(ActivityAuthoringTemplateView))]
[JsonSerializable(typeof(ActivityAuthoringStructureView))]
[JsonSerializable(typeof(ActivityDefinitionView))]
[JsonSerializable(typeof(ActivityDefinitionDetailsView))]
[JsonSerializable(typeof(ActivityDefinitionVersionDetailsView))]
[JsonSerializable(typeof(ActivityDefinitionVersionSummary))]
[JsonSerializable(typeof(IEnumerable<ActivityDefinitionVersionSummary>))]
[JsonSerializable(typeof(ActivityDependencyPageView))]
[JsonSerializable(typeof(ActivityAuthoringCapabilitiesView))]
[JsonSerializable(typeof(ActivityAuthoringCapabilitiesSnapshot))]
[JsonSerializable(typeof(ActivityManagementPageView<ReusableActivityDefinitionManagementView>))]
[JsonSerializable(typeof(ActivityManagementPageView<ReusableActivityDraftManagementView>))]
[JsonSerializable(typeof(ActivityManagementPageView<ReusableActivityVersionManagementView>))]
[JsonSerializable(typeof(RecommendedActivityDefinitionPageView))]
[JsonSerializable(typeof(RecommendedActivityDefinitionView))]
[JsonSerializable(typeof(ReusableActivityDefinitionMutationView))]
[JsonSerializable(typeof(ReusableActivityDefinitionManagementView))]
[JsonSerializable(typeof(ActivityDefinitionIdentityView))]
[JsonSerializable(typeof(ActivityDefinitionRecommendationView))]
[JsonSerializable(typeof(ActivityForkPreviewView))]
[JsonSerializable(typeof(ActivityForkReceiptView))]
[JsonSerializable(typeof(ActivityForkAccessBindingView))]
[JsonSerializable(typeof(ActivityForkSourceView))]
[JsonSerializable(typeof(ActivityForkPresentationView))]
[JsonSerializable(typeof(ActivityForkTargetView))]
[JsonSerializable(typeof(ActivityForkProviderMigrationView))]
[JsonSerializable(typeof(ActivityForkContractChangeView))]
[JsonSerializable(typeof(ActivityForkContractComparisonView))]
[JsonSerializable(typeof(ReusableActivityDraftView))]
[JsonSerializable(typeof(ReusableActivityDraftSummaryView))]
[JsonSerializable(typeof(ActivityManagementSnapshotView))]
[JsonSerializable(typeof(ActivityDefinitionVersionReferenceView))]
[JsonSerializable(typeof(ActivityDefinitionLifecycleSummaryView))]
[JsonSerializable(typeof(ActivityActionAvailabilityView))]
[JsonSerializable(typeof(ReusableActivityDraftManagementView))]
[JsonSerializable(typeof(ReusableActivityVersionManagementView))]
[JsonSerializable(typeof(ReusableActivityVersionView))]
[JsonSerializable(typeof(ReusableActivityVersionSummaryView))]
[JsonSerializable(typeof(ActivityPublishedTemplateView))]
[JsonSerializable(typeof(ReusableActivityVersionLifecycleView))]
[JsonSerializable(typeof(ActivityContractProposalView))]
[JsonSerializable(typeof(ActivityContractProposalChangeView))]
[JsonSerializable(typeof(ActivityVersionDiffView))]
[JsonSerializable(typeof(ActivityVersionDiffIdentityView))]
[JsonSerializable(typeof(ActivityVersionProviderDiffView))]
[JsonSerializable(typeof(ActivityVersionDiffSummaryView))]
[JsonSerializable(typeof(ActivityVersionChangeSubjectView))]
[JsonSerializable(typeof(ActivityVersionChangeView))]
[JsonSerializable(typeof(ActivityUpgradePlanView))]
[JsonSerializable(typeof(ActivityUpgradeVersionIdentityView))]
[JsonSerializable(typeof(ActivityUpgradeReplacementView))]
[JsonSerializable(typeof(ActivityUpgradeApplyResultView))]
[JsonSerializable(typeof(ActivityUpgradeApplyReceiptView))]
[JsonSerializable(typeof(ActivityProblemDetailsView))]
[JsonSerializable(typeof(ActivityRecoveryView))]
[JsonSerializable(typeof(ActivitiesDesignLegacyProblem))]

// Explicit roots for contract types that are commonly nested behind an opaque provider payload,
// optional values, dictionaries, or a generic response.  These roots also give the completeness
// test a stable, direct metadata surface for the types consumed by native API Explorer.
[JsonSerializable(typeof(ActivityContractView))]
[JsonSerializable(typeof(ActivityTypeReferenceView))]
[JsonSerializable(typeof(ActivityInputDefaultView))]
[JsonSerializable(typeof(ActivityInputContractView))]
[JsonSerializable(typeof(ActivityOutputContractView))]
[JsonSerializable(typeof(ActivityOutcomeContractView))]
[JsonSerializable(typeof(ActivityProviderManifestView))]
[JsonSerializable(typeof(ActivityProviderManifestSchemaCapabilityView))]
[JsonSerializable(typeof(ActivityProviderAuthoringCapabilityView))]
[JsonSerializable(typeof(ActivityContractTypeCapabilityView))]
[JsonSerializable(typeof(ActivityProviderManifest))]
[JsonSerializable(typeof(ActivityLayoutRecord))]
[JsonSerializable(typeof(ActivityArgumentValue))]
[JsonSerializable(typeof(ActivityDesignFacet))]
[JsonSerializable(typeof(InputDefinition))]
[JsonSerializable(typeof(OutputDefinition))]
[JsonSerializable(typeof(ActivityTypeKeyRules))]
[JsonSerializable(typeof(ActivityAvailabilityRuleSet))]
[JsonSerializable(typeof(ActivityDiagnostic))]
[JsonSerializable(typeof(ActivityUpgradeExpectedSnapshot))]
[JsonSerializable(typeof(ActivityUpgradeStep))]
[JsonSerializable(typeof(ActivityUpgradeStage))]
[JsonSerializable(typeof(ActivityUpgradeApplyReceipt))]
[JsonSerializable(typeof(ActivityUpgradePublicationReceipt))]
[JsonSerializable(typeof(JsonElement))]
internal partial class ActivitiesDesignJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Resolves the generated Activities Design metadata against the host's actual serializer options.
/// A fresh context is created for each lookup because API Explorer may add modifiers to returned
/// metadata; the singleton context must never become a mutable host-wide metadata cache.
/// </summary>
internal sealed class ActivitiesDesignJsonTypeInfoResolver : IJsonTypeInfoResolver
{
    public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);

        // The generated context's resolver implementation uses the caller's options for the
        // returned metadata. Its own options are only used to supply the generated factories.
        return ((IJsonTypeInfoResolver)new ActivitiesDesignJsonContext(ActivitiesDesignJsonOptions.Create()))
            .GetTypeInfo(type, options);
    }
}
