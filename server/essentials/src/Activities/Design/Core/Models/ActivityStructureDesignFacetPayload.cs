using System.Text.Json;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Payload for a structure <see cref="ActivityDesignFacet"/> describing authored child slots.
/// </summary>
public sealed record ActivityStructureDesignFacetPayload(
    string Mode,
    bool SupportsScopedVariables,
    IReadOnlyCollection<ActivityChildSlotDesignDescriptor> Slots,
    IReadOnlyDictionary<string, object?> InitialPayload);

/// <summary>
/// Exact authored structure identity declared by an activity definition version.
/// </summary>
public sealed record ActivityStructureContract(string Kind, string SchemaVersion);

/// <summary>
/// Recognizes the typed structure-facet payload without interpreting arbitrary facet kinds.
/// </summary>
public static class ActivityStructureDesignFacetReader
{
    public static bool TryReadContract(ActivityDesignFacet facet, out ActivityStructureContract contract)
    {
        ArgumentNullException.ThrowIfNull(facet);
        contract = null!;
        var payload = facet.Payload;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("mode", out var mode) ||
            mode.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(mode.GetString()) ||
            !payload.TryGetProperty("supportsScopedVariables", out var supportsScopedVariables) ||
            supportsScopedVariables.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
            !payload.TryGetProperty("slots", out var slots) ||
            slots.ValueKind != JsonValueKind.Array ||
            !payload.TryGetProperty("initialPayload", out var initialPayload) ||
            initialPayload.ValueKind != JsonValueKind.Object)
            return false;

        contract = new(facet.Kind, facet.SchemaVersion);
        return true;
    }

    public static bool TryReadSingle(
        IEnumerable<ActivityDesignFacet> facets,
        out ActivityDesignFacet? structureFacet)
    {
        ArgumentNullException.ThrowIfNull(facets);
        var matches = facets
            .Where(facet => TryReadContract(facet, out _))
            .Take(2)
            .ToArray();
        structureFacet = matches.Length == 1 ? matches[0] : null;
        return matches.Length < 2;
    }
}
