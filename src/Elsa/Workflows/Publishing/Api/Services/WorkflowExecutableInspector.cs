using System.Text.Json;
using Elsa.Workflows.Publishing.Api.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Projects the content-addressed artifact + reference stores into the executables list and detail views the Studio
/// Executable Inspector consumes (plan §3, P1 bridge contract §4). Every projection is assembled purely from
/// <see cref="IWorkflowExecutableStore"/> and <see cref="IWorkflowExecutableSourceReferenceStore"/> — no
/// workflow-definition table is consulted, which is what lets an artifact be inspected wherever it was promoted.
/// </summary>
public sealed class WorkflowExecutableInspector(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore referenceStore,
    TimeProvider? timeProvider = null)
{
    private const string FlowchartStructureKind = "elsa.flowchart.structure";
    private const int LiteralPreviewLength = 80;
    private const int ExpressionPreviewLength = 80;
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Builds the executables list: artifact rows with references nested newest-first, filtered by
    /// <paramref name="scope"/>. An artifact appears only if it carries a live reference in the requested scope;
    /// with <paramref name="includeRetired"/>, retired references are surfaced (and artifacts whose only matching
    /// references are retired are included).
    /// </summary>
    public async Task<WorkflowExecutablesListView> ListAsync(
        WorkflowExecutableListScope scope = WorkflowExecutableListScope.Published,
        bool includeRetired = false,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var executables = await executableStore.ListAsync(cancellationToken);

        var rows = new List<WorkflowExecutableRowView>();
        foreach (var executable in executables)
        {
            var references = await referenceStore.ListByArtifactAsync(executable.Identity.ArtifactId, cancellationToken);
            var visible = SelectVisibleReferences(references, scope, includeRetired, now);
            if (visible.Count == 0)
                continue;

            var newest = visible[0];
            rows.Add(new WorkflowExecutableRowView(
                executable.Identity.ArtifactId,
                executable.Identity.ArtifactHash,
                executable.CreatedAt,
                executable.RootActivity.ActivityType,
                executable.RootActivity.ActivityTypeVersion,
                executable.Nodes.Count,
                executable.ResumeTargets.Count,
                newest.ArtifactVersion,
                newest.DefinitionId,
                newest.DefinitionVersionId,
                newest.PublishedAt,
                newest.DeletedAt,
                newest.SourceKind,
                newest.SourceId,
                newest.SourceVersion,
                visible.Select(WorkflowExecutableReferenceView.From).ToArray()));
        }

        var ordered = rows
            .OrderByDescending(row => row.PublishedAt ?? row.CreatedAt)
            .ThenBy(row => row.ArtifactId, StringComparer.Ordinal)
            .ToArray();

        return new WorkflowExecutablesListView(ordered);
    }

    /// <summary>
    /// Builds the detail view for one artifact: identity block, full Execution Material node tree, the chosen
    /// reference's layout, and the complete reference list. When <paramref name="sourceReferenceId"/> is supplied
    /// that reference is used for the layout (if it belongs to the artifact); otherwise the newest live reference
    /// is chosen, falling back to the newest reference overall. Returns null when the artifact does not exist.
    /// </summary>
    public async Task<WorkflowExecutableDetailView?> GetAsync(
        string artifactId,
        string? sourceReferenceId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var executable = await executableStore.FindAsync(artifactId, cancellationToken);
        if (executable is null)
            return null;

        var now = _timeProvider.GetUtcNow();
        var references = (await referenceStore.ListByArtifactAsync(artifactId, cancellationToken))
            .OrderByDescending(reference => reference.PublishedAt ?? reference.CreatedAt)
            .ThenBy(reference => reference.SourceReferenceId, StringComparer.Ordinal)
            .ToArray();

        var chosen = ChooseReference(references, sourceReferenceId, now);

        return new WorkflowExecutableDetailView(
            executable.Identity.ArtifactId,
            executable.Identity.ArtifactHash,
            executable.CreatedAt,
            executable.RootActivity.ActivityType,
            executable.RootActivity.ActivityTypeVersion,
            executable.Nodes.Count,
            executable.ResumeTargets.Count,
            ProjectNode(executable.RootActivity),
            chosen is null
                ? null
                : new WorkflowExecutableChosenReferenceView(
                    chosen.Value.reference.SourceReferenceId,
                    chosen.Value.selection,
                    chosen.Value.reference.Layout),
            references.Select(WorkflowExecutableReferenceView.From).ToArray());
    }

    /// <summary>
    /// Reference visibility for one artifact under a list scope: keeps references whose scope matches, then either
    /// only live ones (default) or all when retired references are requested. Ordered newest-first. An empty result
    /// means the artifact is hidden from this view.
    /// </summary>
    private static IReadOnlyList<WorkflowExecutableSourceReference> SelectVisibleReferences(
        IReadOnlyCollection<WorkflowExecutableSourceReference> references,
        WorkflowExecutableListScope scope,
        bool includeRetired,
        DateTimeOffset now)
    {
        var scopeFilter = ToReferenceScope(scope);

        return references
            .Where(reference => scopeFilter is null || reference.Scope == scopeFilter)
            // Retired references appear only when explicitly requested; expired references are never live and are
            // treated as retired for visibility (they are pending GC).
            .Where(reference => includeRetired || reference.IsLive(now))
            .OrderByDescending(reference => reference.PublishedAt ?? reference.CreatedAt)
            .ThenBy(reference => reference.SourceReferenceId, StringComparer.Ordinal)
            .ToArray();
    }

    private static WorkflowExecutableReferenceScope? ToReferenceScope(WorkflowExecutableListScope scope) =>
        scope switch
        {
            WorkflowExecutableListScope.Published => WorkflowExecutableReferenceScope.Published,
            WorkflowExecutableListScope.TestRuns => WorkflowExecutableReferenceScope.TestRun,
            _ => null
        };

    private static (WorkflowExecutableSourceReference reference, string selection)? ChooseReference(
        IReadOnlyList<WorkflowExecutableSourceReference> newestFirst,
        string? requestedReferenceId,
        DateTimeOffset now)
    {
        if (newestFirst.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(requestedReferenceId))
        {
            var requested = newestFirst.FirstOrDefault(reference =>
                string.Equals(reference.SourceReferenceId, requestedReferenceId, StringComparison.Ordinal));
            if (requested is not null)
                return (requested, "requested");
            // A ref hint that does not resolve for this artifact falls through to the default selection.
        }

        var newestLive = newestFirst.FirstOrDefault(reference => reference.IsLive(now));
        return newestLive is not null
            ? (newestLive, "newest-live")
            : (newestFirst[0], "newest");
    }

    private static WorkflowExecutableNodeView ProjectNode(ExecutableNode node) =>
        new(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.Structure?.Kind,
            node.InputBindings.Values
                .OrderBy(binding => binding.InputName, StringComparer.Ordinal)
                .Select(ProjectInputBinding)
                .ToArray(),
            node.ChildSlots
                .Select(slot => new WorkflowExecutableChildSlotView(
                    slot.Name,
                    slot.Activities.Select(ProjectNode).ToArray()))
                .ToArray())
        {
            Connections = ProjectConnections(node.Structure)
        };

    // Structure payloads can contain activity-owned runtime data, so the Inspector projects only the minimal
    // allowlisted canvas contract. Exact kind matching prevents similarly named custom structures from leaking.
    private static IReadOnlyList<WorkflowExecutableConnectionView>? ProjectConnections(ExecutableActivityStructure? structure)
    {
        if (structure is null || !StringComparer.Ordinal.Equals(structure.Kind, FlowchartStructureKind))
            return null;

        var payload = structure.Payload;
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("connections", out var connections) ||
            connections.ValueKind != JsonValueKind.Array)
            return [];

        var projected = new List<WorkflowExecutableConnectionView>();
        foreach (var connection in connections.EnumerateArray())
        {
            if (connection.ValueKind != JsonValueKind.Object)
                continue;

            var source = ProjectEndpoint(connection, "source");
            var target = ProjectEndpoint(connection, "target");
            if (source is null || target is null)
                continue;

            projected.Add(new WorkflowExecutableConnectionView(source, target, ProjectVertices(connection)));
        }

        return projected;
    }

    private static WorkflowExecutableConnectionEndpointView? ProjectEndpoint(JsonElement connection, string propertyName)
    {
        if (!connection.TryGetProperty(propertyName, out var endpoint) ||
            endpoint.ValueKind != JsonValueKind.Object ||
            !endpoint.TryGetProperty("nodeId", out var nodeIdElement) ||
            nodeIdElement.ValueKind != JsonValueKind.String)
            return null;

        var nodeId = nodeIdElement.GetString();
        if (string.IsNullOrWhiteSpace(nodeId))
            return null;

        string? port = null;
        if (endpoint.TryGetProperty("port", out var portElement) && portElement.ValueKind == JsonValueKind.String)
        {
            var candidate = portElement.GetString();
            if (!string.IsNullOrWhiteSpace(candidate))
                port = candidate.Trim();
        }

        return new WorkflowExecutableConnectionEndpointView(nodeId, port);
    }

    private static IReadOnlyList<WorkflowExecutableConnectionVertexView>? ProjectVertices(JsonElement connection)
    {
        if (!connection.TryGetProperty("vertices", out var vertices) || vertices.ValueKind != JsonValueKind.Array)
            return null;

        var projected = new List<WorkflowExecutableConnectionVertexView>();
        foreach (var vertex in vertices.EnumerateArray())
        {
            if (vertex.ValueKind != JsonValueKind.Object ||
                !vertex.TryGetProperty("x", out var xElement) ||
                !vertex.TryGetProperty("y", out var yElement) ||
                xElement.ValueKind != JsonValueKind.Number ||
                yElement.ValueKind != JsonValueKind.Number ||
                !xElement.TryGetDouble(out var x) ||
                !yElement.TryGetDouble(out var y) ||
                !double.IsFinite(x) ||
                !double.IsFinite(y))
                continue;

            projected.Add(new WorkflowExecutableConnectionVertexView(x, y));
        }

        return projected.Count == 0 ? null : projected;
    }

    private static WorkflowExecutableInputBindingView ProjectInputBinding(RuntimeInputBinding binding) =>
        new(binding.InputName, binding.Source.ToString(), SummarizeBinding(binding));

    // Compact, safe rendering of a binding for the Inspector. Literals/expressions are truncated previews;
    // reference-typed values expose only their type/id so secrets held as references are never inlined (plan §6).
    private static string? SummarizeBinding(RuntimeInputBinding binding) =>
        binding.Source switch
        {
            RuntimeInputBindingSource.Literal => Truncate(RenderLiteral(binding.LiteralValue), LiteralPreviewLength),
            RuntimeInputBindingSource.Expression => binding.Expression is null
                ? null
                : $"{binding.Expression.Language}: {Truncate(binding.Expression.Expression, ExpressionPreviewLength)}",
            RuntimeInputBindingSource.ActivityOutput => binding.ActivityOutput is null
                ? null
                : $"{binding.ActivityOutput.ProducerExecutableNodeId ?? "?"}.{binding.ActivityOutput.OutputName}",
            RuntimeInputBindingSource.DurableValue => binding.DurableValue is null ? null : $"value:{binding.DurableValue.ValueId}",
            RuntimeInputBindingSource.Reference => binding.Reference is null
                ? null
                : $"{binding.Reference.ReferenceType}:{binding.Reference.ReferenceId}",
            _ => null
        };

    private static string? RenderLiteral(JsonElement? literal) =>
        literal is null ? null : literal.Value.ValueKind == JsonValueKind.String ? literal.Value.GetString() : literal.Value.GetRawText();

    private static string? Truncate(string? value, int max) =>
        value is null || value.Length <= max ? value : value[..max] + "…";
}
