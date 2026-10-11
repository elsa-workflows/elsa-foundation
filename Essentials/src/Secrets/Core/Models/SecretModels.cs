using System.Text.Json.Serialization;
using Elsa.Primitives.Persistence;

namespace Elsa.Secrets.Core.Models;

public static class SecretExpressionTypes
{
    public const string Secret = "Secret";
}

public static class SecretInputUIHints
{
    public const string SecretPicker = "secret-picker";
}

public static class SecretStoreNames
{
    public const string Encrypted = "encrypted";
    public const string Configuration = "configuration";
}

public static class SecretTypeNames
{
    public const string Text = "text";
    public const string RsaKey = "rsa-key";
    public const string X509Certificate = "x509-certificate";
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SecretStatus
{
    Active,
    Retired,
    Expired,
    Revoked,
    Deleted
}

[Flags]
public enum SecretStoreCapabilities
{
    None = 0,
    Read = 1,
    Write = 2,
    Delete = 4,
    Test = 8,
    ExportEncrypted = 16,
    Versioned = 32
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SecretResolutionFailureCode
{
    None,
    NotFound,
    Inactive,
    Expired,
    Revoked,
    Deleted,
    TypeMismatch,
    ScopeMismatch,
    StoreUnavailable,
    Unauthorized,
    CorruptState
}

public sealed record SecretReference(string Name, string? TypeName = null, string? Scope = null);

public sealed record SecretStoreDescriptor(
    string Name,
    string DisplayName,
    string Description,
    SecretStoreCapabilities Capabilities,
    bool IsReadOnly);

public sealed record SecretTypeDescriptor(
    string Name,
    string DisplayName,
    string Description,
    string EditorHint,
    IReadOnlyCollection<string> SupportedStoreNames);

public sealed class SecretMetadata
{
    public string Id { get; set; } = "";
    public string TenantId { get; set; } = "";
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string TypeName { get; set; } = SecretTypeNames.Text;
    public string StoreName { get; set; } = SecretStoreNames.Encrypted;
    public string? Scope { get; set; }
    public ICollection<string> Tags { get; set; } = [];
    public SecretStatus Status { get; set; } = SecretStatus.Active;
    public int? CurrentVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

public sealed class CreateSecretRequest
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string TypeName { get; set; } = SecretTypeNames.Text;
    public string StoreName { get; set; } = SecretStoreNames.Encrypted;
    public string? Scope { get; set; }
    public ICollection<string> Tags { get; set; } = [];
    public string? Value { get; set; }
    public string? ConfigurationKey { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class UpdateSecretMetadataRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}

public sealed class RotateSecretRequest
{
    public string? Value { get; set; }
    public string? ConfigurationKey { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed class SecretQuery
{
    public string? Search { get; set; }
    public string? TypeName { get; set; }
    public ICollection<string> TypeNames { get; set; } = [];
    public string? StoreName { get; set; }
    public ICollection<string> StoreNames { get; set; } = [];
    public string? Scope { get; set; }
    public SecretStatus? Status { get; set; }
    public bool ActiveOnly { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}

public sealed class ListSecretsResponse
{
    public ICollection<SecretMetadata> Items { get; set; } = [];
    public long TotalCount { get; set; }

    public static ListSecretsResponse FromPage(Page<SecretMetadata> page) => new()
    {
        Items = page.Items,
        TotalCount = page.TotalCount
    };
}

public sealed class SecretDescriptorsResponse
{
    public ICollection<SecretTypeDescriptor> Types { get; set; } = [];
    public ICollection<SecretStoreDescriptor> Stores { get; set; } = [];
}

public sealed class SecretPickerRequest
{
    public string? Search { get; set; }
    public ICollection<string> TypeNames { get; set; } = [];
    public ICollection<string> StoreNames { get; set; } = [];
    public string? Scope { get; set; }
    public bool ActiveOnly { get; set; } = true;
}

public sealed class SecretPickerResponse
{
    public ICollection<SecretMetadata> Items { get; set; } = [];
    public bool CanCreateInline { get; set; }
}

public sealed class SecretTestResult
{
    public bool Succeeded { get; set; }
    public string Code { get; set; } = "ok";
    public string? Message { get; set; }

    public static SecretTestResult Success() => new() { Succeeded = true, Code = "ok", Message = "Secret resolved successfully." };

    public static SecretTestResult Failure(string code, string message) => new() { Succeeded = false, Code = code, Message = message };
}

public sealed class SecretValidationResult
{
    public bool Succeeded { get; set; }
    public string? Error { get; set; }

    public static SecretValidationResult Success() => new() { Succeeded = true };

    public static SecretValidationResult Failure(string error) => new() { Succeeded = false, Error = error };
}
