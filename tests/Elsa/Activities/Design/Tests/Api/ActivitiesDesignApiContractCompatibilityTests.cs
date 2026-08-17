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

    [Fact]
    public void Every_contract_type_is_compiled_in_core_and_forwarded_by_the_legacy_api_assembly()
    {
        Assert.Equal(120, ContractTypes.Length);

        var coreAssembly = typeof(ActivityDefinitionView).Assembly;
        var legacyAssembly = typeof(ActivitiesDesignApiFeature).Assembly;

        Assert.Equal("Elsa.Activities.Design.Api.Core", coreAssembly.GetName().Name);

        var forwarded = legacyAssembly.GetForwardedTypes()
            .Where(type => type.Namespace is "Elsa.Activities.Design.Api.Models" or "Elsa.Activities.Design.Api.Requests" or "Elsa.Activities.Design.Api.Commands")
            .ToDictionary(type => type.FullName!, StringComparer.Ordinal);

        foreach (var contractType in ContractTypes)
        {
            Assert.Same(coreAssembly, contractType.Assembly);
            Assert.True(forwarded.TryGetValue(contractType.FullName!, out var forwardedType), $"Missing type forwarder for {contractType.FullName}.");
            Assert.Same(contractType, forwardedType);
            Assert.Same(contractType, legacyAssembly.GetType(contractType.FullName!, throwOnError: true));
        }

        Assert.Equal(
            ContractTypes.Select(type => type.FullName).Order(StringComparer.Ordinal),
            coreAssembly.GetExportedTypes().Select(type => type.FullName).Order(StringComparer.Ordinal));
        Assert.Equal(ContractTypes.Select(type => type.FullName).Order(StringComparer.Ordinal), forwarded.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Forwarded_types_preserve_the_complete_public_member_surface()
    {
        var legacyAssembly = typeof(ActivitiesDesignApiFeature).Assembly;

        foreach (var contractType in ContractTypes)
        {
            var forwardedType = legacyAssembly.GetType(contractType.FullName!, throwOnError: true)!;
            Assert.Equal(PublicShape(contractType), PublicShape(forwardedType));
        }

        Assert.Equal("72ee0cb1931dd51ac0f0d3fc2c5254ed4dcdff6e3f010b915d8f0ac29af63132", PublicShapeHash(ContractTypes));
    }

    [Fact]
    public void Core_does_not_reference_endpoint_or_persistence_frameworks()
    {
        var references = typeof(ActivityDefinitionView).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name is
            "Elsa.Api.FastEndpoints" or "FastEndpoints" or "FastEndpoints.Attributes" or
            "Elsa.Activities.Design.Persistence.Core" or "Elsa.Persistence.Core");
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
