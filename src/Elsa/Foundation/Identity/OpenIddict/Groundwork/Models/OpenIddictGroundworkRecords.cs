using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;

/// <summary>Shared persistence metadata for the four OpenIddict record kinds.</summary>
public abstract class OpenIddictGroundworkRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonRequired]
    public string ConcurrencyToken { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Groundwork's authoritative envelope version. It is deliberately absent
    /// from canonical JSON and never exposed as OpenIddict's concurrency token.
    /// </summary>
    [JsonIgnore]
    public long PersistenceVersion { get; internal set; }
}

public sealed class OpenIddictGroundworkApplication : OpenIddictGroundworkRecord
{
    public string? ApplicationType { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? ClientType { get; set; }
    public string? ConsentType { get; set; }
    public string? DisplayName { get; set; }
    public SortedDictionary<string, string> DisplayNames { get; set; } = new(StringComparer.Ordinal);
    public string? JsonWebKeySet { get; set; }
    public string[] Permissions { get; set; } = [];
    public string[] PostLogoutRedirectUris { get; set; } = [];
    public SortedDictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public string[] RedirectUris { get; set; } = [];
    public string[] Requirements { get; set; } = [];
    public SortedDictionary<string, string> Settings { get; set; } = new(StringComparer.Ordinal);
}

public sealed class OpenIddictGroundworkAuthorization : OpenIddictGroundworkRecord
{
    public string? ApplicationId { get; set; }
    public DateTimeOffset? CreationDate { get; set; }
    public SortedDictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public string[] Scopes { get; set; } = [];
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
}

public sealed class OpenIddictGroundworkScope : OpenIddictGroundworkRecord
{
    public string? Description { get; set; }
    public SortedDictionary<string, string> Descriptions { get; set; } = new(StringComparer.Ordinal);
    public string? DisplayName { get; set; }
    public SortedDictionary<string, string> DisplayNames { get; set; } = new(StringComparer.Ordinal);
    public string? Name { get; set; }
    public SortedDictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public string[] Resources { get; set; } = [];
}

public sealed class OpenIddictGroundworkToken : OpenIddictGroundworkRecord
{
    public string? ApplicationId { get; set; }
    public string? AuthorizationId { get; set; }
    public DateTimeOffset? CreationDate { get; set; }
    public DateTimeOffset? ExpirationDate { get; set; }
    public string? Payload { get; set; }
    public SortedDictionary<string, JsonElement> Properties { get; set; } = new(StringComparer.Ordinal);
    public DateTimeOffset? RedemptionDate { get; set; }
    public string? ReferenceId { get; set; }
    public string? Status { get; set; }
    public string? Subject { get; set; }
    public string? Type { get; set; }
}
