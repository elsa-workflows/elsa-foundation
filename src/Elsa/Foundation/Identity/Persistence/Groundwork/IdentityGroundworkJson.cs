using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Foundation.Identity.Persistence.Groundwork;

/// <summary>
/// The frozen serializer contract for identity documents. Mirrors the Secrets Groundwork options: Web
/// defaults plus a string enum converter so persisted enum values (user status, ownership, link policy,
/// membership status) survive as stable, human-readable strings rather than ordinals. Freezing these
/// options keeps the on-disk shape deterministic, which the golden-fixture drift test relies on.
/// </summary>
internal static class IdentityGroundworkJson
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ReadOnlyStringSetJsonConverter());
        return options;
    }
}
