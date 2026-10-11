using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Produces stable, payload-preserving request representations for tenant-scoped idempotency.</summary>
public static class WorkflowAlterationRequestCanonicalizer
{
    /// <summary>Returns the canonical UTF-8 request string.</summary>
    public static string Canonicalize(WorkflowAlterationSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        var builder = new StringBuilder();
        builder.Append('{');
        WriteProperty(builder, "authority", () => WriteAuthority(builder, submission.AuthorityScope));
        builder.Append(',');
        WriteProperty(builder, "target", () => WriteTarget(builder, submission.Target));
        builder.Append(',');
        WriteProperty(builder, "alterations", () =>
        {
            builder.Append('[');
            for (var index = 0; index < submission.Alterations.Count; index++)
            {
                if (index > 0)
                    builder.Append(',');
                var envelope = submission.Alterations[index];
                builder.Append('{');
                WriteProperty(builder, "kind", () => WriteString(builder, envelope.Kind));
                builder.Append(',');
                WriteProperty(builder, "schemaVersion", () => builder.Append(envelope.SchemaVersion.ToString(CultureInfo.InvariantCulture)));
                builder.Append(',');
                WriteProperty(builder, "payload", () => WriteJson(builder, envelope.Payload));
                builder.Append('}');
            }
            builder.Append(']');
        });
        builder.Append('}');
        return builder.ToString();
    }

    /// <summary>Returns a lowercase SHA-256 hash of the canonical request.</summary>
    public static string Hash(WorkflowAlterationSubmission submission) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Canonicalize(submission))));

    private static void WriteAuthority(StringBuilder builder, WorkflowAlterationAuthorityScope authority)
    {
        builder.Append('{');
        WriteProperty(builder, "tenantPartition", () => WriteString(builder, authority.TenantPartition));
        builder.Append(',');
        WriteProperty(builder, "systemIdentity", () => WriteString(builder, authority.SystemIdentity));
        builder.Append(',');
        WriteProperty(builder, "rootInitiator", () => WriteString(builder, authority.RootInitiator));
        builder.Append(',');
        WriteProperty(builder, "metadata", () => WriteMetadata(builder, authority.Metadata));
        builder.Append('}');
    }

    private static void WriteTarget(StringBuilder builder, WorkflowAlterationTargetSelector target)
    {
        builder.Append('{');
        if (target.Query is { } query)
            WriteProperty(builder, "query", () => WriteQuery(builder, query));
        else
        {
            WriteProperty(builder, "executionIds", () =>
            {
                builder.Append('[');
                for (var index = 0; index < target.ExecutionIds.Count; index++)
                {
                    if (index > 0)
                        builder.Append(',');
                    WriteString(builder, target.ExecutionIds[index]);
                }
                builder.Append(']');
            });
        }
        builder.Append('}');
    }

    private static void WriteQuery(StringBuilder builder, WorkflowAlterationQuerySelector query)
    {
        builder.Append('{');
        var entries = new (string Name, Action Write)[]
        {
            ("artifactId", () => WriteNullableString(builder, query.ArtifactId)),
            ("correlationId", () => WriteNullableString(builder, query.CorrelationId)),
            ("definitionId", () => WriteNullableString(builder, query.DefinitionId)),
            ("from", () => WriteNullableString(builder, query.From?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))),
            ("matchAllAuthorized", () => builder.Append(query.MatchAllAuthorized ? "true" : "false")),
            ("runKind", () => WriteNullableString(builder, query.RunKind?.ToString())),
            ("status", () => WriteNullableString(builder, query.Status?.ToString())),
            ("to", () => WriteNullableString(builder, query.To?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture))),
            ("workflowExecutionId", () => WriteNullableString(builder, query.WorkflowExecutionId))
        };
        for (var index = 0; index < entries.Length; index++)
        {
            if (index > 0)
                builder.Append(',');
            WriteProperty(builder, entries[index].Name, entries[index].Write);
        }
        builder.Append('}');
    }

    private static void WriteMetadata(StringBuilder builder, IReadOnlyDictionary<string, string> values)
    {
        builder.Append('{');
        var entries = values.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        for (var index = 0; index < entries.Length; index++)
        {
            if (index > 0)
                builder.Append(',');
            WriteProperty(builder, entries[index].Key, () => WriteString(builder, entries[index].Value));
        }
        builder.Append('}');
    }

    private static void WriteJson(StringBuilder builder, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var properties = value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal).ToArray();
                for (var index = 0; index < properties.Length; index++)
                {
                    if (index > 0)
                        builder.Append(',');
                    WriteProperty(builder, properties[index].Name, () => WriteJson(builder, properties[index].Value));
                }
                builder.Append('}');
                return;
            case JsonValueKind.Array:
                builder.Append('[');
                var items = value.EnumerateArray().ToArray();
                for (var index = 0; index < items.Length; index++)
                {
                    if (index > 0)
                        builder.Append(',');
                    WriteJson(builder, items[index]);
                }
                builder.Append(']');
                return;
            case JsonValueKind.String:
                WriteString(builder, value.GetString()!);
                return;
            case JsonValueKind.Number:
                WriteNumber(builder, value.GetRawText());
                return;
            case JsonValueKind.True:
                builder.Append("true");
                return;
            case JsonValueKind.False:
                builder.Append("false");
                return;
            case JsonValueKind.Null:
                builder.Append("null");
                return;
            default:
                throw new ArgumentException("Alteration payload contains an undefined JSON value.", nameof(value));
        }
    }

    private static void WriteNumber(StringBuilder builder, string raw)
    {
        if (decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue))
            builder.Append(decimalValue.ToString("G29", CultureInfo.InvariantCulture));
        else if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue) && double.IsFinite(doubleValue))
            builder.Append(doubleValue.ToString("R", CultureInfo.InvariantCulture));
        else
            throw new ArgumentException("Alteration payload contains a non-canonical JSON number.");
    }

    private static void WriteProperty(StringBuilder builder, string name, Action value)
    {
        WriteString(builder, name);
        builder.Append(':');
        value();
    }

    private static void WriteString(StringBuilder builder, string value) => builder.Append(JsonSerializer.Serialize(value));
    private static void WriteNullableString(StringBuilder builder, string? value) { if (value is null) builder.Append("null"); else WriteString(builder, value); }

}
