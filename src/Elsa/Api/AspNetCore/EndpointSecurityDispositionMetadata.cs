using System.Text.Json.Serialization;

namespace Elsa.Api.AspNetCore;

/// <summary>The primary security disposition published by an endpoint.</summary>
public enum EndpointSecurityDispositionKind
{
    Permission,
    Public,
    HostCredential,
    NamedPolicy
}

/// <summary>
/// Typed framework-neutral security metadata. Foundation permission metadata is represented by
/// <see cref="Permission"/>; the other forms make their non-permission intent explicit.
/// </summary>
public sealed record EndpointSecurityDispositionMetadata
{
    public EndpointSecurityDispositionMetadata(
        EndpointSecurityDispositionKind kind,
        string? value = null,
        string? owner = null,
        string? category = null,
        string? reason = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "An endpoint security disposition kind must be one of the defined primary dispositions.");

        Kind = kind;
        Value = Normalize(value, kind is EndpointSecurityDispositionKind.Permission or EndpointSecurityDispositionKind.NamedPolicy or EndpointSecurityDispositionKind.HostCredential);
        Owner = Normalize(owner, kind is EndpointSecurityDispositionKind.NamedPolicy or EndpointSecurityDispositionKind.HostCredential);
        Category = Normalize(category, kind == EndpointSecurityDispositionKind.Public);
        Reason = Normalize(reason, kind == EndpointSecurityDispositionKind.Public);

        if ((kind == EndpointSecurityDispositionKind.Public && (Value is not null || Owner is not null)) ||
            (kind != EndpointSecurityDispositionKind.Public && (Category is not null || Reason is not null)))
            throw new ArgumentException("Security-disposition fields do not match the selected kind.");
    }

    public EndpointSecurityDispositionKind Kind { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Value { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Owner { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Category { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; }

    public static EndpointSecurityDispositionMetadata Permission(string permission) =>
        new(EndpointSecurityDispositionKind.Permission, permission, null);

    public static EndpointSecurityDispositionMetadata Public(string category, string reason) =>
        new(EndpointSecurityDispositionKind.Public, category: category, reason: reason);

    public static EndpointSecurityDispositionMetadata HostCredential(string credential, string owner) =>
        new(EndpointSecurityDispositionKind.HostCredential, credential, owner);

    public static EndpointSecurityDispositionMetadata NamedPolicy(string policy, string owner) =>
        new(EndpointSecurityDispositionKind.NamedPolicy, policy, owner);

    private static string? Normalize(string? value, bool required)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (required && normalized is null)
            throw new ArgumentException("A non-empty security disposition value is required.");

        return normalized;
    }
}
