using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Elsa.Activities.Design.Tests.Api;

/// <summary>
/// Compile-time and reflection checks for the stable Activities Design API contract boundary.
/// The explicit type list is deliberately kept next to the forwarders: removing or renaming a public
/// request/response/enum requires an intentional compatibility decision rather than a silent source break.
/// </summary>
public sealed class ActivitiesDesignApiContractCompatibilityTests
{
    private static readonly Type[] ContractTypes =
    [
        typeof(ActivityActionAvailabilityView),
        typeof(ActivityAuthoringCapabilitiesView),
        typeof(ActivityAuthoringCatalogView),
        typeof(ActivityAuthoringDescriptorView),
        typeof(ActivityAuthoringIntrinsicView),
        typeof(ActivityAuthoringProvenanceView),
        typeof(ActivityAuthoringStructureView),
        typeof(ActivityAuthoringTemplateView),
        typeof(ActivityContractProposalChangeView),
        typeof(ActivityContractProposalView),
        typeof(ActivityContractTypeCapabilityView),
        typeof(ActivityContractView),
        typeof(ActivityDefinitionDetailsView),
        typeof(ActivityDefinitionIdentityView),
        typeof(ActivityDefinitionLifecycleSummaryView),
        typeof(ActivityDefinitionRecommendationView),
        typeof(ActivityDefinitionReferenceView),
        typeof(ActivityDefinitionVersionDetailsView),
        typeof(ActivityDefinitionVersionReferenceView),
        typeof(ActivityDefinitionView),
        typeof(ActivityDependencyConsistencyView),
        typeof(ActivityDependencyItemView),
        typeof(ActivityDependencyOccurrenceView),
        typeof(ActivityDependencyPageView),
        typeof(ActivityDependencyQueryView),
        typeof(ActivityDraftValidationView),
        typeof(ActivityForkAccessBindingView),
        typeof(ActivityForkCandidateLifecycleView),
        typeof(ActivityForkContractChangeView),
        typeof(ActivityForkContractComparisonView),
        typeof(ActivityForkOutcomeView),
        typeof(ActivityForkPresentationView),
        typeof(ActivityForkPreviewView),
        typeof(ActivityForkProviderMigrationView),
        typeof(ActivityForkReceiptView),
        typeof(ActivityForkSourceView),
        typeof(ActivityForkTargetView),
        typeof(ActivityInputContractView),
        typeof(ActivityInputDefaultView),
        typeof(ActivityInputDescriptorView),
        typeof(ActivityManagementPageView<>),
        typeof(ActivityManagementSnapshotView),
        typeof(ActivityOutcomeContractView),
        typeof(ActivityOutputContractView),
        typeof(ActivityOutputDescriptorView),
        typeof(ActivityPortDescriptorView),
        typeof(ActivityProblemDetailsView),
        typeof(ActivityProviderAuthoringCapabilityView),
        typeof(ActivityProviderManifestSchemaCapabilityView),
        typeof(ActivityProviderManifestView),
        typeof(ActivityPublishedTemplateView),
        typeof(ActivityRecoveryView),
        typeof(ActivityTypeReferenceView),
        typeof(ActivityUpgradeApplyReceiptView),
        typeof(ActivityUpgradeApplyResultView),
        typeof(ActivityUpgradePlanView),
        typeof(ActivityUpgradeReplacementView),
        typeof(ActivityUpgradeVersionIdentityView),
        typeof(ActivityVersionChangeSubjectView),
        typeof(ActivityVersionChangeView),
        typeof(ActivityVersionDiffIdentityView),
        typeof(ActivityVersionDiffSummaryView),
        typeof(ActivityVersionDiffView),
        typeof(ActivityVersionProviderDiffView),
        typeof(AddDefinition),
        typeof(AddVersion),
        typeof(ApplyActivityUpgradePlan),
        typeof(ApplyReusableActivityContractProposal),
        typeof(ApplyReusableActivityFork),
        typeof(CompareActivityVersions),
        typeof(CreateActivityUpgradePlan),
        typeof(CreateReusableActivityDefinition),
        typeof(CreateReusableActivityDraft),
        typeof(CreateReusableActivityDraftConflictCopy),
        typeof(DiscardReusableActivityDraft),
        typeof(GetActivityAuthoringCapabilities),
        typeof(GetActivityAvailabilitySettings),
        typeof(GetActivityDependencies),
        typeof(GetActivityUpgradeApplyReceipt),
        typeof(GetActivityUpgradePlan),
        typeof(GetDefinition),
        typeof(GetReusableActivityDefinition),
        typeof(GetReusableActivityDraft),
        typeof(GetReusableActivityForkStatus),
        typeof(GetReusableActivityVersion),
        typeof(GetVersion),
        typeof(ListActivityAuthoringCatalog),
        typeof(ListActivityAvailabilityDiagnostics),
        typeof(ListDefinitionVersions),
        typeof(ListDefinitions),
        typeof(ListRecommendedActivityDefinitions),
        typeof(ListReusableActivityDefinitions),
        typeof(ListReusableActivityDrafts),
        typeof(ListReusableActivityVersions),
        typeof(MigrateReusableActivityDraft),
        typeof(PreviewActivityDraftDiff),
        typeof(PreviewReusableActivityFork),
        typeof(ProposeReusableActivityContract),
        typeof(RecommendedActivityDefinitionPageView),
        typeof(RecommendedActivityDefinitionView),
        typeof(RefreshActivityUpgradePlan),
        typeof(ReplaceReusableActivityDraft),
        typeof(RestoreReusableActivityVersion),
        typeof(RetireReusableActivityVersion),
        typeof(ReusableActivityDefinitionManagementView),
        typeof(ReusableActivityDefinitionMutationView),
        typeof(ReusableActivityDraftManagementView),
        typeof(ReusableActivityDraftSummaryView),
        typeof(ReusableActivityDraftView),
        typeof(ReusableActivityVersionLifecycleView),
        typeof(ReusableActivityVersionManagementView),
        typeof(ReusableActivityVersionSummaryView),
        typeof(ReusableActivityVersionView),
        typeof(RevokeReusableActivityVersion),
        typeof(SaveActivityAvailabilitySettings),
        typeof(SetRecommendedReusableActivityVersion),
        typeof(UpdateReusableActivityDefinition),
        typeof(UpdateReusableActivityDraftPresentation),
        typeof(ValidateReusableActivityDraft),
        typeof(ActivityCatalogAvailability)
    ];

