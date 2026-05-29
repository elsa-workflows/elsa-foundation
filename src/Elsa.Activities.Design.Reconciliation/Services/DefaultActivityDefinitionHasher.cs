using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Reconciliation.Core;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Activities.Design.Reconciliation.Services;

/// <summary>
/// Default implementation of <see cref="IActivityDefinitionHasher"/>. Computes a SHA-256
/// hash over a canonicalised JSON serialisation of the (definition, version) pair —
/// "canonical" meaning every object's properties are sorted alphabetically (ordinal) at
/// every level. Native .NET only: no third-party canonicalisation library.
/// </summary>
/// <remarks>
/// Excluded from the hash: <c>LastModifiedAt</c> (mutates on every save), reconciliation-state
/// fields (live on the sibling, not the definition), and any nested versions on the parent
/// (the version is the second argument and is hashed explicitly). The hash is deterministic
/// across structurally-equivalent inputs regardless of how the serialiser ordered them.
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
    // ReconciledAt/ReconciledBy are deliberately excluded — they're operational provenance
    // that shifts each pass (UtcNow on the incoming candidate, frozen on the persisted row);
    // including them would prevent the second-pass hash from matching the first, defeating
    // the hasher's purpose. Id is also out (random per pass).
    private static object ProjectDefinition(IActivityDefinition d) => new
    {
        d.ActivityTypeKey,
        d.Category,
        d.DisplayName,
        d.Description,
    };

    private static object ProjectVersion(IActivityDefinitionVersion v) => new
    {
        v.Version,
        v.ImplementationKind,
        v.ImplementationDescriptor,
        v.ExecutionType,
        v.Inputs,
        v.Outputs,
        v.Ports,
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
