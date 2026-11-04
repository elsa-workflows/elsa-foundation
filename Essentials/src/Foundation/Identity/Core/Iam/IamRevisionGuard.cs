namespace Elsa.Foundation.Identity.Core.Iam;

/// <summary>The authority root whose revision is part of a coordinated provider-configuration write.</summary>
public enum IamRevisionGuardKind { User, Role }

/// <summary>A bounded, opaque IAM snapshot guard. Successful coordination advances this root's revision atomically.</summary>
public sealed class IamRevisionGuard
{
    public IamRevisionGuard(IamRevisionGuardKind kind, string tenantId, string id, string revision)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrEmpty(tenantId);
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentException.ThrowIfNullOrEmpty(revision);
        if (tenantId.Length > 400 || id.Length > 400 || revision.Length > 128)
            throw new ArgumentException("An IAM revision guard exceeds its bound.");
        Kind = kind; TenantId = tenantId; Id = id; Revision = revision;
    }

    public IamRevisionGuardKind Kind { get; }
    public string TenantId { get; }
    public string Id { get; }
    public string Revision { get; }
}
