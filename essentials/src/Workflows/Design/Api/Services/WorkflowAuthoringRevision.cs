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
        var snapshot = JsonSerializer.Deserialize(json, WorkflowsDesignJsonOptions.WireContext.WorkflowDefinitionStateView)
            ?? throw new JsonException("Invalid authored document.");
        return (snapshot.ToState(), Elsa.Serialization.Core.JsonHashInput.Hash(json));
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
        return Elsa.Serialization.Core.JsonHashInput.HashUtf8(stream.ToArray());
    }

    internal static string JsonDigest(JsonElement json) => Elsa.Serialization.Core.JsonHashInput.Hash(json);
}
