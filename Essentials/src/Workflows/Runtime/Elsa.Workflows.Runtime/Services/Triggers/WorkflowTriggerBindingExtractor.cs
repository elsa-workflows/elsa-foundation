using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Triggers;

/// <summary>
/// Default <see cref="IWorkflowTriggerBindingExtractor"/>. It walks the published executable's node
/// tree, selects the nodes the compiler marked as start-triggers (via
/// <see cref="TriggerNodeMetadata.ExecutionTypeKey"/>), and resolves each one's stimulus identity through the
/// registered <see cref="IActivityTriggerStimulusProvider"/> set — deriving the durable trigger index over the
/// pinned, published artifact rather than the mutable authored definition.
/// <para>
/// Every node that produces start bindings must also lie on the start path (spec 238). The extractor walks from the
/// root activity to the node and asks the <see cref="IStructuralStartEntryProvider"/> registered for each composite's
/// structure kind which child a run started by that trigger begins at. A composite that does not begin at the next
/// node of the path, has no structure, or has no provider for its kind fails the preflight with
/// <see cref="WorkflowTriggerPreflightException.PlacementFacet"/>. A node that is intentionally non-starting is a
/// mid-flow wait and is not checked.
/// </para>
/// </summary>
public sealed class WorkflowTriggerBindingExtractor : IWorkflowTriggerBindingExtractor
{
    private const string StartEntryProviderIdentityFacet = "StartEntryProviderIdentity";
    private const string StartEntryRecognitionFacet = "StartEntryRecognition";

    private readonly IReadOnlyList<IActivityTriggerStimulusProvider> _providers;
    private readonly ILookup<string, IStructuralStartEntryProvider> _startEntryProviders;
    private readonly TimeProvider _timeProvider;

    public WorkflowTriggerBindingExtractor(IEnumerable<IActivityTriggerStimulusProvider> providers)
        : this(providers, [], TimeProvider.System)
    {
    }

    public WorkflowTriggerBindingExtractor(IEnumerable<IActivityTriggerStimulusProvider> providers, TimeProvider timeProvider)
        : this(providers, [], timeProvider)
    {
    }

    public WorkflowTriggerBindingExtractor(
        IEnumerable<IActivityTriggerStimulusProvider> providers,
        IEnumerable<IStructuralStartEntryProvider> startEntryProviders)
        : this(providers, startEntryProviders, TimeProvider.System)
    {
    }

    public WorkflowTriggerBindingExtractor(
        IEnumerable<IActivityTriggerStimulusProvider> providers,
        IEnumerable<IStructuralStartEntryProvider> startEntryProviders,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(startEntryProviders);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _providers = providers.ToArray();
        var startEntryProviderList = startEntryProviders.ToArray();
        if (startEntryProviderList.FirstOrDefault(provider => string.IsNullOrWhiteSpace(provider.StructureKind)) is { } blank)
            throw new ArgumentException($"Start-entry provider '{blank.GetType().FullName}' has a blank structure kind.", nameof(startEntryProviders));
        _startEntryProviders = startEntryProviderList.ToLookup(provider => provider.StructureKind, StringComparer.Ordinal);
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
        var parents = IndexParents(executable.RootActivity);

        foreach (var node in Flatten(executable.RootActivity))
        {
            if (!IsTrigger(node))
                continue;

            var claims = new List<(string ProviderId, TriggerCardinality Cardinality, ActivityTriggerStimulusResult Result)>();
            foreach (var provider in _providers)
            {
                var providerId = provider.ProviderId;
                var providerType = provider.GetType().FullName ?? provider.GetType().Name;
                ActivityTriggerStimulusResult describedResult;
                try
                {
                    describedResult = provider.Describe(node);
                }
                catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
                {
                    if (string.IsNullOrWhiteSpace(providerId))
                        throw Failure(identity.ArtifactId, node, [], "ProviderIdentity",
                            $"Trigger provider type '{providerType}' has a blank provider id and failed while describing node '{node.ExecutableNodeId}'.", exception);

                    throw Failure(identity.ArtifactId, node, [providerId], "ProviderRecognition",
                        $"Trigger provider '{providerId}' could not describe node '{node.ExecutableNodeId}'.", exception);
                }

                if (describedResult.IsRecognized)
                {
                    if (string.IsNullOrWhiteSpace(providerId))
                        throw Failure(identity.ArtifactId, node, [], "ProviderIdentity",
                            $"Trigger provider type '{providerType}' recognizes node '{node.ExecutableNodeId}' but has a blank provider id.");

                    claims.Add((providerId, provider.Cardinality, describedResult));
                }
            }

            if (claims.Count == 0)
                throw Failure(identity.ArtifactId, node, [], "ProviderRecognition",
                    $"No trigger stimulus provider recognizes node '{node.ExecutableNodeId}' (activity type '{node.ActivityType}').");

            var providerIds = claims.Select(x => x.ProviderId).Order(StringComparer.Ordinal).ToArray();
            if (claims.Count != 1)
                throw Failure(identity.ArtifactId, node, providerIds, "ProviderRecognition",
                    $"Several trigger stimulus providers recognize node '{node.ExecutableNodeId}': {string.Join(", ", providerIds)}.");

            var (recognizingProviderId, cardinality, claimResult) = claims[0];
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
                    CreatedAt: now,
                    Cardinality: cardinality)).ToArray();

