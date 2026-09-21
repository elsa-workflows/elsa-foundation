using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Graph.Design.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Graph.Design.Services;

/// <summary>Schema-owned exact reference rewrite; universal Design never interprets graph JSON.</summary>
public sealed class GraphActivityProviderReferenceRewriter(IActivityStructureService structureService)
    : IActivityProviderReferenceRewriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IReadOnlySet<string> Schemas = new HashSet<string>(
        [ActivityGraphManifest.SchemaVersion, ActivityGraphManifest.MultipleOutcomesSchemaVersion],
        StringComparer.Ordinal);

    public string ProviderKey => GraphActivityProvider.Key;
    public IReadOnlySet<string> SupportedManifestSchemas => Schemas;

    public ValueTask<ActivityProviderManifest> RewriteReferencesAsync(
        ActivityProviderManifest manifest,
        IReadOnlyList<ActivityUpgradeOccurrenceReplacement> replacements,
        CancellationToken cancellationToken = default)
    {
        if (!StringComparer.Ordinal.Equals(manifest.ProviderKey, ProviderKey) || !Schemas.Contains(manifest.SchemaVersion))
            throw new InvalidOperationException("The graph reference rewriter does not own this provider schema.");
        if (!ActivityGraphManifest.TryParse(manifest.SchemaVersion, manifest.Payload, out var graph, out var errors) || graph is null)
            throw new InvalidOperationException($"The graph manifest cannot be rewritten: {string.Join("; ", errors.Select(x => x.Message))}");
        var root = graph.RootActivity.Deserialize<ActivityNode>(JsonOptions)
                   ?? throw new InvalidOperationException("The graph root activity cannot be deserialized.");
        var replacementsByOccurrence = replacements
            .GroupBy(x => x.OccurrenceId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Single(), StringComparer.Ordinal);
        var updated = new Dictionary<string, ActivityNode>(StringComparer.Ordinal);
        var matched = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<(ActivityNode Node, bool ChildrenVisited)>();
        stack.Push((root, false));

        while (stack.TryPop(out var frame))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!frame.ChildrenVisited)
            {
                stack.Push((frame.Node, true));
                foreach (var slot in structureService.ProjectChildren(frame.Node).Reverse())
                    foreach (var child in slot.Activities.Reverse())
                        stack.Push((child, false));
                continue;
            }

            var slots = structureService.ProjectChildren(frame.Node)
                .Select(slot => slot with { Activities = slot.Activities.Select(child => updated[child.NodeId]).ToArray() })
                .ToArray();
            var node = structureService.ReplaceChildren(frame.Node, slots);
            if (replacementsByOccurrence.TryGetValue(node.NodeId, out var replacement))
            {
                if (!StringComparer.Ordinal.Equals(node.ActivityVersionId, replacement.FromVersionId))
                    throw new InvalidOperationException($"Occurrence '{node.NodeId}' no longer references expected version '{replacement.FromVersionId}'.");
                node = node with { ActivityVersionId = replacement.ToVersionId };
                matched.Add(node.NodeId);
            }
            if (!updated.TryAdd(node.NodeId, node))
                throw new InvalidOperationException($"Graph node identity '{node.NodeId}' is not unique.");
        }

        var missing = replacementsByOccurrence.Keys.Where(x => !matched.Contains(x)).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new InvalidOperationException($"Upgrade occurrences were not found: {string.Join(", ", missing)}.");
        var rewrittenGraph = graph with { RootActivity = JsonSerializer.SerializeToElement(updated[root.NodeId], JsonOptions) };
        return ValueTask.FromResult(manifest with { Payload = rewrittenGraph.ToCanonicalJsonElement() });
    }
}
