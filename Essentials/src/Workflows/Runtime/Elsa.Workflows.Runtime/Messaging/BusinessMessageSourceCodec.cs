using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Messaging;

namespace Elsa.Workflows.Runtime.Messaging;

internal static class BusinessMessageSourceCodec
{
    internal const string FingerprintVersion = "elsa.business-message-source/v1";
    private const string ProgressVersion = "elsa.business-message-progress/v1";

    internal static string Encode(BusinessMessageSourceState source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return BusinessMessageWire.Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("version", BusinessMessageSourceState.Version);
            writer.WritePropertyName("header"); writer.WriteRawValue(BusinessMessageAcceptedHeaderCodec.Encode(source.Header));
            writer.WritePropertyName("payloadSlot"); writer.WriteRawValue(BusinessMessageAcceptedHeaderCodec.EncodePayloadSlot(source.PayloadSlot));
            writer.WritePropertyName("progress"); writer.WriteStartArray();
            foreach (var item in source.Progress) WriteProgress(writer, item);
            writer.WriteEndArray(); writer.WriteEndObject();
        });
    }
    internal static BusinessMessageSourceState Decode(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
            var root = BusinessMessageWire.Object(document.RootElement, ["version", "header", "payloadSlot", "progress"]);
            if (BusinessMessageWire.Int(root["version"]) != BusinessMessageSourceState.Version)
                throw new NotSupportedException("The BusinessMessage source version is unsupported.");
            var header = BusinessMessageAcceptedHeaderCodec.Decode(root["header"].GetRawText());
            var payload = BusinessMessageAcceptedHeaderCodec.DecodePayloadSlot(root["payloadSlot"].GetRawText());
            if (root["progress"].ValueKind != JsonValueKind.Array) throw BusinessMessageSourceState.Invalid();
            var progress = new List<BusinessMessageDefinitionProgress>();
            foreach (var value in root["progress"].EnumerateArray())
            {
                if (progress.Count == BusinessMessageAcceptedHeader.MaximumDefinitions) throw BusinessMessageSourceState.Invalid();
                progress.Add(ReadProgress(header.Scope, value));
            }
            return new(header, payload, progress);
        }
        catch (Exception error) when (BusinessMessageWire.IsMalformed(error)) { throw BusinessMessageSourceState.Invalid(); }
    }
    internal static string Fingerprint(BusinessMessageSourceState source) => BusinessMessageWire.Digest(FingerprintVersion, Encode(source));
    internal static string ProgressFingerprint(BusinessMessageDefinitionProgress progress) =>
        BusinessMessageWire.Digest(ProgressVersion, BusinessMessageWire.Json(writer => WriteProgress(writer, progress)));

    private static void WriteProgress(Utf8JsonWriter writer, BusinessMessageDefinitionProgress item)
    {
        writer.WriteStartObject(); BusinessMessageWire.O(writer, "namespace", item.Definition.DefinitionNamespace);
        BusinessMessageWire.O(writer, "id", item.Definition.DefinitionId); writer.WriteNumber("status", (int)item.Status);
        if (item.Discovery is { } discovery)
        {
            writer.WritePropertyName("discovery"); writer.WriteStartObject(); writer.WriteNumber("generation", discovery.Generation);
            writer.WriteNumber("notBeforeUtcTicks", discovery.NotBefore.UtcTicks); writer.WriteEndObject();
        }
        if (item.Selection is { } selection)
        {
            writer.WritePropertyName("selection"); writer.WriteStartObject(); BusinessMessageWire.O(writer, "id", selection.Id);
            writer.WriteNumber("capabilityEpoch", selection.CapabilityEpoch); writer.WriteNumber("selectedAtUtcTicks", selection.SelectedAt.UtcTicks); writer.WriteEndObject();
        }
        if (item.ResolvedAt is { } at) writer.WriteNumber("resolvedAtUtcTicks", at.UtcTicks);
        if (item.Evidence is { } evidence)
        {
            writer.WritePropertyName("evidence"); writer.WriteStartObject(); writer.WriteNumber("kind", (int)evidence.Kind);
            BusinessMessageWire.O(writer, "id", evidence.Id);
            if (evidence.SelectionId is { } id) BusinessMessageWire.O(writer, "selectionId", id);
            if (evidence.CapabilityEpoch is { } epoch) writer.WriteNumber("capabilityEpoch", epoch);
            writer.WriteEndObject();
        }
        if (item.Failure is { } failure) writer.WriteNumber("failure", (int)failure);
        writer.WriteEndObject();
    }
    private static BusinessMessageDefinitionProgress ReadProgress(MessageScope scope, JsonElement value)
    {
        var row = BusinessMessageWire.Object(value, ["namespace", "id", "status"], ["discovery", "selection", "resolvedAtUtcTicks", "evidence", "failure"]);
        BusinessMessageDiscoveryReference? discovery = null; BusinessMessageSelectionReference? selection = null; BusinessMessageEvidenceReference? evidence = null;
        if (row.TryGetValue("discovery", out var discoveryValue))
        {
            var item = BusinessMessageWire.Object(discoveryValue, ["generation", "notBeforeUtcTicks"]);
            discovery = new(BusinessMessageWire.Long(item["generation"]), BusinessMessageWire.At(item["notBeforeUtcTicks"]));
        }
        if (row.TryGetValue("selection", out var selectionValue))
        {
            var item = BusinessMessageWire.Object(selectionValue, ["id", "capabilityEpoch", "selectedAtUtcTicks"]);
            selection = new(BusinessMessageWire.O(item["id"]), BusinessMessageWire.Long(item["capabilityEpoch"]), BusinessMessageWire.At(item["selectedAtUtcTicks"]));
        }
        if (row.TryGetValue("evidence", out var evidenceValue))
        {
            var item = BusinessMessageWire.Object(evidenceValue, ["kind", "id"], ["selectionId", "capabilityEpoch"]);
            evidence = new((BusinessMessageEvidenceKind)BusinessMessageWire.Int(item["kind"]), BusinessMessageWire.O(item["id"]),
                item.TryGetValue("selectionId", out var id) ? BusinessMessageWire.O(id) : null,
                item.TryGetValue("capabilityEpoch", out var epoch) ? BusinessMessageWire.Long(epoch) : null);
        }
        return new(new(scope, BusinessMessageWire.O(row["namespace"]), BusinessMessageWire.O(row["id"])),
            (BusinessMessageDefinitionStatus)BusinessMessageWire.Int(row["status"]), discovery, selection,
            row.TryGetValue("resolvedAtUtcTicks", out var resolved) ? BusinessMessageWire.At(resolved) : null, evidence,
            row.TryGetValue("failure", out var failure) ? (BusinessMessageFailure)BusinessMessageWire.Int(failure) : null);
    }
}

