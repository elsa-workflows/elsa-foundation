using System.Collections.ObjectModel;
using System.Text.Json;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>Bounded transcript dependencies; current execution context and original targets are kept elsewhere.</summary>
internal sealed class AgentDisclosureHistory
{
    internal const int MaximumSnapshots = 64;
    internal const int MaximumBytes = 8 * 1024 * 1024;
    private readonly List<AgentDisclosureSnapshot> _snapshots = [];
    private readonly HashSet<string> _identities = new(StringComparer.Ordinal);
    private long _bytes;

    internal IReadOnlyList<AgentDisclosureSnapshot> Snapshots => Array.AsReadOnly(_snapshots.ToArray());

    internal AgentError? Add(IReadOnlyCollection<AgentContextAttachment> context, IReadOnlyList<AgentToolResult> results)
    {
        if (context is null || results is null)
            return new("agent.history.provenance_unavailable", "Complete disclosure history is missing its original context or outcomes.", 409);
        if (context.Count == 0 && results.Count == 0) return null;
        try
        {
            var snapshot = Freeze(new AgentDisclosureSnapshot(context, results));
            var identity = AgentInvocationIntegrity.Digest(snapshot);
            if (_identities.Contains(identity)) return null;
            var size = JsonSerializer.SerializeToUtf8Bytes(snapshot).LongLength;
            if (_snapshots.Count >= MaximumSnapshots || _bytes + size > MaximumBytes)
                return new("agent.context.disclosure_budget_exceeded", "The complete transcript disclosure history exceeds the supported bound. Start a fresh session; no dependency was discarded.", 409);
            _identities.Add(identity);
            _snapshots.Add(snapshot);
            _bytes += size;
            return null;
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or AgentContributionException or ArgumentException)
        {
            return new("agent.history.provenance_unavailable", "Complete immutable disclosure history could not be retained.", 409);
        }
    }

    internal AgentError? Add(IEnumerable<AgentDisclosureSnapshot> snapshots)
    {
        foreach (var snapshot in snapshots)
        {
            if (snapshot is null) return new("agent.history.provenance_unavailable", "A retained disclosure snapshot is missing.", 409);
            if (Add(snapshot.Context, snapshot.ToolResults) is { } error) return error;
        }
        return null;
    }

    internal static IReadOnlyList<AgentDisclosureSnapshot> Freeze(IEnumerable<AgentDisclosureSnapshot> snapshots)
        => Array.AsReadOnly(snapshots.Select(Freeze).ToArray());

    private static AgentDisclosureSnapshot Freeze(AgentDisclosureSnapshot snapshot)
        => new(Array.AsReadOnly(snapshot.Context.Select(attachment => attachment with
        {
            Content = attachment.Content is null ? null : JsonSerializer.SerializeToElement(attachment.Content).Clone(),
            References = new ReadOnlyDictionary<string, string>(attachment.References.ToDictionary()),
            AgentScopes = Array.AsReadOnly(attachment.AgentScopes.ToArray())
        }).ToArray()), Array.AsReadOnly(snapshot.ToolResults.Select(result => result with
        {
            Payload = result.Payload is null ? null : AgentInvocationIntegrity.Freeze(result.Payload),
            Receipt = result.Receipt is null ? null : result.Receipt with { Evidence = AgentInvocationIntegrity.Freeze(result.Receipt.Evidence) }
        }).ToArray()));

    internal static string MessageFingerprint(string content, IReadOnlyCollection<AgentContextAttachment> context,
        IReadOnlyList<AgentToolResult> results, IReadOnlyList<AgentDisclosureSnapshot> history)
        => AgentInvocationIntegrity.Digest(new { Content = content, Context = context, ToolResults = results, DisclosureHistory = history });

    internal static string PauseFingerprint(AgentPausedTurn state)
        => AgentInvocationIntegrity.Digest(state with { DisclosureHistoryFingerprint = null });
}
