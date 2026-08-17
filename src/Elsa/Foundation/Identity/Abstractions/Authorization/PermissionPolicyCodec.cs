using System.Text;

namespace Elsa.Foundation.Identity.Abstractions.Authorization;

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

    internal static PermissionPolicyDescriptor Create(PermissionRequirementMode mode, IEnumerable<string> permissions) => new(mode, permissions);
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

public sealed class PermissionPolicyCodec : IPermissionPolicyCodec
{
    public const string Prefix = "Elsa.Permission:";
    private const string Version = "v1:";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public string Format(PermissionPolicyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        var mode = descriptor.Mode switch
        {
            PermissionRequirementMode.Single => "s",
            PermissionRequirementMode.Any => "a",
            PermissionRequirementMode.All => "l",
            _ => throw new ArgumentOutOfRangeException(nameof(descriptor))
        };
        var tokens = string.Join('.', descriptor.Permissions.Select(Encode));
        return $"{Prefix}{Version}{mode}:{tokens}";
    }

    public PermissionPolicyParseResult Parse(string policyName)
    {
        if (string.IsNullOrEmpty(policyName) || !policyName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            return new(PermissionPolicyParseStatus.NotPermission);

        if (!policyName.StartsWith(Prefix, StringComparison.Ordinal))
            return Malformed("The permission policy namespace must use canonical casing.");

        var suffix = policyName[Prefix.Length..];
        if (suffix.StartsWith(Version, StringComparison.OrdinalIgnoreCase) &&
            !suffix.StartsWith(Version, StringComparison.Ordinal))
            return Malformed("The permission policy version must use canonical casing.");

        if (!suffix.StartsWith(Version, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return new(PermissionPolicyParseStatus.Valid, PermissionPolicyDescriptor.Single(suffix), true);
            }
            catch (ArgumentException exception)
            {
                return new(PermissionPolicyParseStatus.MalformedReservedPolicy, Failure: exception.Message);
            }
        }

        var payload = suffix[Version.Length..];
        if (payload.Length < 3 || payload[1] != ':')
            return Malformed("The v1 policy payload is incomplete.");

        var mode = payload[0] switch
        {
            's' => PermissionRequirementMode.Single,
            'a' => PermissionRequirementMode.Any,
            'l' => PermissionRequirementMode.All,
            _ => (PermissionRequirementMode?)null
        };
        if (mode is null)
            return Malformed("The v1 policy mode is invalid.");

        var encodedTokens = payload[2..].Split('.', StringSplitOptions.None);
        if (encodedTokens.Length == 0 || encodedTokens.Any(string.IsNullOrEmpty))
            return Malformed("The v1 policy must contain non-empty permission tokens.");

        var permissions = new List<string>(encodedTokens.Length);
        foreach (var token in encodedTokens)
        {
            if (!TryDecode(token, out var permission, out var failure))
                return Malformed(failure);
            permissions.Add(permission);
        }

        if (mode == PermissionRequirementMode.Single && permissions.Count != 1)
            return Malformed("A single policy must contain exactly one token.");

        var canonical = permissions.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (!permissions.SequenceEqual(canonical, StringComparer.Ordinal))
            return Malformed("Permission tokens must be unique and sorted by canonical key.");

        return new(PermissionPolicyParseStatus.Valid, PermissionPolicyDescriptor.Create(mode.Value, permissions));
    }

    private static PermissionPolicyParseResult Malformed(string? failure) =>
        new(PermissionPolicyParseStatus.MalformedReservedPolicy, Failure: failure);

    private static string Encode(string permission)
    {
        var value = Convert.ToBase64String(StrictUtf8.GetBytes(permission));
        return value.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static bool TryDecode(string token, out string permission, out string? failure)
    {
        permission = string.Empty;
        failure = null;

        if (token.Contains('=') || token.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '-' or '_')))
        {
            failure = "Permission tokens must be unpadded base64url.";
            return false;
        }

        var base64 = token.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => "!"
        };

        try
        {
            permission = StrictUtf8.GetString(Convert.FromBase64String(base64));
            var canonical = PermissionKey.Normalize(permission);
            if (!string.Equals(permission, canonical, StringComparison.Ordinal) || !string.Equals(token, Encode(canonical), StringComparison.Ordinal))
            {
                failure = "The decoded permission token is not canonical.";
                return false;
            }

            return true;
        }
        catch (Exception exception) when (exception is FormatException or DecoderFallbackException or ArgumentException)
        {
            failure = "The permission token is not canonical UTF-8 base64url.";
            return false;
        }
    }
}
