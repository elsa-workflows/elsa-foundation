using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Workflows.Runtime.Distributed.Placement;

/// <summary>
/// The work this shell's placement pump found waiting because no active member satisfies its placement requirement, or
/// because the requirement could not be resolved (spec 184, FR-017), per persistence scope. The pump refreshes it every
/// sweep, so an execution that is claimed, or no longer pending, leaves it at the next one.
/// </summary>
/// <remarks>
/// A newly unplaceable execution is logged once, as a warning naming what it needs and never a host. The registry is a
/// report, never the record of what is owed: the work stays in the durable transport, and the first capable member to
/// become active claims it (FR-018).
/// </remarks>
public sealed class UnplaceableWorkRegistry(TimeProvider timeProvider, ILogger<UnplaceableWorkRegistry>? logger = null)
{
    private readonly ILogger _logger = logger ?? NullLogger<UnplaceableWorkRegistry>.Instance;
    private readonly ConcurrentDictionary<(string Scope, string WorkflowExecutionId), Entry> _entries = new();

    /// <summary>Records <paramref name="workflowExecutionId"/> as waiting for <paramref name="requirement"/>.</summary>
    public void Record(string scope, string workflowExecutionId, string requirement)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirement);
        var now = timeProvider.GetUtcNow();
        var key = (scope, workflowExecutionId);
        if (_entries.TryGetValue(key, out var existing) && string.Equals(existing.Requirement, requirement, StringComparison.Ordinal))
        {
            _entries[key] = existing with { LastObservedAt = now };
            return;
        }

        _entries[key] = new Entry(requirement, now, now);
        _logger.LogWarning(
            "Workflow execution {WorkflowExecutionId} is waiting in the durable transport: no active member can run it ({Requirement}). It is claimed as soon as a capable member is active.",
            workflowExecutionId,
            requirement);
    }

    /// <summary>Forgets <paramref name="workflowExecutionId"/>: it was claimed, or a capable member exists.</summary>
    public void Clear(string scope, string workflowExecutionId) => _entries.TryRemove((scope, workflowExecutionId), out _);

    /// <summary>Forgets every execution of <paramref name="scope"/> the latest sweep did not see pending and refused.</summary>
    public void Retain(string scope, IReadOnlySet<string> stillWaiting)
    {
        foreach (var key in _entries.Keys.Where(key => StringComparer.Ordinal.Equals(key.Scope, scope) && !stillWaiting.Contains(key.WorkflowExecutionId)))
            _entries.TryRemove(key, out _);
    }

    /// <summary>The reports of <paramref name="scope"/>, one per requirement, counting the executions waiting for it.</summary>
    public IReadOnlyCollection<UnplaceableWorkReport> Reports(string scope) =>
        _entries
            .Where(pair => StringComparer.Ordinal.Equals(pair.Key.Scope, scope))
            .GroupBy(pair => pair.Value.Requirement, StringComparer.Ordinal)
            .Select(group => new UnplaceableWorkReport(
                group.Key,
                group.Count(),
                group.Min(pair => pair.Value.FirstObservedAt),
                group.Max(pair => pair.Value.LastObservedAt)))
            .OrderBy(report => report.Requirement, StringComparer.Ordinal)
            .ToArray();

    private sealed record Entry(string Requirement, DateTimeOffset FirstObservedAt, DateTimeOffset LastObservedAt);
}

/// <summary>The distributed runtime's report of unplaceable work to the runtime's Attention contributor (spec 184, FR-017).</summary>
public sealed class UnplaceableWorkAttention(
    UnplaceableWorkRegistry registry,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowRuntimePlacementAttention
{
    public ValueTask<IReadOnlyCollection<UnplaceableWorkReport>> ListUnplaceableWorkAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(accessContextAccessor.Current.Scope is { } scope
            ? registry.Reports(scope.Value)
            : (IReadOnlyCollection<UnplaceableWorkReport>)[]);
    }
}
