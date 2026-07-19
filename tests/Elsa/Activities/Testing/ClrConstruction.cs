using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;

namespace Elsa.Activities.Testing;

/// <summary>
/// Test helper for the CLR activity construction path: builds the stable-alias descriptor
/// (<see cref="ClrActivityDescriptor"/>) and a well-known type registry whose entries use the same stable
/// aliases as the runtime activation path.
/// </summary>
public static class ClrConstruction
{
    /// <summary>The CLR construction descriptor type's registry key.</summary>
    public static readonly string DescriptorType = typeof(ClrActivityDescriptor).FullName!;

    /// <summary>The stable-alias descriptor payload for <paramref name="activityType"/>.</summary>
    public static JsonElement Payload(IPayloadSerializer serializer, Type activityType)
        => serializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType)));

    /// <summary>A well-known type registry that resolves each of <paramref name="activityTypes"/> by its canonical alias.</summary>
    public static IWellKnownTypeRegistry RegistryFor(params Type[] activityTypes)
    {
        var registry = new WellKnownTypeRegistry();
        foreach (var activityType in activityTypes)
            registry.RegisterType(activityType, TypeAliasConvention.CanonicalAlias(activityType));
        return registry;
    }
}