            var duplicateBindingId = bindings.GroupBy(x => x.TriggerBindingId, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1)?.Key;
            if (duplicateBindingId is not null)
                throw Failure(identity.ArtifactId, node, [recognizingProviderId], "BindingIdentity",
                    $"Trigger provider '{recognizingProviderId}' produced duplicate binding id '{duplicateBindingId}'.");

            var status = bindings.Length == 0
                ? WorkflowTriggerPreflightStatus.IntentionallyNonStarting
                : WorkflowTriggerPreflightStatus.Registered;
            if (status == WorkflowTriggerPreflightStatus.Registered)
                EnsureOnStartPath(identity.ArtifactId, node, recognizingProviderId, parents);
            outcomes.Add(new WorkflowTriggerNodePreflightOutcome(node.ExecutableNodeId, node.ActivityType, recognizingProviderId, status, bindings));
        }

        return new WorkflowTriggerPreflightOutcome(identity, outcomes);
    }

    /// <summary>
    /// Fails the preflight unless every composite from the root down to <paramref name="trigger"/> begins at the next
    /// node of that path for a run started by this trigger. The first composite from the root that does not is named.
    /// </summary>
    private void EnsureOnStartPath(
        string artifactId,
        ExecutableNode trigger,
        string providerId,
        IReadOnlyDictionary<string, ExecutableNode> parents)
    {
        var path = new List<ExecutableNode> { trigger };
        for (var current = trigger; parents.TryGetValue(current.ExecutableNodeId, out var parent); current = parent)
            path.Add(parent);
        path.Reverse();

        for (var index = 0; index < path.Count - 1; index++)
        {
            var composite = path[index];
            var next = path[index + 1];
            var structureKind = composite.Structure?.Kind;
            var startChild = structureKind is null
                ? null
                : SelectStartChild(artifactId, trigger, providerId, composite, structureKind);
            if (startChild is not null && StringComparer.Ordinal.Equals(startChild.ExecutableNodeId, next.ExecutableNodeId))
                continue;

            throw new WorkflowTriggerPreflightException(
                artifactId,
                trigger.ExecutableNodeId,
                trigger.ActivityType,
                [providerId],
                composite.ExecutableNodeId,
                structureKind,
                $"Start trigger '{trigger.ExecutableNodeId}' ({trigger.ActivityType}) cannot start this workflow: " +
                $"'{composite.ExecutableNodeId}' ({composite.ActivityType}) does not begin at it. Place it at an entry " +
                "position: the root activity, the first activity of a Sequence on the start path, or a Flowchart " +
                "activity with no incoming connection. If it should wait mid-flow instead, set CanStartWorkflow to " +
                "false, or use Delay instead of Timer or Cron.");
        }
    }

    /// <summary>
    /// Asks the provider registered for <paramref name="structureKind"/> where a run started by this trigger enters
    /// <paramref name="composite"/>; <c>null</c> when no provider answers for the kind.
    /// </summary>
    private ExecutableNode? SelectStartChild(string artifactId, ExecutableNode trigger, string providerId, ExecutableNode composite, string structureKind)
    {
        var candidates = _startEntryProviders[structureKind].ToArray();
        if (candidates.Length > 1)
            throw Failure(artifactId, trigger, [providerId], StartEntryProviderIdentityFacet,
                $"Several start-entry providers answer for structure kind '{structureKind}': " +
                $"{string.Join(", ", candidates.Select(candidate => candidate.GetType().FullName).Order(StringComparer.Ordinal))}.");

        if (candidates.Length == 0)
            return null;

        try
        {
            return candidates[0].SelectStartChild(composite, trigger.ExecutableNodeId);
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw Failure(artifactId, trigger, [providerId], StartEntryRecognitionFacet,
                $"The start-entry provider for structure kind '{structureKind}' could not read composite '{composite.ExecutableNodeId}' on the path to start trigger '{trigger.ExecutableNodeId}'.",
                exception);
        }
    }

    private static Dictionary<string, ExecutableNode> IndexParents(ExecutableNode root)
    {
        var parents = new Dictionary<string, ExecutableNode>(StringComparer.Ordinal);
        foreach (var node in Flatten(root))
        foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities))
            parents.TryAdd(child.ExecutableNodeId, node);
        return parents;
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
