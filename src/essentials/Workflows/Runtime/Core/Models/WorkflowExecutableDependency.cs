namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// An immutable direct edge from an executable artifact to one exact child artifact.
/// </summary>
/// <remarks>
/// Source-reference and publication facts are deliberately excluded. The child identity and the executable
/// nodes that dispatch it are behavioral data and therefore participate in the parent artifact identity.
/// </remarks>
public sealed class WorkflowExecutableDependency
{
    public WorkflowExecutableDependency(
        string artifactId,
        string artifactHash,
        IReadOnlyCollection<string> dispatchNodeIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactHash);
        ArgumentNullException.ThrowIfNull(dispatchNodeIds);

        var nodeIds = dispatchNodeIds
            .Select(nodeId =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
                return nodeId;
            })
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (nodeIds.Length == 0)
            throw new ArgumentException("An executable dependency must bind at least one dispatch node.", nameof(dispatchNodeIds));

        ArtifactId = artifactId;
        ArtifactHash = artifactHash;
        DispatchNodeIds = Array.AsReadOnly(nodeIds);
    }

    /// <summary>The exact content-addressed child artifact ID.</summary>
    public string ArtifactId { get; }

    /// <summary>The full immutable behavioral hash of the child artifact.</summary>
    public string ArtifactHash { get; }

    /// <summary>The canonical set of executable nodes that dispatch this child.</summary>
    public IReadOnlyCollection<string> DispatchNodeIds { get; }
}
