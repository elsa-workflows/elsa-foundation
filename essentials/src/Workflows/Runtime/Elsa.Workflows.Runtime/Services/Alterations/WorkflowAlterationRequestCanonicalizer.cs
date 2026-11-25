using System.Globalization;
using System.Text;
using System.Text.Json;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>Produces stable, payload-preserving request representations for tenant-scoped idempotency.</summary>
public static class WorkflowAlterationRequestCanonicalizer
{
    /// <summary>Returns the request representation retained by payload protection, preserving each raw JSON payload.</summary>
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

    /// <summary>Returns the canonical JSON hash of the logical request, independently of its retained payload spelling.</summary>
    public static string Hash(WorkflowAlterationSubmission submission)
    {
        ArgumentNullException.ThrowIfNull(submission);
        return submission.Target.Query is { } query
            ? HashRequest(submission, new
            {
                query = new
                {
                    query.ArtifactId,
                    query.CorrelationId,
                    query.DefinitionId,
                    From = query.From?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    query.MatchAllAuthorized,
                    RunKind = query.RunKind?.ToString(),
                    Status = query.Status?.ToString(),
                    To = query.To?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
                    query.WorkflowExecutionId
                }
            })
            : HashRequest(submission, new { executionIds = submission.Target.ExecutionIds });
    }

    private static string HashRequest<TTarget>(WorkflowAlterationSubmission submission, TTarget target) =>
        JsonHashInput.Hash(RuntimeJsonHashProjection.Project(new
        {
            authority = submission.AuthorityScope,
            target,
            alterations = submission.Alterations
        }));

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
        if (value.ValueKind is JsonValueKind.Undefined)
            throw new ArgumentException("Alteration payload contains an undefined JSON value.", nameof(value));
        builder.Append(value.GetRawText());
    }

    private static void WriteProperty(StringBuilder builder, string name, Action value)
    {
        WriteString(builder, name);
        builder.Append(':');
        value();
    }

    private static void WriteString(StringBuilder builder, string value) => builder.Append(JsonSerializer.Serialize(value, JsonBaseline.Options));
    private static void WriteNullableString(StringBuilder builder, string? value) { if (value is null) builder.Append("null"); else WriteString(builder, value); }

}
