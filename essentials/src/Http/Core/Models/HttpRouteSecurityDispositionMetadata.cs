namespace Elsa.Http.Core.Models;

/// <summary>The primary security classification for a workflow HTTP route.</summary>
public enum HttpRouteSecurityDispositionKind
{
    Permission,
    Public,
    HostCredential,
    /// <summary>
    /// Uses the owning host/integration authorization model. Values name policies when present; an empty value set
    /// means the established authenticated-principal/default-policy behavior without inventing a policy name.
    /// </summary>
    HostPolicy
}

/// <summary>
/// Immutable, inspectable security disposition for a dynamic route. A single record is the primary disposition;
/// host-policy values may contain more than one distinct named policy when several waiting workflow bookmarks share the
/// same route and the request path must remain fail-closed.
/// </summary>
public sealed record HttpRouteSecurityDispositionMetadata
{
    public HttpRouteSecurityDispositionMetadata(
        HttpRouteSecurityDispositionKind kind,
        IEnumerable<string>? values = null,
        string? ownerId = null,
        string? category = null,
        string? reason = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A route security disposition kind must be defined.");

        Values = NormalizeValues(values, kind is HttpRouteSecurityDispositionKind.Permission or HttpRouteSecurityDispositionKind.HostCredential);
        OwnerId = Normalize(ownerId, kind is HttpRouteSecurityDispositionKind.HostPolicy or HttpRouteSecurityDispositionKind.HostCredential);
        Category = Normalize(category, kind == HttpRouteSecurityDispositionKind.Public);
        Reason = Normalize(reason, kind == HttpRouteSecurityDispositionKind.Public);

        if (kind == HttpRouteSecurityDispositionKind.Public && (Values.Count > 0 || OwnerId is not null) ||
            kind != HttpRouteSecurityDispositionKind.Public && (Category is not null || Reason is not null))
            throw new ArgumentException("Security-disposition fields do not match the selected kind.");

        Kind = kind;
    }

    public HttpRouteSecurityDispositionKind Kind { get; }
    public IReadOnlyList<string> Values { get; }
    public string? OwnerId { get; }
    public string? Category { get; }
    public string? Reason { get; }

    public string? Value => Values.Count == 0 ? null : Values[0];

    public static HttpRouteSecurityDispositionMetadata Permission(string permission, string? ownerId = null) =>
        new(HttpRouteSecurityDispositionKind.Permission, [permission], ownerId);

    public static HttpRouteSecurityDispositionMetadata Public(string category, string reason) =>
        new(HttpRouteSecurityDispositionKind.Public, category: category, reason: reason);

    public static HttpRouteSecurityDispositionMetadata AuthenticatedPrincipal(string? ownerId = null) =>
        new(HttpRouteSecurityDispositionKind.HostPolicy, ownerId: ownerId ?? "Elsa.Http");

    public static HttpRouteSecurityDispositionMetadata HostCredential(string credential, string ownerId) =>
        new(HttpRouteSecurityDispositionKind.HostCredential, [credential], ownerId);

    public static HttpRouteSecurityDispositionMetadata NamedPolicy(string policy, string ownerId) =>
        new(HttpRouteSecurityDispositionKind.HostPolicy, [policy], ownerId);

    public static HttpRouteSecurityDispositionMetadata NamedPolicies(IEnumerable<string> policies, string ownerId) =>
        new(HttpRouteSecurityDispositionKind.HostPolicy, policies, ownerId);

    private static IReadOnlyList<string> NormalizeValues(IEnumerable<string>? values, bool required)
    {
        var normalized = (values ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (required && normalized.Length == 0)
            throw new ArgumentException("A route security disposition requires a non-empty value.", nameof(values));
        return Array.AsReadOnly(normalized);
    }

    private static string? Normalize(string? value, bool required)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (required && normalized is null)
            throw new ArgumentException("A route security disposition requires a non-empty value.");
        return normalized;
    }
}
