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
            .Select(PermissionKey.NormalizeRequirement)
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

/// <summary>
/// The permission grammar, shared with Elsa 3.9: <c>{resource}:{verb}</c>. <c>:</c> separates the resource from the
/// verb, <c>/</c> separates a parent resource from a child (<c>workflows/runtime</c>), and <c>-</c> joins words inside one
/// resource segment or verb (<c>execution-evidence</c>). A <c>*</c> has meaning in three places only: the whole
/// resource (<c>*:read</c>), a trailing resource segment (<c>workflows/*:read</c> covers <c>workflows</c> and everything
/// beneath it), and the whole verb (<c>workflows/runtime:*</c>). A bare <c>*</c> is the superuser grant.
/// </summary>
public static class PermissionKey
{
    public const string Wildcard = "*";

    public const char ResourceSeparator = '/';

    public const char VerbSeparator = ':';

    public static string Normalize(string permission)
    {
        ArgumentNullException.ThrowIfNull(permission);

        if (permission.Length == 0 || string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("Permission keys cannot be empty or whitespace.", nameof(permission));

        if (!string.Equals(permission, permission.Trim(), StringComparison.Ordinal))
            throw new ArgumentException("Permission keys cannot contain leading or trailing whitespace.", nameof(permission));

        return permission.Normalize(NormalizationForm.FormC).ToUpperInvariant();
    }

    /// <summary>
    /// Normalizes a permission an endpoint or a caller requires: one concrete <c>{resource}:{verb}</c>, or the bare
    /// wildcard, which only the superuser grant satisfies. Anything else is a programming error, so it throws rather
    /// than quietly denying every caller.
    /// </summary>
    public static string NormalizeRequirement(string permission)
    {
        var canonical = Normalize(permission);
        if (canonical != Wildcard && !IsConcrete(canonical))
            throw new ArgumentException(
                $"Permission '{permission}' is not a '{{resource}}:{{verb}}' permission without wildcards.", nameof(permission));

        return canonical;
    }

    /// <summary>Whether <paramref name="permission"/> names exactly one permission: <c>{resource}:{verb}</c> with no <c>*</c>.</summary>
    public static bool IsConcrete(string permission) =>
        TryParse(permission, out var resource, out var verb) &&
        !resource.Contains(Wildcard, StringComparison.Ordinal) &&
        !verb.Contains(Wildcard, StringComparison.Ordinal);

    /// <summary>
    /// Splits a permission into its resource and verb. A bare <c>*</c> parses as resource <c>*</c> and verb <c>*</c>.
    /// Returns <c>false</c> for anything that is not one resource and one verb around a single <c>:</c>, has an empty
    /// resource segment, contains whitespace, or puts a <c>*</c> where it has no meaning; such a grant matches nothing.
    /// </summary>
    public static bool TryParse(string permission, out string resource, out string verb)
    {
        resource = verb = string.Empty;

        if (permission == Wildcard)
        {
            resource = verb = Wildcard;
            return true;
        }

        var separator = permission.IndexOf(VerbSeparator);
        if (separator <= 0 || separator == permission.Length - 1 || separator != permission.LastIndexOf(VerbSeparator) ||
            permission.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
            return false;

        var candidateResource = permission[..separator];
        var candidateVerb = permission[(separator + 1)..];
        if (!IsToken(candidateVerb, allowWildcard: true) || candidateVerb.Contains(ResourceSeparator))
            return false;

        var segments = candidateResource.Split(ResourceSeparator);
        var subtree = segments.Length > 1 && segments[^1] == Wildcard;
        var named = subtree ? segments[..^1] : segments;
        if (!(candidateResource == Wildcard || named.All(segment => IsToken(segment, allowWildcard: false))))
            return false;

        resource = candidateResource;
        verb = candidateVerb;
        return true;
    }

    private static bool IsToken(string value, bool allowWildcard) =>
        value.Length > 0 && ((allowWildcard && value == Wildcard) || !value.Contains(Wildcard, StringComparison.Ordinal));
}
