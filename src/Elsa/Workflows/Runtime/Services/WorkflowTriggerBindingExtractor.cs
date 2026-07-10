using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default <see cref="IWorkflowTriggerBindingExtractor"/> (W7, E3-1). It walks the published executable's node
/// tree, selects the nodes the compiler marked as start-triggers (via
/// <see cref="TriggerNodeMetadata.ExecutionTypeKey"/>), and resolves each one's stimulus identity through the
/// registered <see cref="IActivityTriggerStimulusProvider"/> set — deriving the durable trigger index over the
/// pinned, published artifact rather than the mutable authored definition.
/// </summary>
public sealed class WorkflowTriggerBindingExtractor : IWorkflowTriggerBindingExtractor
{
    private readonly IReadOnlyList<IActivityTriggerStimulusProvider> _providers;
    private readonly TimeProvider _timeProvider;

    public WorkflowTriggerBindingExtractor(IEnumerable<IActivityTriggerStimulusProvider> providers)
        : this(providers, TimeProvider.System)
    {
    }

    public WorkflowTriggerBindingExtractor(IEnumerable<IActivityTriggerStimulusProvider> providers, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _providers = providers.ToArray();
        _timeProvider = timeProvider;
    }

    public IReadOnlyCollection<WorkflowTriggerBinding> Extract(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);

        var identity = executable.Identity;
        var now = _timeProvider.GetUtcNow();
        var bindings = new List<WorkflowTriggerBinding>();

        foreach (var node in Flatten(executable.RootActivity))
        {
            if (!IsTrigger(node))
                continue;

            var result = Describe(node);
            if (!result.IsRecognized)
                throw new WorkflowTriggerExtractionException(
                    identity.ArtifactId,
                    node.ExecutableNodeId,
                    $"Node '{node.ExecutableNodeId}' (activity type '{node.ActivityType}') is marked as a start-trigger, " +
                    "but no registered trigger stimulus provider could describe its stimulus. A published trigger that " +
                    "cannot be indexed is refused so it never silently fails to fire.");

            // A recognized node with zero descriptors declares itself a non-start (spec 089 D:
            // CanStartWorkflow = false) — no bindings, no publish failure. Only an unrecognized node fails.
            foreach (var descriptor in result.Descriptors)
            {
                bindings.Add(new WorkflowTriggerBinding(
                    TriggerBindingId: WorkflowTriggerBinding.BuildId(identity.ArtifactId, node.ExecutableNodeId, descriptor.StimulusHash),
                    ArtifactId: identity.ArtifactId,
                    DefinitionId: identity.DefinitionId,
                    ArtifactVersion: identity.ArtifactVersion,
                    ArtifactHash: identity.ArtifactHash,
                    ExecutableNodeId: node.ExecutableNodeId,
                    StimulusType: descriptor.StimulusType,
                    StimulusHash: descriptor.StimulusHash,
                    CorrelationScope: descriptor.CorrelationScope,
                    Metadata: descriptor.Metadata,
                    CreatedAt: now));
            }
        }

        return bindings;
    }

    private ActivityTriggerStimulusResult Describe(ExecutableNode node)
    {
        // First provider that owns the activity type wins — even if it produces zero descriptors (a recognized
        // non-start). Only when NO provider recognizes the node do we fall through to NotRecognized (→ publish fails).
        foreach (var provider in _providers)
        {
            var result = provider.Describe(node);
            if (result.IsRecognized)
                return result;
        }

        return ActivityTriggerStimulusResult.NotRecognized;
    }

    private static bool IsTrigger(ExecutableNode node) =>
        node.Metadata.TryGetValue(TriggerNodeMetadata.ExecutionTypeKey, out var executionType) &&
        StringComparer.Ordinal.Equals(executionType, TriggerNodeMetadata.TriggerExecutionType);

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode root)
    {
        var stack = new Stack<ExecutableNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
