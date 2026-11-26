using System.Text;
using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// Renders the outer Git document while writing every carried child value raw. Content identity uses
/// JsonHashInput's exact canonical JSON value, independently of the file's indentation, member order,
/// escape spelling and number notation. Rendering does not replace data with that hash preimage.
/// </summary>
public static class GitCanonicalJson
{
    /// <summary>The owner's compact serialization of <paramref name="state"/>, retaining raw data values.</summary>
    public static string ToCompact(WorkflowDefinitionState state, IPayloadSerializer serializer) =>
        serializer.Serialize(state);

    /// <summary>The indented on-disk rendering of a compact canonical string (pure-whitespace transform).</summary>
    public static string Indent(string compact) => RenderOuterDocument(compact, indented: true);

    /// <summary>The compact outer rendering recovered from a file, retaining its carried data text.</summary>
    public static string CompactFromFile(string fileText) => RenderOuterDocument(fileText, indented: false);

    /// <summary>Lowercase hex SHA-256 of the exact canonical JSON value represented by the text.</summary>
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
