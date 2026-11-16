namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Describes the authoring contexts in which an activity is offered. Each nonempty list is a
/// requirement; entries within a list are alternatives. These are presentation hints, not runtime
/// admission rules: existing authored nodes remain valid subject to their activity-owned validators.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ActivityPaletteAttribute : Attribute
{
    /// <summary>Modes of the immediate destination child slot, e.g. bpmn or flowchart.</summary>
    public string[] ContainerModes { get; init; } = [];
    /// <summary>Reference keys of the immediate destination child slot.</summary>
    public string[] SlotIds { get; init; } = [];
    /// <summary>Activity type keys of enclosing scopes, including the immediate container.</summary>
    public string[] AncestorActivityTypes { get; init; } = [];
    /// <summary>Authoring resource kinds, e.g. activity-definition-graph.</summary>
    public string[] ResourceKinds { get; init; } = [];
    /// <summary>Explains the intended context when inspecting an existing occurrence elsewhere.</summary>
    public string? Description { get; init; }
}
