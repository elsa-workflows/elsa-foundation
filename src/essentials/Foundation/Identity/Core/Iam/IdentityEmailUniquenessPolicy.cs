namespace Elsa.Foundation.Identity.Core.Iam;

/// <summary>Provider-neutral authority policy controlling tenant-scoped email uniqueness.</summary>
public interface IIdentityEmailUniquenessPolicy
{
    bool RequireUniqueEmail { get; }
}

public sealed record IdentityEmailUniquenessPolicy(bool RequireUniqueEmail) : IIdentityEmailUniquenessPolicy
{
    public static IdentityEmailUniquenessPolicy NonUnique { get; } = new(false);
    public static IdentityEmailUniquenessPolicy Unique { get; } = new(true);
}
