namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Payload for a structure <see cref="ActivityDesignFacet"/> describing authored child slots.
/// </summary>
public sealed record ActivityStructureDesignFacetPayload(
    string Mode,
    bool SupportsScopedVariables,
    IReadOnlyCollection<ActivityChildSlotDesignDescriptor> Slots,
    IReadOnlyDictionary<string, object?> InitialPayload);
