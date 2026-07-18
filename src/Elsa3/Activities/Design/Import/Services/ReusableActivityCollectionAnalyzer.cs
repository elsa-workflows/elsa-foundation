using System.Text.Json;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Models;

namespace Elsa3.Activities.Design.Import.Services;

public sealed class ReusableActivityCollectionAnalyzer : IReusableActivityCollectionAnalyzer
{
    public ValueTask<ReusableActivityImportPlan> AnalyzeAsync(
        ReusableActivityImportCollection collection,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection);
        cancellationToken.ThrowIfCancellationRequested();

        var diagnostics = new List<Elsa3MigrationDiagnostic>();
        var orderedSources = collection.Definitions
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ThenBy(x => x.DefinitionId, StringComparer.Ordinal)
            .ThenBy(x => x.Version)
            .ToArray();
        var byVersionId = new Dictionary<string, Elsa3WorkflowDefinition>(StringComparer.Ordinal);
        foreach (var group in orderedSources.GroupBy(x => x.Id, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(group.Key) || group.Count() != 1)
            {
                diagnostics.Add(Diagnostic(
                    ReusableActivityImportDiagnosticCodes.DuplicateSourceVersion,
                    $"Elsa 3 source version identity '{group.Key}' is blank or duplicated.",
                    group.Key,
                    metadata: new Dictionary<string, string> { ["count"] = group.Count().ToString() }));
                continue;
            }

            byVersionId.Add(group.Key, group.Single());
        }

        var sources = byVersionId.Values
            .OrderBy(x => x.Id, StringComparer.Ordinal)
            .ThenBy(x => x.DefinitionId, StringComparer.Ordinal)
            .ThenBy(x => x.Version)
            .ToArray();

