namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>A shell-wide activation channel that may be selected by workflow activations.</summary>
/// <remarks>
/// <see cref="Name"/> is an opaque identifier matched against an activation slot name using ordinal, case-sensitive
/// equality. It is not trimmed or normalized. Display metadata does not participate in activation identity.
/// </remarks>
public sealed class ActivationChannelDefinition
{
    /// <summary>The exact activation slot name accepted for this channel.</summary>
    public string Name { get; set; } = "";

    /// <summary>The human-readable channel name; when omitted, the identifier is suitable as a fallback.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Optional explanatory text for this channel.</summary>
    public string? Description { get; set; }
}
