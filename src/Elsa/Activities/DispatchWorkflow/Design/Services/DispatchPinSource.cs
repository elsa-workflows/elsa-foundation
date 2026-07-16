using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Design.Services;

/// <summary>Resolves each static DispatchWorkflow target to one exact live Published executable/source pin.</summary>
public sealed class DispatchPinSource(
    IWorkflowExecutableSourceReferenceStore sourceReferenceStore,
    IWorkflowExecutableStore executableStore,
    TimeProvider timeProvider) : IExecutableNodeMetadataSource
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async ValueTask<IReadOnlyCollection<ExecutableNodeMetadataContribution>> GetMetadataAsync(
        ExecutableNodeMetadataContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var dispatchNodes = Flatten(context.RootActivity)
            .Where(node => StringComparer.Ordinal.Equals(node.ActivityType, DispatchWorkflowConstants.ActivityType))
            .ToArray();
        if (dispatchNodes.Length == 0)
            return [];

        var references = await sourceReferenceStore.ListAsync(
            WorkflowExecutableReferenceScope.Published,
            liveOnly: true,
            now: timeProvider.GetUtcNow(),
            cancellationToken);
        var referencesByDefinition = references
            .GroupBy(reference => reference.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var contributions = new List<ExecutableNodeMetadataContribution>(dispatchNodes.Length);

        foreach (var node in dispatchNodes.OrderBy(item => item.ExecutableNodeId, StringComparer.Ordinal))
        {
            var definitionId = ReadDefinitionId(node);
            if (!referencesByDefinition.TryGetValue(definitionId, out var candidates) || candidates.Length != 1)
                throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' target '{definitionId}' must resolve to exactly one live Published source reference.");

            var source = candidates[0];
            var executable = await executableStore.FindAsync(source.ArtifactId, cancellationToken)
                ?? throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' target '{definitionId}' references missing executable '{source.ArtifactId}'.");
            if (!StringComparer.Ordinal.Equals(executable.Identity.ArtifactId, source.ArtifactId))
                throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' target '{definitionId}' resolved inconsistent executable/source identity.");

            var pin = new DispatchWorkflowPin(executable.Identity, WorkflowExecutableSourceProvenance.From(source));
            contributions.Add(new ExecutableNodeMetadataContribution(
                node.ExecutableNodeId,
                DispatchWorkflowConstants.PinnedTargetMetadataKey,
                JsonSerializer.Serialize(pin, SerializerOptions)));
        }

        return contributions;
    }

    private static string ReadDefinitionId(ExecutableNode node)
    {
        if (!node.InputBindings.TryGetValue("WorkflowDefinitionId", out var binding) ||
            binding.Source != RuntimeInputBindingSource.Literal ||
            binding.LiteralValue is not { ValueKind: JsonValueKind.String } literal ||
            string.IsNullOrWhiteSpace(literal.GetString()))
            throw new ArgumentException($"DispatchWorkflow node '{node.ExecutableNodeId}' requires a literal nonblank WorkflowDefinitionId.");

        return literal.GetString()!.Trim();
    }

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
