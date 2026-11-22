using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// Canonicalization helpers that make git's on-disk file a pure-whitespace rendering of the deterministic
/// payload serialization, so content identity (a hash) is stable across the compact ↔ indented forms.
///
/// The on-disk file is the <em>indented</em> canonical state; content identity is the SHA-256 of the
/// <em>compact</em> canonical form. "Strip whitespace" is done by re-parsing to a <see cref="JsonNode"/>
/// and re-emitting compact — a structural transform that preserves member order (the serializer is
/// deterministic) and, crucially, whitespace <em>inside</em> string values, which a naive byte-level
/// strip would corrupt.
/// </summary>
public static class GitCanonicalJson
{
    /// <summary>The compact canonical serialization of <paramref name="state"/> — the hash preimage.</summary>
    public static string ToCompact(WorkflowDefinitionState state, IPayloadSerializer serializer) =>
        serializer.Serialize(state);

    /// <summary>The indented on-disk rendering of a compact canonical string (pure-whitespace transform).</summary>
    public static string Indent(string compact) => RenderOuterDocument(compact, indented: true);

    /// <summary>The compact canonical form recovered from an on-disk (indented) file's text.</summary>
    public static string CompactFromFile(string fileText) => RenderOuterDocument(fileText, indented: false);

    /// <summary>Lowercase hex SHA-256 of a canonical (compact) string.</summary>
    public static string Sha256Hex(string compact) =>
        JsonHashInput.HashJson(compact);

    /// <summary>The content hash of a file's canonical form (convenience for the source).</summary>
    public static string HashFile(string fileText) => JsonHashInput.HashJson(fileText);

    private static string RenderOuterDocument(string json, bool indented)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 1000 });
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = indented, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                writer.WriteStartObject();
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    writer.WritePropertyName(property.Name);
                    // The serializer already rendered owned wrappers. Never re-encode a carried value to indent it.
                    writer.WriteRawValue(property.Value.GetRawText(), skipInputValidation: true);
                }
                writer.WriteEndObject();
            }
            else writer.WriteRawValue(document.RootElement.GetRawText(), skipInputValidation: true);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
