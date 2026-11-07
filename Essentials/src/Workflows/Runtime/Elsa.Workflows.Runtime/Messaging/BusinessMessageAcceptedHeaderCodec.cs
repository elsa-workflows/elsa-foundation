using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Messaging;

/// <summary>Family-closed immutable header/slot codec; neither JSON nor its fingerprint grants acceptance authority.</summary>
/// <remarks>Domain adapter owns the outer versioned {header,payloadSlot,progress} ContentJson object. It must
/// validate its closed progress schema and, whenever work needs payload, bind the Present slot before returning
/// a checked claimable read. Retired/missing payload never becomes JSON null. No generic source reader owns that policy.</remarks>
internal static class BusinessMessageAcceptedHeaderCodec
{
    internal const string HeaderFingerprintVersion = "elsa.business-message-header/v1";
    internal const string PayloadVersion = "elsa.business-message-payload/v1";
    internal const string CohortVersion = "elsa.business-message-cohort/v1";

    internal static string Encode(BusinessMessageAcceptedHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        return Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("version", BusinessMessageAcceptedHeader.Version);
            writer.WriteString("family", BusinessMessageAcceptedHeader.Family);
            O(writer, "messageId", header.Address.SourceId); O(writer, "receiptId", header.Receipt.ReceiptId);
            writer.WritePropertyName("context"); WriteContext(writer, header.Context);
            O(writer, "producerNamespace", header.ProducerNamespace); O(writer, "configurationRevision", header.ConfigurationRevision);
            writer.WritePropertyName("submission"); writer.WriteStartObject();
            writer.WriteString("name", header.Name); writer.WriteString("correlationKey", header.CorrelationKey);
            if (header.ProducerMessageId is not null) writer.WriteString("messageId", header.ProducerMessageId);
            if (header.LifetimeSeconds is { } lifetime) writer.WriteNumber("lifetimeSeconds", lifetime);
            writer.WriteString("fingerprint", header.SubmittedFingerprint); writer.WriteString("payloadCommitment", header.PayloadCommitment);
            writer.WriteEndObject();
            writer.WritePropertyName("policy"); writer.WriteStartObject(); writer.WriteNumber("version", 1);
            writer.WriteNumber("defaultBufferSeconds", header.Policy.DefaultBufferLifetimeSeconds);
            writer.WriteNumber("maximumBufferSeconds", header.Policy.MaximumBufferLifetimeSeconds);
            writer.WriteNumber("deduplicationSeconds", header.Policy.DeduplicationLifetimeSeconds); writer.WriteEndObject();
            writer.WriteNumber("acceptedAtUtcTicks", header.Receipt.AcceptedAt.UtcTicks);
            if (header.Receipt.BufferUntil is { } buffer) writer.WriteNumber("bufferUntilUtcTicks", buffer.UtcTicks);
            if (header.Receipt.DeduplicateUntil is { } dedup) writer.WriteNumber("deduplicateUntilUtcTicks", dedup.UtcTicks);
            writer.WriteNumber("interestEpoch", header.InterestEpoch); writer.WriteString("cohortDigest", header.CohortDigest);
            writer.WritePropertyName("definitions"); WriteDefinitions(writer, header.Definitions); writer.WriteEndObject();
        });
    }

    internal static BusinessMessageAcceptedHeader Decode(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
            var root = Object(document.RootElement,
                ["version", "family", "messageId", "receiptId", "context", "producerNamespace", "configurationRevision", "submission",
                 "policy", "acceptedAtUtcTicks", "interestEpoch", "cohortDigest", "definitions"], ["bufferUntilUtcTicks", "deduplicateUntilUtcTicks"]);
            if (Int(root["version"]) != BusinessMessageAcceptedHeader.Version)
                throw new NotSupportedException("The BusinessMessage header version is unsupported.");
            if (Text(root["family"]) != BusinessMessageAcceptedHeader.Family) throw Invalid();
            var context = ReadContext(root["context"]); var scope = new MessageScope(context.Scope, context.TenantId, context.TestScope?.ScopeId);
            var submission = Object(root["submission"], ["name", "correlationKey", "fingerprint", "payloadCommitment"], ["messageId", "lifetimeSeconds"]);
            var policy = Object(root["policy"], ["version", "defaultBufferSeconds", "maximumBufferSeconds", "deduplicationSeconds"]);
            if (Int(policy["version"]) != 1) throw new NotSupportedException("The BusinessMessage policy version is unsupported.");
            if (root["definitions"].ValueKind != JsonValueKind.Array) throw Invalid();
            var definitions = new List<MessageDefinitionIdentity>();
            foreach (var value in root["definitions"].EnumerateArray())
            {
                if (definitions.Count == BusinessMessageAcceptedHeader.MaximumDefinitions) throw Invalid();
                var item = Object(value, ["namespace", "id"]);
                definitions.Add(new(scope, O(item["namespace"]), O(item["id"])));
            }
            var header = BusinessMessageAcceptedHeader.Restore(O(root["messageId"]), O(root["receiptId"]), context,
                O(root["producerNamespace"]), O(root["configurationRevision"]), Text(submission["name"]), Text(submission["correlationKey"]),
                submission.TryGetValue("messageId", out var producerId) ? Text(producerId) : null,
                submission.TryGetValue("lifetimeSeconds", out var lifetime) ? Int(lifetime) : null,
                Text(submission["fingerprint"]), Text(submission["payloadCommitment"]),
                new(Int(policy["defaultBufferSeconds"]), Int(policy["deduplicationSeconds"]), Int(policy["maximumBufferSeconds"])),
                At(root["acceptedAtUtcTicks"]), Long(root["interestEpoch"]), definitions);
            if (header.CohortDigest != Text(root["cohortDigest"]) ||
                header.Receipt.BufferUntil != OptionalAt(root, "bufferUntilUtcTicks") ||
                header.Receipt.DeduplicateUntil != OptionalAt(root, "deduplicateUntilUtcTicks"))
                throw Invalid();
            return header;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or FormatException or MessageSubmissionException)
        { throw Invalid(); }
    }

    // Convenience for a caller that already holds a checked physical immutable-header expectation.
    // The EF family adapter can instead Decode, compute Fingerprint independently, and let the common reader
    // compare the actual row's HeaderFingerprint. No expected digest is read from the header itself.
    internal static BusinessMessageAcceptedHeader Decode(string json, string expectedFingerprint)
    {
        var header = Decode(json);
        if (Fingerprint(header) != expectedFingerprint) throw Invalid();
        return header;
    }

    internal static string Fingerprint(BusinessMessageAcceptedHeader header) => Digest(HeaderFingerprintVersion, Encoding.UTF8.GetBytes(Encode(header)));
    internal static string ComputePayloadCommitment(JsonElement payload)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes)) MessageSubmissionRules.WriteCanonical(writer, payload, 0);
        return Digest(PayloadVersion, bytes.ToArray());
    }
    internal static string ComputeCohortDigest(MessageScope scope, IReadOnlyList<MessageDefinitionIdentity> definitions) =>
        Digest(CohortVersion, Encoding.UTF8.GetBytes(Json(writer =>
        {
            writer.WriteStartObject(); O(writer, "scope", scope.PersistenceScope.Value); NullableO(writer, "tenant", scope.TenantId);
            NullableO(writer, "testScopeId", scope.TestScopeId); writer.WritePropertyName("definitions");
            WriteDefinitions(writer, definitions); writer.WriteEndObject();
        })));

    internal static string EncodePayloadSlot(BusinessMessagePayloadSlot slot)
    {
        ArgumentNullException.ThrowIfNull(slot);
        return Json(writer =>
        {
            writer.WriteStartObject(); writer.WriteNumber("state", (int)slot.State);
            if (slot.State == BusinessMessagePayloadState.Present)
            { writer.WritePropertyName("payload"); MessageSubmissionRules.WriteCanonical(writer, slot.Payload, 0); }
            else
            { O(writer, "operationId", slot.RetirementOperationId!); O(writer, "operationFingerprint", slot.RetirementOperationFingerprint!); }
            writer.WriteEndObject();
        });
    }
    internal static BusinessMessagePayloadSlot DecodePayloadSlot(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 128 });
            var shape = Object(document.RootElement, ["state"], ["payload", "operationId", "operationFingerprint"]);
            return Int(shape["state"]) switch
            {
                0 when shape.Count == 2 && shape.TryGetValue("payload", out var value) => BusinessMessagePayloadSlot.Present(value),
                1 when shape.Count == 3 && shape.ContainsKey("operationId") && shape.ContainsKey("operationFingerprint") =>
                    BusinessMessagePayloadSlot.Retired(O(shape["operationId"]), O(shape["operationFingerprint"])),
                _ => throw Invalid()
            };
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or FormatException)
        { throw Invalid(); }
    }

    internal static void RequireFingerprint(string value, string version)
    {
        if (value is null || !value.StartsWith(version + ":", StringComparison.Ordinal) || value.Length != version.Length + 65 ||
            value.Skip(version.Length + 1).Any(character => character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f'))) throw Invalid();
    }

    private static void WriteDefinitions(Utf8JsonWriter writer, IReadOnlyList<MessageDefinitionIdentity> definitions)
    {
        writer.WriteStartArray();
        foreach (var definition in definitions)
        { writer.WriteStartObject(); O(writer, "namespace", definition.DefinitionNamespace); O(writer, "id", definition.DefinitionId); writer.WriteEndObject(); }
        writer.WriteEndArray();
    }
    private static void WriteContext(Utf8JsonWriter writer, RuntimeDeliveryDiscoveryPartition context)
    {
        writer.WriteStartObject(); O(writer, "scope", context.Scope.Value); NullableO(writer, "tenant", context.TenantId);
        writer.WriteNumber("runKind", (int)context.RunKind); NullableO(writer, "executionPartition", context.ExecutionPartition?.Value);
        writer.WritePropertyName("authority"); writer.WriteStartObject();
        O(writer, "system", context.Authority.SystemIdentity); O(writer, "root", context.Authority.RootInitiator);
        writer.WritePropertyName("metadata"); writer.WriteStartArray();
        foreach (var item in context.Authority.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal))
        { writer.WriteStartObject(); O(writer, "key", item.Key); O(writer, "value", item.Value); writer.WriteEndObject(); }
        writer.WriteEndArray(); writer.WriteEndObject(); writer.WritePropertyName("testScope");
        if (context.TestScope is not { } test) writer.WriteNullValue();
        else
        {
            writer.WriteStartObject(); O(writer, "id", test.ScopeId); writer.WriteNumber("expiresAtUtcTicks", test.ExpiresAt.UtcTicks);
            NullableO(writer, "tenant", test.TenantId); O(writer, "partition", test.Partition.Value); writer.WriteEndObject();
        }
        writer.WriteEndObject();
    }
    private static RuntimeDeliveryDiscoveryPartition ReadContext(JsonElement value)
    {
        var context = Object(value, ["scope", "tenant", "runKind", "executionPartition", "authority", "testScope"]);
        var authority = Object(context["authority"], ["system", "root", "metadata"]);
        if (authority["metadata"].ValueKind != JsonValueKind.Array) throw Invalid();
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var valueItem in authority["metadata"].EnumerateArray())
        {
            var item = Object(valueItem, ["key", "value"]);
            if (!metadata.TryAdd(O(item["key"]), O(item["value"]))) throw Invalid();
        }
        WorkflowTestScope? test = null;
        if (context["testScope"].ValueKind != JsonValueKind.Null)
        {
            var item = Object(context["testScope"], ["id", "expiresAtUtcTicks", "tenant", "partition"]);
            test = new(O(item["id"]), At(item["expiresAtUtcTicks"]), NullableO(item["tenant"]), new(O(item["partition"])));
        }
        return new(new(O(context["scope"])), NullableO(context["tenant"]), (WorkflowRunKind)Int(context["runKind"]),
            new(O(authority["system"]), O(authority["root"]), metadata),
            context["executionPartition"].ValueKind == JsonValueKind.Null ? null : new(O(context["executionPartition"])), test);
    }
    private static Dictionary<string, JsonElement> Object(JsonElement value, string[] required, string[]? optional = null)
    {
        if (value.ValueKind != JsonValueKind.Object) throw Invalid();
        var allowed = new HashSet<string>(required, StringComparer.Ordinal); allowed.UnionWith(optional ?? []);
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!allowed.Contains(property.Name) || !result.TryAdd(property.Name, property.Value)) throw Invalid();
        if (required.Any(name => !result.ContainsKey(name))) throw Invalid();
        return result;
    }
    private static string Json(Action<Utf8JsonWriter> write)
    {
        using var bytes = new MemoryStream();
        using (var writer = new Utf8JsonWriter(bytes)) write(writer);
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    // Explicit family-local wire, following RuntimeArtifactJson's strategy without a Runtime→EF dependency.
    private static string O(string text)
    {
        var bytes = new byte[checked(text.Length * 2)];
        for (var index = 0; index < text.Length; index++) { bytes[index * 2] = (byte)text[index]; bytes[index * 2 + 1] = (byte)(text[index] >> 8); }
        return Convert.ToBase64String(bytes);
    }
    private static string O(JsonElement value)
    {
        var encoded = Text(value); var bytes = Convert.FromBase64String(encoded);
        if (bytes.Length % 2 != 0) throw Invalid();
        var chars = new char[bytes.Length / 2];
        for (var index = 0; index < chars.Length; index++) chars[index] = (char)(bytes[index * 2] | bytes[index * 2 + 1] << 8);
        var text = new string(chars);
        if (O(text) != encoded) throw Invalid();
        return text;
    }
    private static void O(Utf8JsonWriter writer, string name, string value) => writer.WriteString(name, O(value));
    private static void NullableO(Utf8JsonWriter writer, string name, string? value) { if (value is null) writer.WriteNull(name); else O(writer, name, value); }
    private static string? NullableO(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : O(value);
    private static string Text(JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString()! : throw Invalid();
    private static int Int(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number) ? number : throw Invalid();
    private static long Long(JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : throw Invalid();
    private static DateTimeOffset At(JsonElement value) => new(Long(value), TimeSpan.Zero);
    private static DateTimeOffset? OptionalAt(Dictionary<string, JsonElement> value, string name) => value.TryGetValue(name, out var item) ? At(item) : null;
    private static string Digest(string version, byte[] value) => version + ":" + Convert.ToHexStringLower(SHA256.HashData(value));
    private static InvalidDataException Invalid() => new("The canonical BusinessMessage content is invalid.");
}
