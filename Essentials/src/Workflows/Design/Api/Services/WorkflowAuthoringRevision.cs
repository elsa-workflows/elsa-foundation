using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Api.Services;

/// <summary>Request-only identities over the owner's existing authored wire representation; never a persisted stamp.</summary>
internal static class WorkflowAuthoringRevision
{
    internal static string For(WorkflowDefinitionDraft draft) => Digest([
        draft.TenantId ?? "", draft.WorkflowDefinitionId, draft.Id,
        draft.Source is { } source ? FormattableString.Invariant($"source:{source.Revision}") : FormattableString.Invariant($"modified:{draft.LastModifiedAt.UtcTicks}"),
        Snapshot(draft.State.ToStateView()).Hash
    ]);

    internal static (WorkflowDefinitionState State, string Hash) Snapshot(WorkflowDefinitionStateView view)
    {
        // Normalize through the same authored resource view and enum converters used by read/save endpoints.
        // This serialization is deliberately unrelated to outgoing activity-value serialization profiles.
        var json = JsonSerializer.SerializeToElement(view.ToState().ToStateView(), WorkflowsDesignJsonOptions.WireContext.WorkflowDefinitionStateView);
        var bytes = CanonicalBytes(json);
        var canonical = JsonSerializer.Deserialize(bytes, WorkflowsDesignJsonOptions.WireContext.WorkflowDefinitionStateView)
            ?? throw new JsonException("Invalid authored document.");
        return (canonical.ToState(), Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    internal static string Digest(IEnumerable<string> parts)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var part in parts) writer.WriteStringValue(part);
            writer.WriteEndArray();
        }
        return Convert.ToHexString(SHA256.HashData(stream.ToArray())).ToLowerInvariant();
    }

    internal static string JsonDigest(JsonElement json) => Convert.ToHexString(SHA256.HashData(CanonicalBytes(json))).ToLowerInvariant();

    private static byte[] CanonicalBytes(JsonElement json)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) Write(writer, json);
        return stream.ToArray();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement json)
    {
        switch (json.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in json.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in json.EnumerateArray()) Write(writer, item);
                writer.WriteEndArray();
                break;
            default:
                json.WriteTo(writer);
                break;
        }
    }
}
