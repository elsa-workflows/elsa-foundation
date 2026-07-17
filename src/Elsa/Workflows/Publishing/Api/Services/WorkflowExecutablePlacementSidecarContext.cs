using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>Scoped handoff of non-behavioral placed layout from compilation to Source Reference creation.</summary>
public sealed class WorkflowExecutablePlacementSidecarContext
{
    private readonly ConcurrentDictionary<string, ExecutableLayoutSidecar> _values = new(StringComparer.Ordinal);

    public void Set(string definitionVersionId, IEnumerable<ExecutableLayoutBoundarySegment> segments) =>
        _values[definitionVersionId] = new(segments.ToArray());

    public ExecutableLayoutSidecar Get(string definitionVersionId) =>
        _values.GetValueOrDefault(definitionVersionId, ExecutableLayoutSidecar.Empty);
}
