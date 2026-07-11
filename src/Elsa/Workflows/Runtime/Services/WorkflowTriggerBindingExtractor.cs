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
        try
        {
            return Evaluate(executable).Bindings;
        }
        catch (WorkflowTriggerPreflightException exception) when (
            exception.Facet == "ProviderRecognition" && exception.ProviderIds.Count == 0)
        {
            throw new WorkflowTriggerExtractionException(exception.ArtifactId, exception.ExecutableNodeId, exception.Message);
        }
    }

    public WorkflowTriggerPreflightOutcome Evaluate(WorkflowExecutable executable)
    {
        ArgumentNullException.ThrowIfNull(executable);

        var identity = executable.Identity;
        var now = _timeProvider.GetUtcNow();
        var outcomes = new List<WorkflowTriggerNodePreflightOutcome>();

        foreach (var node in Flatten(executable.RootActivity))
        {
            if (!IsTrigger(node))
                continue;

            var claims = new List<(IActivityTriggerStimulusProvider Provider, ActivityTriggerStimulusResult Result)>();
            foreach (var provider in _providers)
            {
                ActivityTriggerStimulusResult describedResult;
                try
                {
                    describedResult = provider.Describe(node);
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
                {
                    throw Failure(identity.ArtifactId, node, [provider.ProviderId], "ProviderRecognition",
                        $"Trigger provider '{provider.ProviderId}' could not describe node '{node.ExecutableNodeId}'.", exception);
                }

                if (describedResult.IsRecognized)
                    claims.Add((provider, describedResult));
            }

            if (claims.Count == 0)
                throw Failure(identity.ArtifactId, node, [], "ProviderRecognition",
                    $"No trigger stimulus provider recognizes node '{node.ExecutableNodeId}' (activity type '{node.ActivityType}').");

            var providerIds = claims.Select(x => x.Provider.ProviderId).Order(StringComparer.Ordinal).ToArray();
            if (providerIds.Any(string.IsNullOrWhiteSpace))
                throw Failure(identity.ArtifactId, node, providerIds, "ProviderIdentity",
                    $"A trigger stimulus provider recognizing node '{node.ExecutableNodeId}' has a blank provider id.");
            if (claims.Count != 1)
                throw Failure(identity.ArtifactId, node, providerIds, "ProviderRecognition",
                    $"Several trigger stimulus providers recognize node '{node.ExecutableNodeId}': {string.Join(", ", providerIds)}.");

            var (recognizingProvider, claimResult) = claims[0];
            var bindings = claimResult.Descriptors.Select(descriptor =>
                new WorkflowTriggerBinding(
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
                    CreatedAt: now)).ToArray();

            var duplicateBindingId = bindings.GroupBy(x => x.TriggerBindingId, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1)?.Key;
            if (duplicateBindingId is not null)
                throw Failure(identity.ArtifactId, node, [recognizingProvider.ProviderId], "BindingIdentity",
                    $"Trigger provider '{recognizingProvider.ProviderId}' produced duplicate binding id '{duplicateBindingId}'.");

            var status = bindings.Length == 0
                ? WorkflowTriggerPreflightStatus.IntentionallyNonStarting
                : WorkflowTriggerPreflightStatus.Registered;
            outcomes.Add(new WorkflowTriggerNodePreflightOutcome(node.ExecutableNodeId, node.ActivityType, recognizingProvider.ProviderId, status, bindings));
        }

        return new WorkflowTriggerPreflightOutcome(identity, outcomes);
    }

    private static WorkflowTriggerPreflightException Failure(string artifactId, ExecutableNode node, IReadOnlyCollection<string> providerIds, string facet, string message, Exception? innerException = null) =>
        new(artifactId, node.ExecutableNodeId, node.ActivityType, providerIds, facet, message, innerException);

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
