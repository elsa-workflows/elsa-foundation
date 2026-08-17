using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Publishing.Api.Requests;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Compile-time and reflection checks for the stable Publishing API contract boundary.
/// The explicit list is intentionally kept next to the forwarders: removing or changing a public
/// request/response member requires a compatibility decision rather than silently changing wire API.
/// </summary>
public sealed class PublishingApiContractCompatibilityTests
{
    private static readonly Type[] ContractTypes =
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
        typeof(PublicationSlotListResponse),
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
        typeof(ListPublicationSlots),
        typeof(GetPublicationSlot),
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

    [Fact]
    public void Every_contract_type_is_compiled_in_core_and_forwarded_by_the_legacy_api_assembly()
    {
        Assert.Equal(68, ContractTypes.Length);

        var coreAssembly = typeof(ConstructActivity).Assembly;
        var legacyAssembly = typeof(WorkflowsPublishingApiFeature).Assembly;

        Assert.Equal("Elsa.Workflows.Publishing.Api.Core", coreAssembly.GetName().Name);

        var forwarded = legacyAssembly.GetForwardedTypes()
            .Where(type => type.Namespace is "Elsa.Workflows.Publishing.Api.Models" or "Elsa.Workflows.Publishing.Api.Requests")
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
    public void Forwarded_types_preserve_the_public_member_surface()
    {
        var legacyAssembly = typeof(WorkflowsPublishingApiFeature).Assembly;

        foreach (var contractType in ContractTypes)
        {
            var forwardedType = legacyAssembly.GetType(contractType.FullName!, throwOnError: true)!;
            Assert.Equal(PublicShape(contractType), PublicShape(forwardedType));
        }

        // This hash is deliberately updated only with a reviewed public contract change. It catches
        // accidental constructor/property/method drift even when the forwarder list still compiles.
        var legacyTypes = ContractTypes.Where(type => type != typeof(ActivityPublishingDiagnosticView) &&
                                                       type != typeof(ActivityPublishingProblemDetails) &&
                                                       type != typeof(ExpressionPublicationValidationDiagnosticView) &&
                                                       type != typeof(ExpressionPublicationValidationProblemDetails) &&
                                                       type != typeof(RuntimePreflightProblemDetails)).ToArray();
        Assert.Equal(
            "0b73ac1112c405da944c99089dd55b68fbd872f930d7084f76b3adf336bc35e6",
            PublicShapeHash(legacyTypes));

        var actualHash = PublicShapeHash(ContractTypes);
        Assert.True(
            actualHash == "b4d3a74a659f310d47fb8fd399733821c2211aaab8105fa68b93b7b4edceb8e2",
            $"The Publishing API Core public-shape hash changed to {actualHash}.");
    }

    [Fact]
    public void Core_does_not_reference_endpoint_or_persistence_implementation_frameworks()
    {
        var references = typeof(ConstructActivity).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference => reference.Name is
            "Elsa.Api.FastEndpoints" or "FastEndpoints" or "FastEndpoints.Attributes" or
            "Elsa.Workflows.Publishing.Api" or "Elsa.Workflows.Publishing.Persistence.Groundwork" or
            "Elsa.Persistence.Groundwork" or "Elsa.Persistence.Core");
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