    /// <summary>
    /// Interfaces, exceptions, mapping helpers, and policies that share the contract namespaces but
    /// never appear on the wire. They stayed out of the former Api.Core assembly for the same reason.
    /// </summary>
    private static readonly Type[] ImplementationOnlyTypes =
    [
        typeof(DefaultActivityVersionSelectionPolicy),
        typeof(IActivityAuthoringContext),
        typeof(IActivityAuthoringContextAsync),
        typeof(IActivityVersionSelectionPolicy),
        typeof(ActivityAuthoringException),
        typeof(ActivityContractViewMappings),
        typeof(ActivityDependencyViewMappings),
        typeof(ActivityProblemDetails),
        typeof(ActivityUpgradeViewMappings),
        typeof(ActivityVersionDiffViewMappings)
    ];

    [Fact]
    public void Every_contract_type_is_publicly_exported_by_the_api_assembly()
    {
        Assert.Equal(120, ContractTypes.Length);

        var apiAssembly = typeof(ActivitiesDesignApiFeature).Assembly;

        Assert.Equal("Elsa.Activities.Design.Api", apiAssembly.GetName().Name);

        foreach (var contractType in ContractTypes)
        {
            Assert.Same(apiAssembly, contractType.Assembly);
            Assert.True(contractType.IsPublic, $"{contractType.FullName} must stay publicly exported.");
            Assert.Same(contractType, apiAssembly.GetType(contractType.FullName!, throwOnError: true));
        }

        // Every public type in the contract namespaces is classified as either a wire contract or an
        // implementation-only helper. The Api.Core assembly used to carry that distinction; with one
        // assembly per domain these lists are its only carrier, so a new or deleted type fails here
        // rather than reaching consumers unreviewed.
        Assert.Empty(ContractTypes.Intersect(ImplementationOnlyTypes));

        var exported = apiAssembly.GetExportedTypes()
            .Where(type => type.Namespace is "Elsa.Activities.Design.Api.Models" or "Elsa.Activities.Design.Api.Requests" or "Elsa.Activities.Design.Api.Commands")
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal);

        Assert.Equal(
            ContractTypes.Concat(ImplementationOnlyTypes).Select(type => type.FullName).Order(StringComparer.Ordinal),
            exported);
    }

    [Fact]
    public void Contract_types_preserve_the_complete_public_member_surface()
    {
        // This hash is deliberately updated only with a reviewed public contract change. It catches
        // accidental constructor/property/method drift even when the type list still compiles.
        // It moved once when the contracts left the Api.Core assembly: a constructed generic type's
        // FullName embeds its arguments' assembly-qualified names, so the strings changed while the
        // JSON wire shape did not.
        Assert.Equal("29f2f67d23618d6d2151fc688e01858eacb22d9126cb1b1769d16709b297bab5", PublicShapeHash(ContractTypes));
    }

    [Fact]
    public void Api_assembly_does_not_reference_retired_endpoint_frameworks()
    {
        var references = typeof(ActivitiesDesignApiFeature).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name is
            "Elsa.Api.FastEndpoints" or "FastEndpoints" or "FastEndpoints.Attributes");
    }

    private static string PublicShape(Type type) => string.Join(
        "\n",
        type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(member => member.MemberType is MemberTypes.Constructor or MemberTypes.Method or MemberTypes.Property or MemberTypes.Field or MemberTypes.Event)
            .Select(member => member switch
            {
                ConstructorInfo constructor => $"ctor:{constructor}",
                MethodInfo method => $"method:{method}",
                PropertyInfo property => $"property:{property.PropertyType.FullName}:{property.Name}:{string.Join(',', property.GetIndexParameters().Select(parameter => parameter.ParameterType.FullName))}",
                FieldInfo field => $"field:{field.FieldType.FullName}:{field.Name}",
                EventInfo @event => $"event:{@event.EventHandlerType?.FullName}:{@event.Name}",
                _ => member.ToString() ?? member.Name
            })
            .Order(StringComparer.Ordinal));

    private static string PublicShapeHash(IEnumerable<Type> types)
    {
        var shape = string.Join("\n", types.OrderBy(type => type.FullName, StringComparer.Ordinal).Select(type => $"{type.FullName}\n{PublicShape(type)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(shape))).ToLowerInvariant();
    }
}
