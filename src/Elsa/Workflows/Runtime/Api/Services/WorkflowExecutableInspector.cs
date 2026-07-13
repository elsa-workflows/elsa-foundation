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
        var referencesByArtifact = (await referenceStore.ListAsync(cancellationToken: cancellationToken))
            .GroupBy(reference => reference.ArtifactId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var items = new List<WorkflowExecutableSummaryView>();
        foreach (var executable in await executableStore.ListAsync(cancellationToken))
        {
            var references = referencesByArtifact.GetValueOrDefault(executable.Identity.ArtifactId) ?? [];
            var matching = references.Where(reference => MatchesScope(reference, scope)).ToArray();
            if (!includeRetired && matching.All(reference => !reference.IsLive(now)))
                continue;
            if (matching.Length == 0 && !retainedCounts.ContainsKey(executable.Identity.ArtifactId))
                continue;
            var ordered = OrderReferences(references).ToArray();
            var chosen = ordered.FirstOrDefault(reference => MatchesScope(reference, scope) && reference.IsLive(now))
                ?? ordered.FirstOrDefault(reference => MatchesScope(reference, scope));
            items.Add(Summary(
                executable,
                chosen,
                ordered,
                now,
                references.Count(reference => reference.IsLive(now)),
                retainedCounts.GetValueOrDefault(executable.Identity.ArtifactId)));
        }

        return new WorkflowExecutablesListView(items
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.ArtifactId, StringComparer.Ordinal)
            .ToArray());
    }

    public async ValueTask<WorkflowExecutableDetailsView?> GetAsync(
        string artifactId,
        string? sourceReferenceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        var executable = await executableStore.FindAsync(artifactId, cancellationToken);
        if (executable is null)
            return null;
        var now = _timeProvider.GetUtcNow();
        var references = await referenceStore.ListByArtifactAsync(artifactId, cancellationToken);
        var ordered = OrderReferences(references).ToArray();
        var requested = sourceReferenceId is null
            ? null
            : ordered.FirstOrDefault(reference => StringComparer.Ordinal.Equals(reference.SourceReferenceId, sourceReferenceId));
        var chosen = requested
            ?? ordered.FirstOrDefault(reference => reference.Scope == WorkflowExecutableReferenceScope.Published && reference.IsLive(now))
            ?? ordered.FirstOrDefault(reference => reference.IsLive(now))
            ?? ordered.FirstOrDefault();
        var retained = (await RetainedCountsAsync(cancellationToken)).GetValueOrDefault(artifactId);
        return new WorkflowExecutableDetailsView(
            artifactId,
            executable.Identity.ArtifactHash,
            executable.CreatedAt,
            executable.RootActivity.ActivityType,
            executable.RootActivity.ActivityTypeVersion,
            executable.Nodes.Count,
            executable.ResumeTargets.Count,
            references.Count(reference => reference.IsLive(now)),
            retained,
            Node(executable.RootActivity),
            executable.CompatibilityMetadata,
            chosen is null
                ? null
                : new WorkflowExecutableChosenReferenceView(
                    chosen.SourceReferenceId,
                    requested is null ? chosen.IsLive(now) ? "newest-live" : "newest" : "requested",
                    chosen.Layout),
            ordered.Select(reference => ExecutableSourceReferenceView.From(reference, now)).ToArray());
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

    private static WorkflowExecutableSummaryView Summary(
        WorkflowExecutable executable,
        WorkflowExecutableSourceReference? chosen,
        IReadOnlyCollection<WorkflowExecutableSourceReference> references,
        DateTimeOffset now,
        int liveReferences,
        int retainedExecutions) =>
        new(
            executable.Identity.ArtifactId,
            chosen?.ArtifactVersion ?? executable.Identity.ArtifactVersion,
            executable.Identity.ArtifactHash,
            chosen?.DefinitionId ?? executable.Identity.DefinitionId,
            chosen?.DefinitionVersionId ?? executable.Identity.DefinitionVersionId,
            executable.CreatedAt,
            chosen?.PublishedAt,
            chosen?.DeletedAt,
            chosen?.SourceKind,
            chosen?.SourceId,
            chosen?.SourceVersion,
            executable.RootActivity.ActivityType,
            executable.RootActivity.ActivityTypeVersion,
            executable.Nodes.Count,
            executable.ResumeTargets.Count,
            liveReferences,
            retainedExecutions,
            references.Select(reference => ExecutableSourceReferenceView.From(reference, now)).ToArray());

    private static IOrderedEnumerable<WorkflowExecutableSourceReference> OrderReferences(
        IEnumerable<WorkflowExecutableSourceReference> references) =>
        references
            .OrderByDescending(reference => reference.PublishedAt ?? reference.CreatedAt)
            .ThenBy(reference => reference.SourceReferenceId, StringComparer.Ordinal);

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
