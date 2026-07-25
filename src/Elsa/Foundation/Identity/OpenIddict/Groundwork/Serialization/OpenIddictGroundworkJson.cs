using System.Globalization;
using Groundwork.Documents.Serialization;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;

/// <summary>
/// Owns the version-admission policy for OpenIddict's four durable record kinds.
/// Record content remains adapter-private; callers never deserialize an arbitrary
/// OpenIddict document outside this policy.
/// </summary>
public static class OpenIddictGroundworkJson
{
    public const string ApplicationDocumentKind = "openiddict-application";
    public const string AuthorizationDocumentKind = "openiddict-authorization";
    public const string ScopeDocumentKind = "openiddict-scope";
    public const string TokenDocumentKind = "openiddict-token";

    public static IReadOnlyList<DocumentSchemaVersionPolicy> Policies { get; } =
    [
        CreateApplicationPolicy(),
        CreateAuthorizationPolicy(),
        CreateScopePolicy(),
        CreateTokenPolicy()
    ];

    // No historical OpenIddict Groundwork content exists. Upcasters are deliberately
    // empty until a future schema version has a real predecessor to transform.
    public static IReadOnlyList<IDocumentJsonUpcaster> Upcasters { get; } = [];

    public static DocumentSchemaVersionPolicy CreateApplicationPolicy() =>
        new(ApplicationDocumentKind, minimumReadableVersion: 1, currentVersion: 1);

    public static DocumentSchemaVersionPolicy CreateAuthorizationPolicy() =>
        new(AuthorizationDocumentKind, minimumReadableVersion: 1, currentVersion: 1);

    public static DocumentSchemaVersionPolicy CreateScopePolicy() =>
        new(ScopeDocumentKind, minimumReadableVersion: 1, currentVersion: 1);

    public static DocumentSchemaVersionPolicy CreateTokenPolicy() =>
        new(TokenDocumentKind, minimumReadableVersion: 1, currentVersion: 1);

    public static VersionedJsonDocumentCodec CreateCodec() =>
        new(Policies, Upcasters, new DocumentSchemaVersionFormat(ParseVersion, FormatVersion));

    private static int? ParseVersion(string _, string stamp) =>
        stamp.StartsWith('v') &&
        int.TryParse(stamp.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            ? version
            : null;

    private static string FormatVersion(string _, int version) => $"v{version}";
}
