using System.Collections;
using System.Security.Cryptography;
using System.Text;
using Elsa.Modularity.Planning.Models;

namespace Elsa.Modularity.Planning.Json;

/// <summary>
/// RFC 8785 canonicalization for the v1 selection catalog's string/array/object-only semantic projection.
/// This is deliberately not a general-purpose JCS writer for arbitrary JSON numbers.
/// </summary>
public static class SelectionDigest
{
    private static readonly UTF8Encoding s_utf8 = new(false, true);

    public static string ComputeDefinitionDigest(SelectionDefinition definition) =>
        Hash(DefinitionProjection(definition, includeDigest: false));

    public static string ComputeCatalogDigest(SelectionCatalog catalog)
    {
        var profiles = catalog.Profiles
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Version, StringComparer.Ordinal)
            .Select(x => (object)DefinitionProjection(x, includeDigest: true))
            .ToArray();
        var groups = catalog.Groups
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Version, StringComparer.Ordinal)
            .Select(x => (object)DefinitionProjection(x, includeDigest: true))
            .ToArray();

        return Hash(new Dictionary<string, object>
        {
            ["schemaVersion"] = catalog.SchemaVersion,
            ["id"] = catalog.Id,
            ["version"] = catalog.Version,
            ["publisher"] = catalog.Publisher,
            ["profiles"] = profiles,
            ["groups"] = groups
        });
    }

    public static string CanonicalDefinition(SelectionDefinition definition) => Canonicalize(DefinitionProjection(definition, includeDigest: false));

    private static Dictionary<string, object> DefinitionProjection(SelectionDefinition definition, bool includeDigest)
    {
        var members = definition.Members.OrderBy(x => x, StringComparer.Ordinal).Cast<object>().ToArray();
        var explanations = definition.DependencyExplanations
            .OrderBy(x => x.FeatureId, StringComparer.Ordinal)
            .ThenBy(x => x.DependencyId, StringComparer.Ordinal)
            .ThenBy(x => x.Mode, StringComparer.Ordinal)
            .ThenBy(x => x.Reason, StringComparer.Ordinal)
            .Select(x => (object)new Dictionary<string, object>
            {
                ["featureId"] = x.FeatureId,
                ["dependencyId"] = x.DependencyId,
                ["mode"] = x.Mode,
                ["reason"] = x.Reason
            })
            .ToArray();

        var projection = new Dictionary<string, object>
        {
            ["kind"] = definition.Kind,
            ["id"] = definition.Id,
            ["version"] = definition.Version,
            ["members"] = members,
            ["rationale"] = definition.Rationale,
            ["title"] = definition.Title,
            ["description"] = definition.Description,
            ["dependencyExplanations"] = explanations
        };
        if (includeDigest)
            projection["digest"] = definition.Digest;
        return projection;
    }

    private static string Hash(object value)
    {
        var bytes = s_utf8.GetBytes(Canonicalize(value));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Canonicalize(object value)
    {
        var builder = new StringBuilder();
        WriteValue(builder, value);
        return builder.ToString();
    }

    private static void WriteValue(StringBuilder builder, object value)
    {
        switch (value)
        {
            case string text:
                WriteString(builder, text);
                break;
            case IReadOnlyDictionary<string, object> properties:
                builder.Append('{');
                var first = true;
                foreach (var pair in properties.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    if (!first)
                        builder.Append(',');
                    first = false;
                    WriteString(builder, pair.Key);
                    builder.Append(':');
                    WriteValue(builder, pair.Value);
                }
                builder.Append('}');
                break;
            case IEnumerable elements:
                builder.Append('[');
                var firstElement = true;
                foreach (var element in elements)
                {
                    if (!firstElement)
                        builder.Append(',');
                    firstElement = false;
                    WriteValue(builder, element ?? throw new SelectionDocumentException("invalid-field", "A digest projection cannot contain null."));
                }
                builder.Append(']');
                break;
            default:
                throw new SelectionDocumentException("invalid-field", "The v1 digest projection accepts only strings, arrays, and objects.");
        }
    }

    private static void WriteString(StringBuilder builder, string value)
    {
        builder.Append('"');
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            switch (ch)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\t': builder.Append("\\t"); break;
                case '\n': builder.Append("\\n"); break;
                case '\f': builder.Append("\\f"); break;
                case '\r': builder.Append("\\r"); break;
                default:
                    if (ch < 0x20)
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4"));
                    }
                    else if (char.IsHighSurrogate(ch))
                    {
                        if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                            throw new SelectionDocumentException("invalid-unicode", "A digest string contains an unpaired surrogate.");
                        builder.Append(ch);
                        builder.Append(value[index]);
                    }
                    else if (char.IsLowSurrogate(ch))
                        throw new SelectionDocumentException("invalid-unicode", "A digest string contains an unpaired surrogate.");
                    else
                        builder.Append(ch);
                    break;
            }
        }
        builder.Append('"');
    }
}
