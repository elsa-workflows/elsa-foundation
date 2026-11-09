using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Disclosure.Services;

/// <summary>Validates and snapshots the bounded conjunction independently of executable behavior hashes.</summary>
internal sealed class CatalogDisclosureRequirementCanonicalizer
{
    internal const int SchemaVersion = 1;
    internal const int MaximumPolicies = 64;
    internal const int MaximumPolicyKeyLength = 128;
    internal const int MaximumBindingSchemaLength = 64;
    internal const int MaximumResourceDepth = 16;
    internal const int MaximumResourceBytes = 16 * 1024;
    internal const int MaximumRequirementBytes = 256 * 1024;

    public bool TryCanonicalize(CatalogDisclosureRequirement? value, out CatalogDisclosureRequirement canonical)
    {
        try
        {
            canonical = Canonicalize(value);
            return true;
        }
        catch (CatalogDisclosureRequirementException)
        {
            canonical = null!;
            return false;
        }
    }

    public CatalogDisclosureRequirement Canonicalize(CatalogDisclosureRequirement? value)
    {
        try
        {
            return CanonicalizeCore(value);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or ArgumentException)
        {
            throw new CatalogDisclosureRequirementException(exception);
        }
    }

    private static CatalogDisclosureRequirement CanonicalizeCore(CatalogDisclosureRequirement? value)
    {
        if (value is null || value.SchemaVersion != SchemaVersion || value.Policies is null ||
            value.Policies.Count > MaximumPolicies)
            throw Invalid();

        if (value.Kind == CatalogDisclosureKind.Public)
        {
            if (value.Policies.Count != 0)
                throw Invalid();
            return new(SchemaVersion, CatalogDisclosureKind.Public, []);
        }

        if (value.Kind != CatalogDisclosureKind.Protected || value.Policies.Count == 0)
            throw Invalid();

        var policies = new Dictionary<(string Key, string Schema, string Resource), CatalogDisclosurePolicyRequirement>();
        foreach (var policy in value.Policies)
        {
            if (policy is null || !ValidKey(policy.PolicyKey, MaximumPolicyKeyLength) ||
                !ValidKey(policy.BindingSchema, MaximumBindingSchemaLength))
                throw Invalid();

            var bytes = WriteBounded(MaximumResourceBytes, writer => WriteResource(writer, policy.Resource, 0));
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaximumResourceDepth });
            var resource = Encoding.UTF8.GetString(bytes);
            policies.TryAdd((policy.PolicyKey, policy.BindingSchema, resource),
                new(policy.PolicyKey, policy.BindingSchema, document.RootElement));
        }

        var result = CreateProtected(policies);
        _ = WriteRequirement(result);
        return result;
    }

    public CatalogDisclosureRequirement Union(IEnumerable<CatalogDisclosureRequirement> values)
    {
        if (values is null)
            throw Invalid();
        var policies = new Dictionary<(string Key, string Schema, string Resource), CatalogDisclosurePolicyRequirement>();
        var hasValue = false;
        foreach (var value in values)
        {
            hasValue = true;
            var canonical = Canonicalize(value);
            foreach (var policy in canonical.Policies)
            {
                policies.TryAdd((policy.PolicyKey, policy.BindingSchema, policy.Resource.GetRawText()), policy);
                if (policies.Count > MaximumPolicies)
                    throw Invalid();
            }
        }

        // An absent lineage input must not become an explicit Public classification.
        if (!hasValue)
            throw Invalid();
        var result = policies.Count == 0
            ? new CatalogDisclosureRequirement(SchemaVersion, CatalogDisclosureKind.Public, [])
            : CreateProtected(policies);
        _ = WriteRequirement(result);
        return result;
    }

    public bool AreEquivalent(CatalogDisclosureRequirement? left, CatalogDisclosureRequirement? right) =>
        TryCanonicalize(left, out var canonicalLeft) && TryCanonicalize(right, out var canonicalRight) &&
        WriteRequirement(canonicalLeft).AsSpan().SequenceEqual(WriteRequirement(canonicalRight));

    public string ToCanonicalJson(CatalogDisclosureRequirement? value) =>
        Encoding.UTF8.GetString(WriteRequirement(Canonicalize(value)));

    private static CatalogDisclosureRequirement CreateProtected(
        Dictionary<(string Key, string Schema, string Resource), CatalogDisclosurePolicyRequirement> policies) =>
        new(SchemaVersion, CatalogDisclosureKind.Protected,
            policies.OrderBy(item => item.Key.Key, StringComparer.Ordinal)
                .ThenBy(item => item.Key.Schema, StringComparer.Ordinal)
                .ThenBy(item => item.Key.Resource, StringComparer.Ordinal)
                .Select(item => item.Value).ToArray());

    private static byte[] WriteRequirement(CatalogDisclosureRequirement value) =>
        WriteBounded(MaximumRequirementBytes, writer =>
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", value.SchemaVersion);
            writer.WriteNumber("kind", (int)value.Kind);
            writer.WriteStartArray("policies");
            foreach (var policy in value.Policies)
            {
                writer.WriteStartObject();
                writer.WriteString("policyKey", policy.PolicyKey);
                writer.WriteString("bindingSchema", policy.BindingSchema);
                writer.WritePropertyName("resource");
                policy.Resource.WriteTo(writer);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        });

    private static void WriteResource(Utf8JsonWriter writer, JsonElement value, int depth)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
            {
                if (depth >= MaximumResourceDepth)
                    throw Invalid();
                var properties = new List<JsonProperty>();
                var names = new HashSet<string>(StringComparer.Ordinal);
                var minimumBytes = 2;
                foreach (var property in value.EnumerateObject())
                {
                    // Even before escaping or visiting values, this much canonical JSON must fit.
                    var nameBytes = Encoding.UTF8.GetByteCount(property.Name);
                    minimumBytes += nameBytes + 4 + (properties.Count == 0 ? 0 : 1);
                    if (!CatalogDisclosureText.IsWellFormedUtf16(property.Name) ||
                        !names.Add(property.Name) || minimumBytes > MaximumResourceBytes)
                        throw Invalid();
                    properties.Add(property);
                }
                writer.WriteStartObject();
                foreach (var property in properties.OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteResource(writer, property.Value, depth + 1);
                }
                writer.WriteEndObject();
                break;
            }
            case JsonValueKind.Array:
                if (depth >= MaximumResourceDepth || value.GetArrayLength() > MaximumResourceBytes)
                    throw Invalid();
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteResource(writer, item, depth + 1);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
            {
                var text = value.GetString()!;
                if (!CatalogDisclosureText.IsWellFormedUtf16(text) ||
                    Encoding.UTF8.GetByteCount(text) > MaximumResourceBytes)
                    throw Invalid();
                writer.WriteStringValue(text);
                break;
            }
            case JsonValueKind.Number:
            {
                // Preserve exact JSON numeric tokens: no conversion through double/decimal or silent rounding.
                var token = value.GetRawText();
                if (Encoding.UTF8.GetByteCount(token) > MaximumResourceBytes)
                    throw Invalid();
                writer.WriteRawValue(token);
                break;
            }
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                // Null is allowed inside an opaque binding, but a missing root binding is not a requirement.
                if (depth == 0)
                    throw Invalid();
                writer.WriteNullValue();
                break;
            default:
                throw Invalid();
        }
    }

    private static byte[] WriteBounded(int maximumBytes, Action<Utf8JsonWriter> write)
    {
        using var stream = new BoundedJsonStream(maximumBytes);
        using var writer = new Utf8JsonWriter(stream);
        write(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static bool ValidKey(string value, int maximumLength) =>
        CatalogDisclosureText.IsValid(value, maximumLength);

    private static CatalogDisclosureRequirementException Invalid() => new();

    private sealed class BoundedJsonStream(int maximumBytes) : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (Length + count > maximumBytes)
                throw Invalid();
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (Length + buffer.Length > maximumBytes)
                throw Invalid();
            base.Write(buffer);
        }
    }
}

/// <summary>Safe, input-free refusal for malformed or excessive retained security material.</summary>
internal sealed class CatalogDisclosureRequirementException(Exception? innerException = null)
    : Exception("The catalog disclosure requirement is invalid or unsupported.", innerException)
{
    public const string Code = "activities.catalog.requirement-invalid";
}
