using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// Inputs whose materialization belongs to an activity's structure. Consumed catalog keys are excluded
/// from ordinary property binding; a projected input with no definition uses its catalog declaration.
/// A consumed key need not be projected when the structure does not use it.
/// </summary>
public sealed record ActivityStructureInputProjection(
    IReadOnlyCollection<ActivityStructureInput> Inputs,
    IReadOnlyCollection<string> ConsumedInputKeys)
{
    public static ActivityStructureInputProjection Empty { get; } = new([], []);

    /// <summary>Returns ordinary unconsumed arguments and normalized structure arguments for authoring validation.</summary>
    public ArgumentState[] ApplyToInputs(IEnumerable<ArgumentState> ordinaryInputs) => ordinaryInputs
        .Where(input => !ConsumedInputKeys.Contains(input.ReferenceKey, StringComparer.Ordinal))
        .Concat(Inputs.Where(input => input.State is not null).Select(input => input.State! with { ReferenceKey = input.ReferenceKey }))
        .ToArray();
}

/// <summary>A structure-owned input, optionally declared by the ordinary activity catalog.</summary>
public sealed record ActivityStructureInput(
    string ReferenceKey,
    InputDefinition? Definition,
    ArgumentState? State);
