using System.Text.Json;
using Elsa.Activities.Primitives.Binding;
using Elsa.Activities.Primitives.Constructors;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText.Services;

namespace Elsa.Activities.Testing;

/// <summary>
/// Test helper for the CLR activity construction path: builds the stable-alias descriptor
/// (<see cref="ClrActivityDescriptor"/>) and a <see cref="ClrActivityConstructor"/> whose well-known type
/// registry resolves the activity types under test by their canonical alias — the same wiring the real host
/// gets from <c>RegisterActivityTypesStartupTask</c>.
/// </summary>
public static class ClrConstruction
{
    /// <summary>The CLR Runtime consumer's durable key.</summary>
    public const string ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity;

    /// <summary>The stable-alias descriptor payload for <paramref name="activityType"/>.</summary>
    public static JsonElement Payload(IPayloadSerializer serializer, Type activityType)
        => serializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType)));

    public static RuntimeActivityDescriptor Descriptor(IPayloadSerializer serializer, Type activityType) =>
        new(ConsumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, Payload(serializer, activityType));

    /// <summary>A well-known type registry that resolves each of <paramref name="activityTypes"/> by its canonical alias.</summary>
    public static IWellKnownTypeRegistry RegistryFor(params Type[] activityTypes)
    {
        var registry = new WellKnownTypeRegistry();
        foreach (var activityType in activityTypes)
            registry.RegisterType(activityType, TypeAliasConvention.CanonicalAlias(activityType));
        return registry;
    }

    /// <summary>A CLR activity constructor whose registry resolves <paramref name="activityTypes"/>.</summary>
    public static ClrActivityConstructor Constructor(IServiceProvider serviceProvider, IPayloadSerializer serializer, params Type[] activityTypes)
        => new(serviceProvider, new ActivityArgumentBinder(), serializer, RegistryFor(activityTypes));
}
