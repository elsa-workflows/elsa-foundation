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
/// Compile-time and reflection checks for the Publishing API wire contract.
/// The explicit list is the tripwire: adding, removing, or changing a public request/response
/// member requires a deliberate compatibility decision rather than silently changing wire API.
/// </summary>
public sealed class PublishingApiContractCompatibilityTests
{
    private static readonly Type[] ContractTypes = PublishingApiContractSurface.ContractTypes;

    [Fact]
    public void Every_contract_type_is_publicly_exported_by_the_api_assembly()
    {
        // 68 before spec 151. T117 moved slot *reads* to the runtime activation-slot API, retiring
        // PublicationSlotListResponse, ListPublicationSlots and GetPublicationSlot from the publishing wire
        // contract; publishing keeps the slot lifecycle commands. The removal is recorded as an approved
        // difference against the frozen surface, so this number moves with that record and not on its own.
        Assert.Equal(65, ContractTypes.Length);

        var apiAssembly = typeof(WorkflowsPublishingApiFeature).Assembly;

        Assert.Equal("Elsa.Workflows.Publishing.Api", apiAssembly.GetName().Name);

        foreach (var contractType in ContractTypes)
        {
            Assert.Same(apiAssembly, contractType.Assembly);
            Assert.True(contractType.IsPublic, $"{contractType.FullName} must stay publicly exported.");
            Assert.Same(contractType, apiAssembly.GetType(contractType.FullName!, throwOnError: true));
        }

        // Every public type in the contract namespaces is classified as either a wire contract or an
        // implementation-only model, so a new or deleted type fails here rather than reaching
        // consumers unreviewed.
        Assert.Empty(ContractTypes.Intersect(PublishingApiContractSurface.ImplementationOnlyModelTypes));

        Assert.Equal(
            ContractTypes.Concat(PublishingApiContractSurface.ImplementationOnlyModelTypes)
                .Select(type => type.FullName).Order(StringComparer.Ordinal),
            PublishingApiContractSurface.ExportedContractNamespaceTypes()
                .Select(type => type.FullName).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Contract_types_preserve_the_public_member_surface()
    {
        // These hashes are deliberately updated only with a reviewed public contract change. They
        // catch accidental constructor/property/method drift even when the type list still compiles.
        // They moved once when the contracts left the Api.Core assembly: a constructed generic type's
        // FullName embeds its arguments' assembly-qualified names, so the strings changed while the
        // JSON wire shape did not.
        var legacyTypes = ContractTypes.Where(type => type != typeof(ActivityPublishingDiagnosticView) &&
                                                       type != typeof(ActivityPublishingProblemDetails) &&
                                                       type != typeof(ExpressionPublicationValidationDiagnosticView) &&
                                                       type != typeof(ExpressionPublicationValidationProblemDetails) &&
                                                       type != typeof(RuntimePreflightProblemDetails)).ToArray();
        // Moved again for spec 151 / T117: the three slot-read contract types left the publishing wire surface
        // when slot reads became runtime-owned. This is a deliberate contract change with a recorded approved
        // difference behind it, not drift -- the hash moves only alongside that record.
        Assert.Equal(
            "c3346b31bcc5fa4fcdf7fe21e409a4d2740f1ec51c08588c04d5e2adbd2e494b",
            PublicShapeHash(legacyTypes));

        var actualHash = PublicShapeHash(ContractTypes);
        Assert.True(
            actualHash == "5e7bcfb8542ce1e17769b8641191371fa204022bd30f03fe6592ec33acf8f5ba",
            $"The Publishing API public-shape hash changed to {actualHash}.");
    }

    [Fact]
    public void Api_assembly_does_not_reference_retired_endpoint_frameworks()
    {
        var references = typeof(WorkflowsPublishingApiFeature).Assembly.GetReferencedAssemblies();

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
