using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Handlers;

/// <summary>
/// Single fan-in handler that collects and validates every registered executable-compilation source.
/// </summary>
public sealed class CollectExecutableCompilation : IEventHandler<OnExecutableCompilationCollecting>
{
    private readonly SourceRegistration[] _sources;

    public CollectExecutableCompilation(IEnumerable<IExecutableCompilationSource> sources)
        : this(sources, [])
    {
    }

    public CollectExecutableCompilation(
        IEnumerable<IExecutableCompilationSource> sources,
        IEnumerable<IExecutableNodeMetadataSource> legacyMetadataSources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(legacyMetadataSources);

        var registrations = sources
            .Select(source => new SourceRegistration(
                SourceIdentity(source),
                source.GetContributionAsync))
            .Concat(legacyMetadataSources.Select(source => new SourceRegistration(
                SourceIdentity(source),
                async (context, cancellationToken) =>
                {
                    var metadata = await source.GetMetadataAsync(
                        new ExecutableNodeMetadataContext(context.Request, context.Source, context.RootActivity),
                        cancellationToken)
                        ?? throw new ArgumentException($"Executable metadata source '{SourceIdentity(source)}' returned null.");
                    foreach (var item in metadata)
                        ArgumentNullException.ThrowIfNull(item);

                    return new ExecutableCompilationContribution(
                        nodeMetadata: metadata.Select(item => new ExecutableNodeMetadataClaim(
                            item.ExecutableNodeId,
                            item.Key,
                            item.Value)).ToArray());
                })))
            .OrderBy(source => source.Identity, StringComparer.Ordinal)
            .ToArray();

        var duplicateIdentity = registrations
            .GroupBy(source => source.Identity, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;
        if (duplicateIdentity is not null)
            throw new ArgumentException($"Executable compilation source identity '{duplicateIdentity}' is registered more than once.", nameof(sources));

        _sources = registrations;
    }

    public async Task Handle(OnExecutableCompilationCollecting domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        var contributions = new List<ExecutableCompilationContribution>(_sources.Length);
        foreach (var source in _sources)
        {
            var contribution = await source.Read(domainEvent.Context, cancellationToken)
                ?? throw new ArgumentException($"Executable compilation source '{source.Identity}' returned null.");
            contributions.Add(contribution with { SourceIdentity = source.Identity });
        }

        Validate(domainEvent.Context.RootActivity, contributions);

        foreach (var contribution in NormalizeAndMerge(contributions))
            domainEvent.Contributions.Add(contribution);
    }

    private static IReadOnlyCollection<ExecutableCompilationContribution> NormalizeAndMerge(
        IReadOnlyCollection<ExecutableCompilationContribution> contributions)
    {
        var metadata = new HashSet<(string NodeId, string Key, string Value)>();
        var dependencies = new HashSet<(string NodeId, string ArtifactId, string ArtifactHash)>();
        var result = new List<ExecutableCompilationContribution>();

        foreach (var contribution in contributions.OrderBy(item => item.SourceIdentity, StringComparer.Ordinal))
        {
            var normalizedMetadata = contribution.NodeMetadata
                .OrderBy(item => item.ExecutableNodeId, StringComparer.Ordinal)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .ThenBy(item => item.Value, StringComparer.Ordinal)
                .Where(item => metadata.Add((item.ExecutableNodeId, item.Key, item.Value)))
                .ToArray();
            var normalizedDependencies = contribution.Dependencies
                .OrderBy(item => item.ExecutableNodeId, StringComparer.Ordinal)
                .ThenBy(item => item.ArtifactId, StringComparer.Ordinal)
                .ThenBy(item => item.ArtifactHash, StringComparer.Ordinal)
                .Where(item => dependencies.Add((item.ExecutableNodeId, item.ArtifactId, item.ArtifactHash)))
                .ToArray();

            if (normalizedMetadata.Length == 0 && normalizedDependencies.Length == 0)
                continue;

            result.Add(new ExecutableCompilationContribution(normalizedMetadata, normalizedDependencies)
            {
                SourceIdentity = contribution.SourceIdentity
            });
        }

        return result;
    }

    private static void Validate(
        ExecutableNode rootActivity,
        IReadOnlyCollection<ExecutableCompilationContribution> contributions)
    {
        var nodes = Flatten(rootActivity).ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal);
        var metadata = nodes.ToDictionary(
            item => item.Key,
            item => item.Value.Metadata.ToDictionary(
                entry => entry.Key,
                entry => new OwnedValue(entry.Value, $"compiled:{item.Key}"),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
        var dependenciesByNode = new Dictionary<string, OwnedDependency>(StringComparer.Ordinal);
        var artifactHashes = new Dictionary<string, OwnedValue>(StringComparer.Ordinal);

        foreach (var contribution in contributions.OrderBy(item => item.SourceIdentity, StringComparer.Ordinal))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(contribution.SourceIdentity);
            var sourceIdentity = contribution.SourceIdentity!;
            var metadataClaims = contribution.NodeMetadata.ToArray();
            foreach (var claim in metadataClaims)
            {
                ArgumentNullException.ThrowIfNull(claim);
                ArgumentException.ThrowIfNullOrWhiteSpace(claim.ExecutableNodeId);
                ArgumentException.ThrowIfNullOrWhiteSpace(claim.Key);
                ArgumentException.ThrowIfNullOrWhiteSpace(claim.Value);
            }

            foreach (var claim in metadataClaims
                         .OrderBy(item => item.ExecutableNodeId, StringComparer.Ordinal)
                         .ThenBy(item => item.Key, StringComparer.Ordinal)
                         .ThenBy(item => item.Value, StringComparer.Ordinal))
            {
                if (!metadata.TryGetValue(claim.ExecutableNodeId, out var nodeMetadata))
                    throw new ArgumentException($"Executable metadata contribution targets unknown node '{claim.ExecutableNodeId}'.");
                if (nodeMetadata.TryGetValue(claim.Key, out var existing))
                {
                    if (StringComparer.Ordinal.Equals(existing.Value, claim.Value))
                        continue;

                    throw Conflict(
                        $"Executable node '{claim.ExecutableNodeId}' metadata key '{claim.Key}' has unequal values",
                        existing.Owner,
                        sourceIdentity);
                }

                nodeMetadata.Add(claim.Key, new OwnedValue(claim.Value, sourceIdentity));
            }

            var dependencyClaims = contribution.Dependencies.ToArray();
            foreach (var claim in dependencyClaims)
            {
                ArgumentNullException.ThrowIfNull(claim);
                ArgumentException.ThrowIfNullOrWhiteSpace(claim.ExecutableNodeId);
                ArgumentException.ThrowIfNullOrWhiteSpace(claim.ArtifactId);
                ArgumentException.ThrowIfNullOrWhiteSpace(claim.ArtifactHash);
            }

            foreach (var claim in dependencyClaims
                         .OrderBy(item => item.ExecutableNodeId, StringComparer.Ordinal)
                         .ThenBy(item => item.ArtifactId, StringComparer.Ordinal)
                         .ThenBy(item => item.ArtifactHash, StringComparer.Ordinal))
            {
                if (!nodes.ContainsKey(claim.ExecutableNodeId))
                    throw new ArgumentException($"Executable dependency contribution targets unknown node '{claim.ExecutableNodeId}'.");
                if (artifactHashes.TryGetValue(claim.ArtifactId, out var artifact) &&
                    !StringComparer.Ordinal.Equals(artifact.Value, claim.ArtifactHash))
                {
                    throw Conflict(
                        $"Executable dependency artifact '{claim.ArtifactId}' has conflicting hashes",
                        artifact.Owner,
                        sourceIdentity);
                }

                artifactHashes.TryAdd(claim.ArtifactId, new OwnedValue(claim.ArtifactHash, sourceIdentity));

                if (dependenciesByNode.TryGetValue(claim.ExecutableNodeId, out var existing))
                {
                    if (StringComparer.Ordinal.Equals(existing.ArtifactId, claim.ArtifactId) &&
                        StringComparer.Ordinal.Equals(existing.ArtifactHash, claim.ArtifactHash))
                        continue;

                    throw Conflict(
                        $"Executable node '{claim.ExecutableNodeId}' has conflicting dependency claims",
                        existing.Owner,
                        sourceIdentity);
                }

                dependenciesByNode.Add(
                    claim.ExecutableNodeId,
                    new OwnedDependency(claim.ArtifactId, claim.ArtifactHash, sourceIdentity));
            }
        }
    }

    private static ArgumentException Conflict(string message, string firstOwner, string secondOwner)
    {
        var owners = new[] { firstOwner, secondOwner }.Order(StringComparer.Ordinal).ToArray();
        return new ArgumentException($"{message} from owners '{owners[0]}' and '{owners[1]}'.");
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

    private static string SourceIdentity(IExecutableCompilationSource source) =>
        source.GetType().FullName ?? source.GetType().Name;

    private static string SourceIdentity(IExecutableNodeMetadataSource source) =>
        source.GetType().FullName ?? source.GetType().Name;

    private sealed record OwnedValue(string Value, string Owner);
    private sealed record OwnedDependency(string ArtifactId, string ArtifactHash, string Owner);
    private sealed record SourceRegistration(
        string Identity,
        Func<ExecutableCompilationContext, CancellationToken, ValueTask<ExecutableCompilationContribution>> Read);
}
