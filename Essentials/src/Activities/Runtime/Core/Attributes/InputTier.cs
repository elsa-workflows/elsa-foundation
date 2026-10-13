namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Presentation-only grouping for an activity input in the designer. It does not change
/// requiredness, browsability, bindings, or the catalog content hash.
/// </summary>
/// <remarks>
/// The authoring catalog exposes the value as the camel-case string <c>essential</c>,
/// <c>optional</c>, or <c>setting</c>. When unset, the catalog field is <see langword="null"/> and
/// the client applies its own default. Studio supplies the shared engine properties (display name,
/// commit strategy, and the like) as settings; they are not declared here.
/// Members start at 1 because a named attribute argument cannot be a nullable enum: the CLR
/// default <c>0</c> means "not declared" and is not a catalog value.
/// </remarks>
public enum InputTier
{
    /// <summary>Parameters tab, always visible, in descriptor order.</summary>
    Essential = 1,

    /// <summary>Parameters tab, only after the author adds it (or a reveal toggle opens its group).</summary>
    Optional = 2,

    /// <summary>Settings tab: engine or behavioural knobs specific to this activity.</summary>
    Setting = 3
}
