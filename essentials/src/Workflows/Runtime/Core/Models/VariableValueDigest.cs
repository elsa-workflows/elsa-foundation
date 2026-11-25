using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The value digest a variable version carries (spec 1221 FR-001, ADR 0092 decision 2): SHA-256 over a canonical JSON
/// form of the stored value, so two spellings of the same value (key order, number notation, string escaping) digest
/// the same. Computed once per applied write; a read never hashes.
/// </summary>
/// <remarks>
/// The canonical algorithm was extracted unchanged into JsonCanonicalForm. The enclosing hash now goes through
/// JsonHashInput (spec 1207 T023b); presence and external/withheld markers remain part of its identity.
/// </remarks>
internal static class VariableValueDigest
{
    private const string Prefix = "sha256:";

    public static string Compute(ValueEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("presence", (int)envelope.Presence);
            if (envelope.InlineValue is { } inline)
            {
                writer.WritePropertyName("inline");
                WriteCanonical(inline, writer);
            }

            if (envelope.ExternalReference is { } external)
            {
                writer.WritePropertyName("external");
                WriteCanonical(Elsa.Serialization.Core.JsonHashInput.SerializeToElement(external), writer);
            }

            if (envelope.WithheldValue is { } withheld)
            {
                writer.WritePropertyName("withheld");
                WriteCanonical(Elsa.Serialization.Core.JsonHashInput.SerializeToElement(withheld), writer);
            }

            writer.WriteEndObject();
        }

        return Prefix + Elsa.Serialization.Core.JsonHashInput.HashUtf8(stream.ToArray());
    }

    private static void WriteCanonical(JsonElement value, Utf8JsonWriter writer) =>
        writer.WriteRawValue(Elsa.Serialization.Core.JsonHashInput.CanonicalBytes(value), skipInputValidation: true);
}
