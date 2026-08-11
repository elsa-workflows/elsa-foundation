using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>Aggregates deterministic executable-node metadata contributions and rejects conflicting ownership.</summary>
public sealed class ExecutableNodeMetadataEnricher(IInlineEventPublisher eventPublisher) : IExecutableNodeMetadataEnricher
{
    public async ValueTask<ExecutableNode> EnrichAsync(
        WorkflowExecutableCompileRequest request,
        WorkflowExecutableCompileSource source,
        ExecutableNode rootActivity,
        CancellationToken cancellationToken = default) =>
        (await EnrichCompilationAsync(request, source, rootActivity, cancellationToken)).RootActivity;

    public async ValueTask<ExecutableCompilationEnrichment> EnrichCompilationAsync(
        WorkflowExecutableCompileRequest request,
        WorkflowExecutableCompileSource source,
        ExecutableNode rootActivity,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(rootActivity);

        var metadata = Flatten(rootActivity).ToDictionary(
            node => node.ExecutableNodeId,
            node => node.Metadata.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
        var context = new ExecutableCompilationContext(request, source, rootActivity);
        var collecting = new ExecutableCompilationCollecting(context);
        await eventPublisher.Publish(collecting, cancellationToken);

        foreach (var contribution in collecting.Contributions.OrderBy(item => item.SourceIdentity, StringComparer.Ordinal))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contribution.SourceIdentity);
            foreach (var claim in contribution.NodeMetadata
                         .OrderBy(item => item.ExecutableNodeId, StringComparer.Ordinal)
                         .ThenBy(item => item.Key, StringComparer.Ordinal))
            {
                if (!metadata.TryGetValue(claim.ExecutableNodeId, out var nodeMetadata))
                    throw new ArgumentException($"Executable metadata contribution targets unknown node '{claim.ExecutableNodeId}'.");
                nodeMetadata[claim.Key] = claim.Value;
            }
        }

        var dependencies = collecting.Contributions
            .OrderBy(item => item.SourceIdentity, StringComparer.Ordinal)
            .SelectMany(item => item.Dependencies)
            .OrderBy(item => item.ArtifactId, StringComparer.Ordinal)
            .ThenBy(item => item.ArtifactHash, StringComparer.Ordinal)
            .ThenBy(item => item.ExecutableNodeId, StringComparer.Ordinal)
            .ToArray();

        return new ExecutableCompilationEnrichment(Rebuild(rootActivity, metadata), dependencies);
    }

    private static ExecutableNode Rebuild(
        ExecutableNode node,
        IReadOnlyDictionary<string, Dictionary<string, string>> metadata) =>
        new(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.Descriptor,
            node.InputBindings,
            node.OutputCaptures,
            metadata[node.ExecutableNodeId],
            node.ChildSlots.Select(slot => new ExecutableChildSlot(
                slot.Name,
                slot.Activities.Select(child => Rebuild(child, metadata)).ToArray(),
                slot.OperatorSchedulingCapability)).ToArray(),
            node.Structure,
            node.ActivityContract,
            node.IntrinsicKind,
            node.IntrinsicVariable);

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
}
