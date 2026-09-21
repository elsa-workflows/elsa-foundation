using Elsa.Activities.Design.Core.Contracts;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Activities.Design.Persistence.Core.Services;

/// <summary>
/// Default implementation of <see cref="IActivityDefinitionHasher"/>. Computes a SHA-256
/// hash over a canonicalised JSON serialisation of the (definition, version) pair —
/// "canonical" meaning every object's properties are sorted alphabetically (ordinal) at
/// every level. Native .NET only: no third-party canonicalisation library.
/// </summary>
/// <remarks>
/// This is the sanctioned exception to the "all JSON via IPayloadSerializer" rule: hashing needs a
/// canonical sorted-key serialisation that the payload serializer does not produce, and the JSON is
/// consumed only here (it is never persisted or read by another component — only the SHA-256 is).
/// Excluded from the hash: <c>LastModifiedAt</c>/<c>CreatedAt</c>/<c>RowNumber</c>, provenance,
/// identity fields such as <c>Id</c> and <c>Version</c>, and any nested versions on the parent.
/// </remarks>
public sealed class DefaultActivityDefinitionHasher : IActivityDefinitionHasher
{
    private static readonly HashSet<string> ExcludedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Primitives.Entities.Entity.LastModifiedAt),
        nameof(Primitives.Entities.Entity.CreatedAt),
        nameof(Primitives.Entities.Entity.RowNumber),
    };
    private static readonly JsonSerializerOptions options = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public string Hash(IActivityDefinition definition, IActivityDefinitionVersion version)
    {
        var payload = new
        {
            Definition = ProjectDefinition(definition),
            Version = ProjectVersion(version),
        };

        var json = JsonSerializer.Serialize(payload, options);

        var canonical = Canonicalise(json);
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var digest = SHA256.HashData(bytes);

        return Convert.ToHexString(digest);
    }

    // Projects the definition to a plain object containing only the content-bearing fields.
    // Provenance (SourceKind/SourceId) is deliberately excluded — it identifies where a row came
    // from, not what it contains; including it would let a re-source defeat duplicate detection.
    private static object ProjectDefinition(IActivityDefinition d) => new
    {
        d.ActivityTypeKey,
        d.Category,
        d.DisplayName,
        d.Description,
    };

    // Version is deliberately excluded here: it identifies the version row, not its projected content.
    private static object ProjectVersion(IActivityDefinitionVersion v) => new
    {
        v.ProviderKey,
        v.ProviderSchemaVersion,
        v.ConsumerKey,
        v.ConsumerSchemaVersion,
        v.DescriptorPayload,
        v.ExecutionType,
        v.Inputs,
        v.Outputs,
        v.DesignFacets,
    };

    private static string Canonicalise(string json)
    {
        var node = JsonNode.Parse(json)
            ?? throw new InvalidOperationException("Hasher: input JSON parsed to null.");
        var sorted = SortNode(node);
        return sorted.ToJsonString();
    }

    private static JsonNode SortNode(JsonNode node)
    {
        return node switch
        {
            JsonObject obj => SortObject(obj),
            JsonArray arr => SortArray(arr),
            _ => node.DeepClone(),
        };
    }

    private static JsonObject SortObject(JsonObject src)
    {
        var dest = new JsonObject();
        foreach (var key in src.Select(kv => kv.Key)
                                .Where(k => !ExcludedPropertyNames.Contains(k))
                                .OrderBy(k => k, StringComparer.Ordinal))
        {
            var value = src[key];
            dest[key] = value is null ? null : SortNode(value);
        }
        return dest;
    }

    private static JsonArray SortArray(JsonArray src)
    {
        var dest = new JsonArray();
        foreach (var item in src)
            dest.Add(item is null ? null : SortNode(item));
        return dest;
    }
}
