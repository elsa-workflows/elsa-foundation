using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Local copy of the CLR activity construction test helper (the original lives in the Runtime tests assembly
/// and is <c>internal</c>). Builds the stable-alias descriptor payload the production
/// <c>ClrActivityConstructor</c> consumes, so the HTTP activities are exercised through the same
/// descriptor → construct → bind → execute path the real host uses.
/// </summary>
internal static class ClrConstruction
{
    /// <summary>The CLR construction descriptor type's registry key.</summary>
    public static readonly string DescriptorType = typeof(ClrActivityDescriptor).FullName!;

    /// <summary>The stable-alias descriptor payload for <paramref name="activityType"/>.</summary>
    public static JsonElement Payload(IPayloadSerializer serializer, Type activityType)
        => serializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType)));
}
