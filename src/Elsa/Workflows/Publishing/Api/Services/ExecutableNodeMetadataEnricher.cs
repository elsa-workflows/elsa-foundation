using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>Aggregates deterministic executable-node metadata contributions and rejects conflicting ownership.</summary>
public sealed class ExecutableNodeMetadataEnricher(IInlineEventPublisher eventPublisher) : IExecutableNodeMetadataEnricher
{
    public async ValueTask<ExecutableNode> EnrichAsync(
        WorkflowExecutableCompileRequest request,
        WorkflowExecutableCompileSource source,
        ExecutableNode rootActivity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rootActivity);

        var nodes = Flatten(rootActivity).ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal);
        var ownership = nodes.ToDictionary(
            item => item.Key,
            item => item.Value.Metadata.ToDictionary(
                entry => entry.Key,
                entry => new OwnedMetadata(entry.Value, $"compiled:{item.Key}"),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
        var context = new ExecutableNodeMetadataContext(request, source, rootActivity);
        var collecting = new OnExecutableNodeMetadataCollecting(context);
        await eventPublisher.Publish(collecting, cancellationToken);

        foreach (var contribution in collecting.Contributions
                     .OrderBy(item => item.SourceIdentity, StringComparer.Ordinal)
                     .ThenBy(item => item.ExecutableNodeId, StringComparer.Ordinal)
                     .ThenBy(item => item.Key, StringComparer.Ordinal))
        {
            ArgumentNullException.ThrowIfNull(contribution);
            ArgumentException.ThrowIfNullOrWhiteSpace(contribution.ExecutableNodeId);
            ArgumentException.ThrowIfNullOrWhiteSpace(contribution.Key);
            ArgumentException.ThrowIfNullOrWhiteSpace(contribution.Value);
            ArgumentException.ThrowIfNullOrWhiteSpace(contribution.SourceIdentity);
            var sourceIdentity = contribution.SourceIdentity!;

            if (!ownership.TryGetValue(contribution.ExecutableNodeId, out var nodeMetadata))
                throw new ArgumentException($"Executable metadata contribution targets unknown node '{contribution.ExecutableNodeId}'.");
            if (nodeMetadata.TryGetValue(contribution.Key, out var existing))
            {
                if (StringComparer.Ordinal.Equals(existing.Value, contribution.Value))
                    continue;

                var owners = new[] { existing.Owner, sourceIdentity }.Order(StringComparer.Ordinal).ToArray();
                throw new ArgumentException(
                    $"Executable node '{contribution.ExecutableNodeId}' metadata key '{contribution.Key}' has unequal values from owners '{owners[0]}' and '{owners[1]}'.");
            }

            nodeMetadata.Add(contribution.Key, new OwnedMetadata(contribution.Value, sourceIdentity));
        }

        var metadata = ownership.ToDictionary(
            item => item.Key,
            item => item.Value.ToDictionary(entry => entry.Key, entry => entry.Value.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
        return Rebuild(rootActivity, metadata);
    }

    private static ExecutableNode Rebuild(
        ExecutableNode node,
        IReadOnlyDictionary<string, Dictionary<string, string>> metadata) =>
        new(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.DescriptorType,
            node.DescriptorPayload,
            node.InputBindings,
            node.OutputCaptures,
            metadata[node.ExecutableNodeId],
            node.ChildSlots.Select(slot => new ExecutableChildSlot(
                slot.Name,
                slot.Activities.Select(child => Rebuild(child, metadata)).ToArray())).ToArray(),
            node.Structure);

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode root)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities).Reverse())
                stack.Push(child);
        }
    }

    private sealed record OwnedMetadata(string Value, string Owner);
}
