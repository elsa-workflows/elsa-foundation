using System.Text;

namespace Elsa.Foundation.Identity.Core.Authorization;

public enum PermissionRequirementMode
{
    Single,
    Any,
    All
}

public enum PermissionPolicyParseStatus
{
    NotPermission,
    Valid,
    MalformedReservedPolicy
}

public sealed record PermissionPolicyDescriptor
{
    private PermissionPolicyDescriptor(PermissionRequirementMode mode, IEnumerable<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        if (mode is not (PermissionRequirementMode.Single or PermissionRequirementMode.Any or PermissionRequirementMode.All))
            throw new ArgumentOutOfRangeException(nameof(mode));

        Mode = mode;
        Permissions = Array.AsReadOnly(permissions
            .Select(PermissionKey.Normalize)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray());

        if (Permissions.Count == 0)
            throw new ArgumentException("At least one permission is required.", nameof(permissions));

        if (mode == PermissionRequirementMode.Single && Permissions.Count != 1)
            throw new ArgumentException("A single permission requirement must contain exactly one permission.", nameof(permissions));
    }

    public PermissionRequirementMode Mode { get; }

    public IReadOnlyList<string> Permissions { get; }

    public static PermissionPolicyDescriptor Single(string permission) => new(PermissionRequirementMode.Single, [permission]);

    public static PermissionPolicyDescriptor Any(params string[] permissions) => new(PermissionRequirementMode.Any, permissions);

    public static PermissionPolicyDescriptor All(params string[] permissions) => new(PermissionRequirementMode.All, permissions);
}

public sealed record PermissionPolicyParseResult(
    PermissionPolicyParseStatus Status,
    PermissionPolicyDescriptor? Descriptor = null,
    bool IsLegacyAlias = false,
    string? Failure = null);

public interface IPermissionPolicyCodec
{
    string Format(PermissionPolicyDescriptor descriptor);

    PermissionPolicyParseResult Parse(string policyName);
}

public static class PermissionKey
{
    public const string Wildcard = "*";

    public static string Normalize(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        if (permission.Length == 0 || string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("Permission keys cannot be empty or whitespace.", nameof(permission));

        if (!string.Equals(permission, permission.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Permission keys cannot contain leading or trailing whitespace.", nameof(permission));

        return permission.Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }
}