        var reusable = sources.Where(IsReusable).ToArray();
        var reusableByDefinition = reusable
            .GroupBy(x => x.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.Version).ThenBy(y => y.Id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);
        var reusableByVersion = reusable
            .Where(x => !string.IsNullOrWhiteSpace(x.Id))
            .GroupBy(x => x.Id, StringComparer.Ordinal)
            .Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single(), StringComparer.Ordinal);
        var allByDefinition = sources
            .GroupBy(x => x.DefinitionId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.Version).ThenBy(y => y.Id, StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

        var items = new List<ReusableActivityImportItem>(sources.Length);
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var isReusable = IsReusable(source);
            var itemDiagnostics = new List<Elsa3MigrationDiagnostic>();
            var dependencies = new List<ReusableActivityDependency>();
            var rewrites = new List<ReusableActivityReferenceRewrite>();
            var directStarts = new List<ReusableActivityDirectStart>();
            var nodePaths = new Dictionary<string, string>(StringComparer.Ordinal);

            if (source.Root is null)
            {
                itemDiagnostics.Add(Diagnostic(
                    ReusableActivityImportDiagnosticCodes.InvalidSource,
                    "Elsa 3 workflow definition has no root activity.",
                    source.Id,
                    "/root",
                    guidance: "Repair or re-export the Elsa 3 workflow definition before importing it."));
            }

            if (isReusable && string.IsNullOrWhiteSpace(source.DefinitionId))
            {
                itemDiagnostics.Add(Diagnostic(
                    ReusableActivityImportDiagnosticCodes.InvalidSource,
                    "Elsa 3 reusable workflow definition identity is blank.",
                    source.Id,
                    guidance: "Assign the reusable Elsa 3 workflow a stable definition identity before importing it."));
            }

            foreach (var (activity, path, isRoot) in EnumerateActivities(source.Root))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(activity.NodeId))
                {
                    itemDiagnostics.Add(Diagnostic(
                        ReusableActivityImportDiagnosticCodes.InvalidSource,
                        "Elsa 3 activity node identity is blank.",
                        source.Id,
                        path,
                        guidance: "Assign every activity node a unique stable identity before importing the collection."));
                }
                else if (!nodePaths.TryAdd(activity.NodeId, path))
                {
                    itemDiagnostics.Add(Diagnostic(
                        ReusableActivityImportDiagnosticCodes.DuplicateNodeId,
                        $"Elsa 3 activity node identity '{activity.NodeId}' is duplicated in one workflow version.",
                        source.Id,
                        path,
                        guidance: "Assign every activity node a unique stable identity before importing the collection.",
                        metadata: new Dictionary<string, string>
                        {
                            ["nodeId"] = activity.NodeId,
                            ["firstPath"] = nodePaths[activity.NodeId]
                        }));
                }

                if (isReusable && activity.CustomProperties?.CanStartWorkflow == true)
                {
                    directStarts.Add(new(activity.NodeId, activity.Type));
                    if (activity.Activities?.Count > 0 || activity.Connections?.Count > 0)
                    {
                        itemDiagnostics.Add(Diagnostic(
                            ReusableActivityImportDiagnosticCodes.UnsupportedTrigger,
                            $"Direct-start activity '{activity.NodeId}' owns nested behavior that cannot be separated into the generated wrapper deterministically.",
                            source.Id,
                            path,
                            guidance: "Refactor the Elsa 3 trigger into a leaf trigger before importing the reusable collection.",
                            metadata: new Dictionary<string, string> { ["nodeId"] = activity.NodeId, ["isRoot"] = isRoot.ToString() }));
                    }
                }

                var reference = ReadReference(activity, reusableByDefinition);
                if (reference is null)
                    continue;

                var target = ResolveTarget(reference, reusableByVersion, reusableByDefinition, byVersionId, allByDefinition, out var resolutionCode);
                if (target is null)
                {
                    var code = resolutionCode ?? ReusableActivityImportDiagnosticCodes.MissingReference;
                    itemDiagnostics.Add(Diagnostic(
                        code,
                        $"Reusable workflow reference at node '{activity.NodeId}' could not be resolved to one exact reusable Elsa 3 workflow version.",
                        source.Id,
                        path,
                        guidance: "Include the exact reusable target version in the analyzed collection and remove ambiguous or non-reusable references.",
                        metadata: ReferenceMetadata(activity, reference),
                        dependencySourceIdentity: reference.VersionId ?? reference.DefinitionId,
                        dependencyPathKind: reference.VersionId is null
                            ? Elsa3MigrationPathSegmentKind.DependencySourceDefinition
                            : Elsa3MigrationPathSegmentKind.DependencySourceVersion));
                    continue;
                }

                var targetVersionId = ReusableActivityImportIdentity.Create("activity-version", target.Id);
                dependencies.Add(new(source.Id, target.DefinitionId, target.Id, targetVersionId, activity.NodeId));
                rewrites.Add(new(source.Id, activity.NodeId, target.Id, targetVersionId));
            }

            var canCreateReusableIdentity = isReusable && !string.IsNullOrWhiteSpace(source.DefinitionId);
            var item = new ReusableActivityImportItem(
                source.DefinitionId,
                source.Id,
                source.Version,
                SourceFingerprint(source),
                isReusable,
                source.DefinitionId,
                source.Id,
                canCreateReusableIdentity ? ReusableActivityImportIdentity.Create("activity-definition", source.DefinitionId) : null,
                canCreateReusableIdentity ? ReusableActivityImportIdentity.Create("activity-version", source.Id) : null,
                canCreateReusableIdentity ? ReusableActivityImportIdentity.ActivityTypeKey(source.DefinitionId) : null,
                dependencies.OrderBy(x => x.NodeId, StringComparer.Ordinal).ThenBy(x => x.TargetSourceVersionId, StringComparer.Ordinal).ToArray(),
                rewrites.OrderBy(x => x.NodeId, StringComparer.Ordinal).ThenBy(x => x.TargetSourceVersionId, StringComparer.Ordinal).ToArray(),
                directStarts.OrderBy(x => x.NodeId, StringComparer.Ordinal).ToArray(),
                OrderDiagnostics(itemDiagnostics));
            items.Add(item);
            diagnostics.AddRange(item.Diagnostics);
        }

        var cycles = FindCycles(items.Where(x => x.IsReusable).ToArray());
        foreach (var cycle in cycles)
        {
            var message = $"Reusable Elsa 3 composition contains a cycle: {string.Join(" -> ", cycle)}.";
            var cycleDiagnostic = Diagnostic(
                ReusableActivityImportDiagnosticCodes.DependencyCycle,
                message,
                cycle[0],
                guidance: "Break the recursive reusable composition; it cannot be converted into separate-workflow execution.",
                metadata: new Dictionary<string, string>
                {
                    ["cyclePath"] = string.Join("|", cycle),
                    ["cycleLength"] = (cycle.Count - 1).ToString()
                },
                cycle: cycle);
            diagnostics.Add(cycleDiagnostic);
            var members = cycle.Take(cycle.Count - 1).ToHashSet(StringComparer.Ordinal);
            for (var index = 0; index < items.Count; index++)
            {
                if (members.Contains(items[index].SourceVersionId))
                    items[index] = items[index] with { Diagnostics = OrderDiagnostics(items[index].Diagnostics.Append(cycleDiagnostic)) };
            }
        }

        var orderedItems = items.OrderBy(x => x.SourceVersionId, StringComparer.Ordinal).ToArray();
        var orderedDiagnostics = OrderDiagnostics(diagnostics);
        var planId = ComputePlanId(collection.CollectionId, orderedItems, orderedDiagnostics);
        return ValueTask.FromResult(new ReusableActivityImportPlan(planId, collection.CollectionId, orderedItems, orderedDiagnostics));
    }

    private static bool IsReusable(Elsa3WorkflowDefinition definition) => definition.Options?.UsableAsActivity == true;

    private static IEnumerable<(Elsa3Activity Activity, string Path, bool IsRoot)> EnumerateActivities(Elsa3Activity? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<(Elsa3Activity Activity, string Path, bool IsRoot)>();
        stack.Push((root, "/root", true));
        while (stack.TryPop(out var current))
        {
            yield return current;
            var children = current.Activity.Activities ?? [];
            for (var index = children.Count - 1; index >= 0; index--)
                stack.Push((children[index], $"{current.Path}/activities/{index}", false));
        }
    }

    private static ReusableReference? ReadReference(
        Elsa3Activity activity,
        IReadOnlyDictionary<string, Elsa3WorkflowDefinition[]> reusableByDefinition)
    {
        var exactVersionId = ReadString(activity.AdditionalProperties, "workflowDefinitionVersionId");
        var definitionId = ReadString(activity.AdditionalProperties, "workflowDefinitionId");
        if (!string.IsNullOrWhiteSpace(exactVersionId) || !string.IsNullOrWhiteSpace(definitionId))
            return new(exactVersionId, definitionId, activity.Version);

        return reusableByDefinition.ContainsKey(activity.Type)
            ? new(null, activity.Type, activity.Version)
            : null;
    }

    private static Elsa3WorkflowDefinition? ResolveTarget(
        ReusableReference reference,
        IReadOnlyDictionary<string, Elsa3WorkflowDefinition> reusableByVersion,
        IReadOnlyDictionary<string, Elsa3WorkflowDefinition[]> reusableByDefinition,
        IReadOnlyDictionary<string, Elsa3WorkflowDefinition> allByVersion,
        IReadOnlyDictionary<string, Elsa3WorkflowDefinition[]> allByDefinition,
        out string? diagnosticCode)
    {
        diagnosticCode = null;
        if (!string.IsNullOrWhiteSpace(reference.VersionId))
        {
            if (reusableByVersion.TryGetValue(reference.VersionId, out var exact))
            {
                if (!string.IsNullOrWhiteSpace(reference.DefinitionId) &&
                    !StringComparer.Ordinal.Equals(reference.DefinitionId, exact.DefinitionId))
                {
                    diagnosticCode = ReusableActivityImportDiagnosticCodes.UnsupportedReference;
                    return null;
                }
                return exact;
            }
            diagnosticCode = allByVersion.ContainsKey(reference.VersionId)
                ? ReusableActivityImportDiagnosticCodes.UnsupportedReference
                : ReusableActivityImportDiagnosticCodes.MissingReference;
            return null;
        }

        if (string.IsNullOrWhiteSpace(reference.DefinitionId))
            return null;
        if (!reusableByDefinition.TryGetValue(reference.DefinitionId, out var candidates))
        {
            diagnosticCode = allByDefinition.ContainsKey(reference.DefinitionId)
                ? ReusableActivityImportDiagnosticCodes.UnsupportedReference
                : ReusableActivityImportDiagnosticCodes.MissingReference;
            return null;
        }

        var matches = reference.Version is null
            ? candidates
            : candidates.Where(x => x.Version == reference.Version.Value).ToArray();
        if (matches.Length == 1)
            return matches[0];
        diagnosticCode = matches.Length == 0
            ? ReusableActivityImportDiagnosticCodes.MissingReference
            : ReusableActivityImportDiagnosticCodes.AmbiguousReference;
        return null;
    }

    private static string? ReadString(IDictionary<string, JsonElement> properties, string name)
    {
        if (!properties.TryGetValue(name, out var value))
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();
        if (value.ValueKind != JsonValueKind.Object)
            return null;
        if (value.TryGetProperty("expression", out var expression) && expression.ValueKind == JsonValueKind.Object &&
            expression.TryGetProperty("value", out var expressionValue) && expressionValue.ValueKind == JsonValueKind.String)
            return expressionValue.GetString();
        return value.TryGetProperty("value", out var direct) && direct.ValueKind == JsonValueKind.String
            ? direct.GetString()
            : null;
    }

    private static IReadOnlyList<IReadOnlyList<string>> FindCycles(IReadOnlyList<ReusableActivityImportItem> items)
    {
        var graph = items.ToDictionary(
            x => x.SourceVersionId,
            x => x.Dependencies.Select(y => y.TargetSourceVersionId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            StringComparer.Ordinal);
        var state = new Dictionary<string, byte>(StringComparer.Ordinal);
        var active = new List<string>();
        var activeIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var cycles = new SortedDictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        foreach (var start in graph.Keys.Order(StringComparer.Ordinal))
        {
            if (state.GetValueOrDefault(start) != 0)
                continue;
            var frames = new Stack<TraversalFrame>();
            Enter(start, graph, state, active, activeIndexes, frames);
            while (frames.TryPeek(out var frame))
            {
                if (frame.NextIndex >= frame.Neighbors.Length)
                {
                    frames.Pop();
                    state[frame.Node] = 2;
                    activeIndexes.Remove(frame.Node);
                    active.RemoveAt(active.Count - 1);
                    continue;
                }

                var neighbor = frame.Neighbors[frame.NextIndex++];
                if (!graph.ContainsKey(neighbor))
                    continue;
                var neighborState = state.GetValueOrDefault(neighbor);
                if (neighborState == 0)
                {
                    Enter(neighbor, graph, state, active, activeIndexes, frames);
                    continue;
                }

                if (neighborState != 1 || !activeIndexes.TryGetValue(neighbor, out var cycleStart))
                    continue;
                var cycle = active.Skip(cycleStart).Append(neighbor).ToArray();
                var canonical = CanonicalizeCycle(cycle);
                cycles.TryAdd(string.Join("\u001f", canonical), canonical);
            }
        }

        return cycles.Values.ToArray();
    }

    private static void Enter(
        string node,
        IReadOnlyDictionary<string, string[]> graph,
        IDictionary<string, byte> state,
        IList<string> active,
        IDictionary<string, int> activeIndexes,
        Stack<TraversalFrame> frames)
    {
        state[node] = 1;
        activeIndexes[node] = active.Count;
        active.Add(node);
        frames.Push(new(node, graph[node]));
    }

    private static IReadOnlyList<string> CanonicalizeCycle(IReadOnlyList<string> cycle)
    {
        var body = cycle.Take(cycle.Count - 1).ToArray();
        var start = Enumerable.Range(0, body.Length)
            .OrderBy(index => body[index], StringComparer.Ordinal)
            .First();
        return Enumerable.Range(0, body.Length)
            .Select(offset => body[(start + offset) % body.Length])
            .Append(body[start])
            .ToArray();
    }

    private static string ComputePlanId(
        string collectionId,
        IReadOnlyList<ReusableActivityImportItem> items,
        IReadOnlyList<Elsa3MigrationDiagnostic> diagnostics)
    {
        var fields = new List<string> { collectionId };
        foreach (var item in items)
        {
            fields.AddRange([item.SourceDefinitionId, item.SourceVersionId, item.SourceVersion.ToString(), item.SourceFingerprint, item.IsReusable.ToString(), item.ActivityDefinitionId ?? "", item.ActivityDefinitionVersionId ?? ""]);
            foreach (var rewrite in item.Rewrites)
                fields.AddRange([rewrite.NodeId, rewrite.TargetSourceVersionId, rewrite.TargetActivityDefinitionVersionId]);
        }
        foreach (var diagnostic in diagnostics)
            fields.AddRange([diagnostic.Code, diagnostic.Path ?? "", diagnostic.Metadata.GetValueOrDefault("cyclePath") ?? ""]);
        return ReusableActivityImportIdentity.Create("plan", fields.ToArray());
    }

    private static string SourceFingerprint(Elsa3WorkflowDefinition source)
    {
        var fields = new List<string>
        {
            source.Id, source.DefinitionId, source.Name, source.Description ?? string.Empty,
            source.Version.ToString(), source.CreatedAt.ToUniversalTime().ToString("O"),
            source.ToolVersion ?? string.Empty,
            source.IsReadonly.ToString(), source.IsSystem.ToString(), source.IsLatest.ToString(), source.IsPublished.ToString(),
            source.Options?.UsableAsActivity?.ToString() ?? string.Empty,
            source.Options?.AutoUpdateConsumingWorkflows?.ToString() ?? string.Empty
        };
        foreach (var input in source.Inputs ?? [])
            fields.AddRange(["input", input.Name, input.Type, input.StorageDriverType ?? string.Empty, input.DisplayName ?? string.Empty, input.Description ?? string.Empty]);
        foreach (var output in source.Outputs ?? [])
            fields.AddRange(["output", output.Name, output.Type, output.StorageDriverType ?? string.Empty, output.DisplayName ?? string.Empty, output.Description ?? string.Empty]);
        foreach (var variable in source.Variables ?? [])
            fields.AddRange(["variable", variable.Id, variable.Name, variable.TypeName, variable.IsArray.ToString(), variable.Value ?? string.Empty, variable.StorageDriverTypeName ?? string.Empty]);
        foreach (var outcome in source.Outcomes ?? [])
            fields.AddRange(["outcome", outcome]);
        foreach (var property in (source.CustomProperties ?? new Dictionary<string, JsonElement>()).OrderBy(x => x.Key, StringComparer.Ordinal))
            fields.AddRange(["workflow-property", property.Key, property.Value.GetRawText()]);
        foreach (var (activity, _, _) in EnumerateActivities(source.Root))
        {
            fields.AddRange([
                "activity", activity.Id, activity.NodeId, activity.Name, activity.Type, activity.Version?.ToString() ?? string.Empty,
                activity.CustomProperties?.CanStartWorkflow.ToString() ?? string.Empty,
                activity.CustomProperties?.RunAsynchronously.ToString() ?? string.Empty,
                activity.Metadata?.DisplayText ?? string.Empty,
                activity.Metadata?.Designer.Position.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                activity.Metadata?.Designer.Position.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                activity.Metadata?.Designer.Size.Width.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                activity.Metadata?.Designer.Size.Height.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            ]);
            foreach (var property in activity.AdditionalProperties.OrderBy(x => x.Key, StringComparer.Ordinal))
                fields.AddRange([property.Key, property.Value.GetRawText()]);
            foreach (var property in (activity.Metadata?.AdditionalProperties ?? new Dictionary<string, JsonElement>()).OrderBy(x => x.Key, StringComparer.Ordinal))
                fields.AddRange(["activity-metadata", property.Key, property.Value.GetRawText()]);
            foreach (var connection in activity.Connections ?? [])
                fields.AddRange(["connection", connection.Source.Activity, connection.Source.Port, connection.Target.Activity, connection.Target.Port]);
        }
        return ReusableActivityImportIdentity.Create("source", fields.ToArray());
    }

    private static Dictionary<string, string> ReferenceMetadata(Elsa3Activity activity, ReusableReference reference) => new(StringComparer.Ordinal)
    {
        ["nodeId"] = activity.NodeId,
        ["activityType"] = activity.Type,
        ["targetDefinitionId"] = reference.DefinitionId ?? string.Empty,
        ["targetVersionId"] = reference.VersionId ?? string.Empty,
        ["targetVersion"] = reference.Version?.ToString() ?? string.Empty
    };

    private static Elsa3MigrationDiagnostic Diagnostic(
        string code,
        string message,
        string sourceVersionId,
        string? path = null,
        string? guidance = null,
        IReadOnlyDictionary<string, string>? metadata = null,
        string? dependencySourceIdentity = null,
        Elsa3MigrationPathSegmentKind dependencyPathKind = Elsa3MigrationPathSegmentKind.DependencySourceVersion,
        IReadOnlyList<string>? cycle = null) =>
        new(Elsa3MigrationDiagnosticSeverity.Error, code, message, path, guidance,
            new Dictionary<string, string>(metadata ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                ["SourceVersionId"] = sourceVersionId
            },
            BuildPath(sourceVersionId, path, metadata, dependencySourceIdentity, dependencyPathKind),
            cycle);

    private static IReadOnlyList<Elsa3MigrationPathSegment> BuildPath(
        string sourceVersionId,
        string? nodePath,
        IReadOnlyDictionary<string, string>? metadata,
        string? dependencySourceIdentity,
        Elsa3MigrationPathSegmentKind dependencyPathKind)
    {
        var result = new List<Elsa3MigrationPathSegment>
        {
            new(Elsa3MigrationPathSegmentKind.SourceVersion, sourceVersionId)
        };
        if (nodePath is not null)
            result.Add(new(
                Elsa3MigrationPathSegmentKind.Node,
                metadata?.GetValueOrDefault("nodeId") ?? nodePath,
                nodePath));
        if (dependencySourceIdentity is not null)
            result.Add(new(dependencyPathKind, dependencySourceIdentity));
        return result;
    }

    private static IReadOnlyList<Elsa3MigrationDiagnostic> OrderDiagnostics(IEnumerable<Elsa3MigrationDiagnostic> diagnostics) =>
        diagnostics.OrderBy(x => x.Severity)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Metadata.GetValueOrDefault("SourceVersionId"), StringComparer.Ordinal)
            .ThenBy(x => x.Path, StringComparer.Ordinal)
            .ThenBy(x => x.Metadata.GetValueOrDefault("cyclePath"), StringComparer.Ordinal)
            .ToArray();

    private sealed record ReusableReference(string? VersionId, string? DefinitionId, int? Version);
    private sealed class TraversalFrame(string node, string[] neighbors)
    {
        public string Node { get; } = node;
        public string[] Neighbors { get; } = neighbors;
        public int NextIndex { get; set; }
    }
}
