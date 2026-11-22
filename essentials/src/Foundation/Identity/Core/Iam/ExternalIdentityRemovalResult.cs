namespace Elsa.Foundation.Identity.Core.Iam;

public enum ExternalIdentityRemovalStatus { Removed, Missing, Conflict }

/// <summary>Committed qualified-link removal outcome. A conflict never reports that a different link instance was removed.</summary>
public sealed class ExternalIdentityRemovalResult(ExternalIdentityRemovalStatus status, string? userRevision = null)
{
    public ExternalIdentityRemovalStatus Status { get; } = status;
    public string? UserRevision { get; } = userRevision;
}
