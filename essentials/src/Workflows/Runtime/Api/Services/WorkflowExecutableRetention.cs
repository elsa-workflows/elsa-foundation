using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Services;

internal interface IWorkflowExecutableRetentionReader
{
    ValueTask<WorkflowExecutableRetention> ReadAsync(WorkflowExecutableRetentionQuery query, CancellationToken cancellationToken);
}

internal readonly record struct WorkflowExecutableRetentionQuery(string? ArtifactId, IReadOnlyCollection<string>? Definitions,
    WorkflowExecutableSourceReference? Source);

internal sealed class WorkflowExecutableRetention
{
    private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

    internal bool IsSourcePinned { get; private set; }

    internal int CountFor(string artifactId) => _counts.GetValueOrDefault(artifactId);

    internal void Add(string artifactId, bool pinsSource)
    {
        _counts[artifactId] = checked(CountFor(artifactId) + 1);
        IsSourcePinned |= pinsSource;
    }
}

internal sealed class LegacyWorkflowExecutableRetentionReader(IWorkflowExecutionStateStore? executions = null) : IWorkflowExecutableRetentionReader
{
    public async ValueTask<WorkflowExecutableRetention> ReadAsync(WorkflowExecutableRetentionQuery query, CancellationToken cancellationToken)
    {
        var retention = new WorkflowExecutableRetention();
        if (executions is null) return retention;

        foreach (var execution in await executions.ListAsync(cancellationToken))
        {
            var pinnedArtifact = execution.PinnedExecutable.ArtifactId;
            if (query.ArtifactId is null || pinnedArtifact == query.ArtifactId)
                retention.Add(pinnedArtifact, query.Source is { } source && pinnedArtifact == source.ArtifactId &&
                    execution.PinnedSource?.SourceReferenceId == source.SourceReferenceId);
        }
        return retention;
    }
}
