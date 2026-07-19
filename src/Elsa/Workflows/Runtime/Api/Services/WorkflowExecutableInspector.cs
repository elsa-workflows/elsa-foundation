using System.Text.Json;
using Elsa.Primitives.Exceptions;
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
        if (sourceReferenceId is not null && (requested is null || !requested.IsLive(now)))
            throw EntityNotFoundException.ForEntity(typeof(WorkflowExecutableSourceReference), sourceReferenceId);
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
            Node(executable.RootActivity, includeSourceDetails: false),
            executable.CompatibilityMetadata,
            chosen is null
                ? null
                : new WorkflowExecutableChosenReferenceView(
                    chosen.SourceReferenceId,
                    requested is null ? chosen.IsLive(now) ? "newest-live" : "newest" : "requested",
                    chosen.Layout),
            ordered.Select(reference => ExecutableSourceReferenceView.From(reference, now)).ToArray())
        {
            InputContract = InputContract(executable.InputContract),
            Dependencies = executable.Dependencies
                .OrderBy(dependency => dependency.ArtifactId, StringComparer.Ordinal)
                .Select(dependency => new WorkflowExecutableDependencyView(
                    dependency.ArtifactId,
                    dependency.ArtifactHash,
                    dependency.DispatchNodeIds.Order(StringComparer.Ordinal).ToArray()))
                .ToArray()
        };
    }

    public async ValueTask<WorkflowExecutableInputSourcesView?> GetInputSourcesAsync(
        string artifactId,
        string sourceReferenceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceReferenceId);

        var executable = await executableStore.FindAsync(artifactId, cancellationToken);
        var reference = await referenceStore.FindAsync(sourceReferenceId, cancellationToken);
        if (executable is null || reference is null || !StringComparer.Ordinal.Equals(reference.ArtifactId, artifactId))
            return null;

        var authoredInputs = reference.AuthoredInputs.Select(input => new WorkflowExecutableAuthoredInputView(
            input.ExecutableNodeId,
            input.InputKey,
            input.ExpressionType,
            input.IsSensitive ? null : input.Value,
            input.IsSensitive,
            input.IsSensitive ? "redacted" : "allowed")).ToArray();
        var compiledInputs = executable.Nodes
            .SelectMany(node => node.InputBindings.Values.Select(binding => new WorkflowExecutableCompiledInputView(
                node.ExecutableNodeId,
                Binding(binding, includeSourceDetails: !binding.EffectivePolicy.IsSensitive),
                binding.EffectivePolicy.IsSensitive ? "redacted" : "allowed")))
            .ToArray();

        return new WorkflowExecutableInputSourcesView(
            artifactId,
            sourceReferenceId,
            "allowed",
            authoredInputs,
            compiledInputs);
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

    private static WorkflowExecutableNodeView Node(ExecutableNode node, bool includeSourceDetails) =>
        new(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.Structure?.Kind,
            node.InputBindings.Values.OrderBy(binding => binding.InputName, StringComparer.Ordinal).Select(binding => Binding(binding, includeSourceDetails)).ToArray(),
            node.ChildSlots.Select(slot => new WorkflowExecutableChildSlotView(slot.Name, slot.Activities.Select(child => Node(child, includeSourceDetails)).ToArray())).ToArray(),
            ProjectConnections(node),
            node.OutputCaptures.Values
                .OrderBy(capture => capture.OutputName, StringComparer.Ordinal)
                .Select(OutputCapture)
                .ToArray());

    private static WorkflowExecutableOutputCaptureView OutputCapture(RuntimeOutputCapture capture) =>
        new(
            capture.OutputName,
            capture.ValueId,
            capture.Type,
            capture.Lifecycle.ToString(),
            capture.Storage.ToString(),
            capture.StorageDriverKey,
            capture.CaptureOnSuccessfulCompletion,
            capture.ConversionPlan,
            capture.Metadata);

    // The immutable executable structure is activity-owned, so Runtime API does not deserialize it through an
    // activity-module type. It projects only the compact endpoint shape understood by inspection clients and skips
    // malformed entries rather than leaking the complete compiled structure payload.
    private static IReadOnlyCollection<WorkflowExecutableConnectionView> ProjectConnections(ExecutableNode node)
    {
        if (node.Structure?.Payload is not { ValueKind: JsonValueKind.Object } payload ||
            !payload.TryGetProperty("connections", out var connections) ||
            connections.ValueKind != JsonValueKind.Array)
            return [];

        return connections.EnumerateArray()
            .Select(ProjectConnection)
            .Where(connection => connection is not null)
            .Cast<WorkflowExecutableConnectionView>()
            .ToArray();
    }

    private static WorkflowExecutableConnectionView? ProjectConnection(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("source", out var sourceValue) ||
            !value.TryGetProperty("target", out var targetValue))
            return null;

        var source = ProjectEndpoint(sourceValue);
        var target = ProjectEndpoint(targetValue);
        return source is null || target is null ? null : new(source, target);
    }

    private static WorkflowExecutableConnectionEndpointView? ProjectEndpoint(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("nodeId", out var nodeIdValue) ||
            nodeIdValue.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(nodeIdValue.GetString()))
            return null;

        var port = value.TryGetProperty("port", out var portValue) && portValue.ValueKind == JsonValueKind.String
            ? portValue.GetString()
            : null;
        return new(nodeIdValue.GetString()!, port);
    }

    private static WorkflowExecutableInputBindingView Binding(RuntimeInputBinding binding, bool includeSourceDetails) =>
        new(
            binding.InputName,
            binding.Source.ToString(),
            includeSourceDetails ? Preview(binding) : null,
            binding.InputKey,
            binding.EffectivePolicy.IsSensitive,
            includeSourceDetails ? binding.LiteralValue : null,
            includeSourceDetails ? binding.Expression : null,
            includeSourceDetails ? binding.WorkflowRequest : null,
            includeSourceDetails ? binding.Variable : null,
            includeSourceDetails ? binding.ActivityResult : null,
            includeSourceDetails ? binding.ConversionPlan : null,
            includeSourceDetails ? binding.Metadata : null);

    private static WorkflowExecutableInputContractView? InputContract(WorkflowExecutableInputContract? contract) =>
        contract is null
            ? null
            : new WorkflowExecutableInputContractView(
                contract.Version,
                contract.Inputs
                    .OrderBy(input => input.Name, StringComparer.Ordinal)
                    .Select(input => new WorkflowExecutableDeclaredInputView(
                        input.Name,
                        input.Type,
                        input.IsRequired,
                        input.DefaultValue?.Clone()))
                    .ToArray());

    private static string? Preview(RuntimeInputBinding binding)
    {
        var text = binding.Source switch
        {
            RuntimeInputBindingSource.Literal when binding.LiteralValue is { } value => value.GetRawText(),
            RuntimeInputBindingSource.Expression when binding.Expression is { } expression => $"{expression.Language}: {expression.Expression}",
            RuntimeInputBindingSource.WorkflowRequest => binding.WorkflowRequest?.MemberKey,
            RuntimeInputBindingSource.VariableRead => binding.Variable?.VariableKey,
            RuntimeInputBindingSource.ActivityResult => binding.ActivityResult?.ProjectionKey,
            _ => null
        };
        return text is null || text.Length <= PreviewLength ? text : $"{text[..PreviewLength]}…";
    }
}