// Family-local strict JSON/framing only; not a universal serializer or a public persisted contract.
internal static class BusinessMessageWire
{
    internal static Dictionary<string, JsonElement> Object(JsonElement value, string[] required, string[]? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw BusinessMessageSourceState.Invalid();
        var allowed = new HashSet<string>(required, StringComparer.Ordinal); allowed.UnionWith(optional ?? []);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var item in value.EnumerateObject())
            if (!allowed.Contains(item.Name) || !result.TryAdd(item.Name, item.Value)) throw BusinessMessageSourceState.Invalid();
        if (required.Any(name => !result.ContainsKey(name))) throw BusinessMessageSourceState.Invalid();
        return result;
    }
    internal static string Json(Action<Utf8JsonWriter> write)
    {
        using var bytes = new MemoryStream(); using (var writer = new Utf8JsonWriter(bytes)) write(writer);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    internal static JsonElement Element(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    internal static string Digest(string version, string json) => version + ":" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    internal static string O(string value)
    {
        var bytes = new byte[checked(value.Length * 2)];
        for (var index = 0; index < value.Length; index++) { bytes[index * 2] = (byte)value[index]; bytes[index * 2 + 1] = (byte)(value[index] >> 8); }
        return Convert.ToBase64String(bytes);
    }
    internal static string O(JsonElement value)
    {
        var encoded = Text(value); var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length % 2 != 0) throw BusinessMessageSourceState.Invalid();
        var chars = new char[bytes.Length / 2];
        for (var index = 0; index < chars.Length; index++) chars[index] = (char)(bytes[index * 2] | bytes[index * 2 + 1] << 8);
        var result = new string(chars); if (O(result) != encoded) throw BusinessMessageSourceState.Invalid(); return result;
    }
    internal static void O(Utf8JsonWriter writer, string name, string value) => writer.WriteString(name, O(value));
    internal static void NullableO(Utf8JsonWriter writer, string name, string? value) { if (value is null) writer.WriteNull(name); else O(writer, name, value); }
    internal static string? NullableO(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : O(value);
    internal static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw BusinessMessageSourceState.Invalid();
    internal static int Int(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var result) ? result : throw BusinessMessageSourceState.Invalid();
    internal static long Long(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : throw BusinessMessageSourceState.Invalid();
    internal static DateTimeOffset At(JsonElement value) => new(Long(value), TimeSpan.Zero);
    internal static bool IsMalformed(Exception error) => error is ArgumentException or InvalidOperationException or JsonException or FormatException or OverflowException or MessageSubmissionException;
}
