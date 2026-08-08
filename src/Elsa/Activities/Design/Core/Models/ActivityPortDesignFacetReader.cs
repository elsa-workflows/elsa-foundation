using System.Text.Json;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// A fully resolved outcome port declared by a design facet: the reader has already applied the
/// convention's fallbacks, so every consumer projects the same values without restating them.
/// </summary>
public sealed record ActivityPortDesignDescriptor(
    string Name,
    string DisplayName,
    string? Type,
    bool IsBrowsable,
    string ReferenceKey);

/// <summary>
/// The single reader of the <c>"ports"</c> design-facet convention (the array emitted by the CLR
/// scanner's <c>elsa.outcomes</c> facet). Sibling of <see cref="ActivityStructureDesignFacetReader"/>.
/// </summary>
/// <remarks>
/// One-projection rule (spec 149 / RFC #1191): the served authoring catalog
/// (<c>ListActivityAuthoringCatalogRequestHandler.ToPorts</c>) and the build-time contract fragments
/// (<c>tools/contracts/Elsa.Contracts.Generator</c>) both parse this convention. Parsing it in two
/// places would let the served catalog and the published contract disagree about an activity's ports
/// after any change to the facet shape, so the convention — key names, the display-name and
/// reference-key fallbacks to <c>name</c>, and the browsable default — lives here only.
/// </remarks>
public static class ActivityPortDesignFacetReader
{
    public static IEnumerable<ActivityPortDesignDescriptor> Read(ActivityDesignFacet facet)
    {
        // Validate eagerly: the iterator body below would otherwise defer the guard until enumeration,
        // turning a caller's null bug into a failure at an unrelated call site.
        ArgumentNullException.ThrowIfNull(facet);
        return ReadCore(facet);
    }

    private static IEnumerable<ActivityPortDesignDescriptor> ReadCore(ActivityDesignFacet facet)
    {
        if (facet.Payload.ValueKind != JsonValueKind.Object ||
            !facet.Payload.TryGetProperty("ports", out var ports) ||
            ports.ValueKind != JsonValueKind.Array)
            yield break;

        var namedPorts = ports.EnumerateArray()
            .Where(candidate => candidate.ValueKind == JsonValueKind.Object &&
                                candidate.TryGetProperty("name", out var nameProperty) &&
                                !string.IsNullOrWhiteSpace(nameProperty.GetString()));

        foreach (var port in namedPorts)
        {
            var name = port.GetProperty("name").GetString()!;
            yield return new ActivityPortDesignDescriptor(
                name,
                ReadString(port, "displayName") ?? name,
                ReadString(port, "type"),
                ReadBoolean(port, "isBrowsable") ?? true,
                ReadString(port, "referenceKey") ?? name);
        }
    }

    /// <summary>Reads the ports of every facet that declares any, preserving facet order.</summary>
    public static IEnumerable<ActivityPortDesignDescriptor> ReadAll(IEnumerable<ActivityDesignFacet> facets)
    {
        ArgumentNullException.ThrowIfNull(facets);
        return facets.SelectMany(Read);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static bool? ReadBoolean(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;
}
