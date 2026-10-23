namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Design-facing description of one child slot inside an activity structure.
/// </summary>
public sealed record ActivityChildSlotDesignDescriptor(
    string Name,
    string Property,
    string DisplayName,
    string Cardinality,
    string? CollectionProperty = null,
    string? ChildProperty = null,
    string? LabelProperty = null,
    string? SlotNameTemplate = null);
