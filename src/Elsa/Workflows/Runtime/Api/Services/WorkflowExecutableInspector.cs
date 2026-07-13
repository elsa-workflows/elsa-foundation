using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>Runtime-owned, Design-independent inspection of immutable executable artifacts and their roots.</summary>
public sealed class WorkflowExecutableInspector(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore referenceStore,
    IWorkflowExecutionStateStore executionStore,
    TimeProvider? timeProvider = null)
{
    private const int PreviewLength = 80;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public async ValueTask<WorkflowExecutablesListView> ListAsync(
        WorkflowExecutableListScope scope = WorkflowExecutableListScope.Published,
        bool includeRetired = false,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var retainedCounts = await RetainedCountsAsync(cancellationToken);
        var items = new List<WorkflowExecutableSummaryView>();
        foreach (var executable in await executableStore.ListAsync(cancellationToken))
        {
            var references = await referenceStore.ListByArtifactAsync(executable.Identity.ArtifactId, cancellationToken);
            var matching = references.Where(reference => MatchesScope(reference, scope)).ToArray();
            if (!includeRetired && matching.All(reference => !reference.IsLive(now)))
                continue;
            if (matching.Length == 0 && !retainedCounts.ContainsKey(executable.Identity.ArtifactId))
                continue;
            items.Add(Summary(executable, references.Count(reference => reference.IsLive(now)), retainedCounts.GetValueOrDefault(executable.Identity.ArtifactId)));
        }

        return new WorkflowExecutablesListView(items
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.ArtifactId, StringComparer.Ordinal)
            .ToArray());
    }

    public async ValueTask<WorkflowExecutableDetailsView?> GetAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        var executable = await executableStore.FindAsync(artifactId, cancellationToken);
        if (executable is null)
            return null;
        var now = _timeProvider.GetUtcNow();
        var references = await referenceStore.ListByArtifactAsync(artifactId, cancellationToken);
        var retained = (await RetainedCountsAsync(cancellationToken)).GetValueOrDefault(artifactId);
        return new WorkflowExecutableDetailsView(
            artifactId,
            executable.Identity.ArtifactHash,
            executable.CreatedAt,
            references.Count(reference => reference.IsLive(now)),
            retained,
            Node(executable.RootActivity),
            executable.CompatibilityMetadata);
    }

    public async ValueTask<ExecutableProvenanceView?> GetProvenanceAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        if (await executableStore.FindAsync(artifactId, cancellationToken) is null)
            return null;
        var now = _timeProvider.GetUtcNow();
        var references = (await referenceStore.ListByArtifactAsync(artifactId, cancellationToken))
            .OrderByDescending(reference => reference.CreatedAt)
            .ThenBy(reference => reference.SourceReferenceId, StringComparer.Ordinal)
            .ToArray();
        var retained = (await RetainedCountsAsync(cancellationToken)).GetValueOrDefault(artifactId);
        return new ExecutableProvenanceView(
            artifactId,
            references.Select(reference => ExecutableSourceReferenceView.From(reference, now)).ToArray(),
            retained,
            retained > 0 || references.Any(reference => reference.IsLive(now)));
    }

    private async ValueTask<IReadOnlyDictionary<string, int>> RetainedCountsAsync(CancellationToken cancellationToken) =>
        (await executionStore.ListAsync(cancellationToken))
        .GroupBy(state => state.PinnedExecutable.ArtifactId, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    private static bool MatchesScope(WorkflowExecutableSourceReference reference, WorkflowExecutableListScope scope) => scope switch
    {
        WorkflowExecutableListScope.Published => reference.Scope == WorkflowExecutableReferenceScope.Published,
        WorkflowExecutableListScope.TestRuns => reference.Scope == WorkflowExecutableReferenceScope.TestRun,
        _ => true
    };

    private static WorkflowExecutableSummaryView Summary(WorkflowExecutable executable, int liveReferences, int retainedExecutions) =>
        new(
            executable.Identity.ArtifactId,
            executable.Identity.ArtifactHash,
            executable.CreatedAt,
            executable.RootActivity.ActivityType,
            executable.Nodes.Count,
            liveReferences,
            retainedExecutions);

    private static WorkflowExecutableNodeView Node(ExecutableNode node) =>
        new(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.Structure?.Kind,
            node.InputBindings.Values.OrderBy(binding => binding.InputName, StringComparer.Ordinal).Select(Binding).ToArray(),
            node.ChildSlots.Select(slot => new WorkflowExecutableChildSlotView(slot.Name, slot.Activities.Select(Node).ToArray())).ToArray());

    private static WorkflowExecutableInputBindingView Binding(RuntimeInputBinding binding) =>
        new(binding.InputName, binding.Source.ToString(), Preview(binding));

    private static string? Preview(RuntimeInputBinding binding)
    {
        var text = binding.Source switch
        {
            RuntimeInputBindingSource.Literal when binding.LiteralValue is { } value => value.GetRawText(),
            RuntimeInputBindingSource.Expression when binding.Expression is { } expression => $"{expression.Language}: {expression.Expression}",
            RuntimeInputBindingSource.ActivityOutput => binding.ActivityOutput?.OutputName,
            RuntimeInputBindingSource.DurableValue => binding.DurableValue?.ValueId,
            RuntimeInputBindingSource.Reference => binding.Reference?.ReferenceId,
            _ => null
        };
        return text is null || text.Length <= PreviewLength ? text : $"{text[..PreviewLength]}…";
    }
}
