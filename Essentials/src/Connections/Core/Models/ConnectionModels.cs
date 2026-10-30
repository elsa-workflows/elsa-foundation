using System.Text.Json.Serialization;

namespace Elsa.Connections.Core.Models;

public sealed record ConnectionSettingField(
    string Name,
    string DisplayName,
    string EditorHint,
    bool Required = false);

public sealed record AuthSchemeField(
    string Name,
    string DisplayName,
    bool Sensitive,
    string EditorHint,
    bool Required = true);

public sealed record AuthSchemeDescriptor(
    string Name,
    string DisplayName,
    IReadOnlyList<AuthSchemeField> Fields,
    bool HasInteractiveAuthorization,
    bool HasRefreshableState,
    bool HasRevocation,
    bool RequiresWritableStore);

public sealed record AllowedHostsPolicy(string Kind, IReadOnlyList<string> Hosts)
{
    public const string AnyKind = "any";
    public const string FixedKind = "fixed";
    public const string UserDeclaredKind = "user-declared";

    public static AllowedHostsPolicy Any { get; } = new(AnyKind, []);

    public static AllowedHostsPolicy UserDeclared { get; } = new(UserDeclaredKind, []);

    public static AllowedHostsPolicy Fixed(params string[] hosts) => new(FixedKind, hosts);

    public bool Allows(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        if (string.Equals(Kind, AnyKind, StringComparison.Ordinal))
            return true;

        var host = requestUri.IdnHost;
        foreach (var pattern in Hosts)
        {
            if (HostMatches(host, pattern))
                return true;
        }

        return false;
    }

    private static bool HostMatches(string host, string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return false;

        var trimmed = pattern.Trim();
        if (trimmed.StartsWith("*.", StringComparison.Ordinal))
        {
            // Keep the leading dot so "*.example.com" matches "api.example.com" and not
            // "example.com", "evilexample.com", or "api.example.com.evil".
            var suffix = trimmed[1..];
            return host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(host, trimmed, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record ConnectionTypeDescriptor(
    string Name,
    string DisplayName,
    IReadOnlyList<string> SupportedSchemes,
    IReadOnlyList<ConnectionSettingField> Settings,
    AllowedHostsPolicy Hosts,
    bool RequiresWritableStore = false);

public sealed class ConnectionSecretField
{
    public string FieldName { get; set; } = "";
    public string SourceKind { get; set; } = ConnectionCredentialSourceKinds.Secret;
    public string? SecretName { get; set; }
    public bool IsOwnedSecret { get; set; }
    public string? NamedCredentialName { get; set; }
}

public sealed class ConnectionDefinition
{
    public string TenantId { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string AuthScheme { get; set; } = "";
    public IDictionary<string, string> Settings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, ConnectionSecretField> SecretFields { get; set; } = new Dictionary<string, ConnectionSecretField>(StringComparer.Ordinal);
    public string ManagedBy { get; set; } = ConnectionManagedBy.Studio;
    public string Owner { get; set; } = ConnectionOwners.Tenant;
    public ICollection<string> Tags { get; set; } = [];
}

public sealed class ConnectionState
{
    public string Status { get; set; } = ConnectionStatuses.Ready;
    public string? GrantSecretName { get; set; }
    public int? GrantSecretVersion { get; set; }
    public DateTimeOffset? LastTestedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class ConnectionDocument
{
    public ConnectionDefinition Definition { get; set; } = new();
    public ConnectionState State { get; set; } = new();
}

public sealed class ConnectionMetadata
{
    public string TenantId { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string AuthScheme { get; set; } = "";
    public IDictionary<string, string> Settings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> SecretFieldNames { get; set; } = [];
    public string ManagedBy { get; set; } = ConnectionManagedBy.Studio;
    public string Owner { get; set; } = ConnectionOwners.Tenant;
    public string Status { get; set; } = ConnectionStatuses.Ready;
    public DateTimeOffset? LastTestedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public ICollection<string> Tags { get; set; } = [];
}

public sealed class ConnectionQuery
{
    public string? Search { get; set; }
    public string? TypeName { get; set; }
    public string? AuthScheme { get; set; }
    public int PageSize { get; set; } = 50;
}

public sealed class CreateConnectionRequest
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string TypeName { get; set; } = ConnectionTypeNames.Http;
    public string AuthScheme { get; set; } = HttpAuthSchemes.Bearer;
    public IDictionary<string, string> Settings { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public IDictionary<string, ConnectionSecretFieldInput> SecretFields { get; set; } = new Dictionary<string, ConnectionSecretFieldInput>(StringComparer.Ordinal);
    public string Owner { get; set; } = ConnectionOwners.Tenant;
    public ICollection<string> Tags { get; set; } = [];
}

public sealed class ConnectionSecretFieldInput
{
    public string? SourceKind { get; set; }
    public string? ExistingSecretName { get; set; }
    public string? Value { get; set; }
    public string? NamedCredentialName { get; set; }
}

public sealed class UpdateConnectionRequest
{
    public string? DisplayName { get; set; }
    public IDictionary<string, string>? Settings { get; set; }
    public IDictionary<string, ConnectionSecretFieldInput>? SecretFields { get; set; }
    public ICollection<string>? Tags { get; set; }
}

public sealed class ConnectionTestResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = "";
}

public sealed class ConnectionResolveRequest
{
    public required string TenantId { get; init; }
    public required string Name { get; init; }
    public string? RequiredType { get; init; }
    public string Owner { get; init; } = ConnectionOwners.Tenant;
}

public sealed class ConnectionCredentialRequest
{
    public required string TenantId { get; init; }
    public required string ConnectionName { get; init; }
    public required string FieldName { get; init; }
    public required string SourceKind { get; init; }
    public string? SecretName { get; init; }
    public string? NamedCredentialName { get; init; }
    public string? Audience { get; init; }
}

public sealed record AccessToken(string TokenType, string Token, DateTimeOffset ExpiresOn)
{
    public override string ToString() => SensitiveValue.Redacted;
}

public sealed record AccessTokenRequest(string Name, string? Audience = null, IReadOnlyDictionary<string, string>? Properties = null);

public sealed class GrantWriteRequest
{
    public required string TenantId { get; init; }
    public required string ConnectionName { get; init; }
    public required string GrantSecretName { get; init; }
    public required int ExpectedSecretVersion { get; init; }
    public required string AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? TokenType { get; init; }
    public string? Scope { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GrantWriteStatus
{
    Written,
    Conflict,
    Missing
}

public sealed record GrantWriteResult(GrantWriteStatus Status, int? CurrentVersion);
