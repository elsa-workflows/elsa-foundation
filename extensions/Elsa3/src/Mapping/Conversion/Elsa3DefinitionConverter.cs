using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Mappings;
using Elsa3.Mapping.Services;
using Elsa3.Models;

using Elsa3.Mapping.Resolution;

using Elsa3.Mapping.ActivityMap;
using Elsa3.Mapping.ActivityMap.Rewrites;

namespace Elsa3.Mapping.Conversion;

/// <summary>
/// Converts one Elsa 3 definition: clone → normalize → collect the memory-reference graph and lower value flow → map
/// to Elsa 4 state (spec FR-015, research R-8). Lowering runs after an activity refusal too: the definition stays
/// refused, but every script, variable and other node is still analysed, so one run reports every finding (#6231). An
/// unresolved node has no contract, so lowering reads only its expressions, and a check of the lowered tree adds nothing
/// on a node already refused. Whole-definition and node conversion share that preparation, so one rule lowers or
/// refuses every expression on both paths. Diagnostics are ordered by (JSON path, code).
/// </summary>
internal sealed class Elsa3DefinitionConverter(
    Elsa3ActivityNormalizer normalizer,
    Elsa3MemoryReferenceGraph memoryReferenceGraph,
    Elsa3ValueFlowLowerer valueFlowLowerer,
    Elsa3WorkflowDefinitionToState stateMapper,
    Elsa3ActivityToState activityMapper) : IElsa3DefinitionConverter, IElsa3NodeConverter
{
    public async ValueTask<Elsa3MigrationResult<Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3ConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Definition.Root is null)
            return Elsa3MigrationResult<Elsa3ConvertedDefinition>.Failure(
                MappingFailed(request, new InvalidOperationException("The definition has no root activity.")));

        try
        {
            var (plan, boundaryOutcomes) = await PrepareCoreAsync(request, cancellationToken);
            if (plan.HasErrors)
                return Elsa3MigrationResult<Elsa3ConvertedDefinition>.Failure(Ordered(plan.Diagnostics));

            var state = stateMapper.Map(plan);
            // A reusable workflow is a schema-2 graph: the root's natural completion is its boundary's done (D-5). The
            // source is the reference key the root's own contract emits: a built-in's Done, or an imported reusable's
            // done (spec 246: compiling against the published contracts is what checks it).
            IReadOnlyList<Elsa3GraphOutcomeMapping> outcomeMappings = request.IsReusable
                ? [new Elsa3GraphOutcomeMapping(RootDoneKey(plan), Elsa3OutcomeKey.DoneKey)]
                : [];
            return Elsa3MigrationResult<Elsa3ConvertedDefinition>.Success(
                new Elsa3ConvertedDefinition(state, plan.OutputMappings.ToArray(), boundaryOutcomes, outcomeMappings),
                Ordered(plan.Diagnostics));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Elsa3MigrationResult<Elsa3ConvertedDefinition>.Failure(MappingFailed(request, exception));
        }
    }

    /// <summary>
    /// A graph activity resolves every node to a published activity version, and engine intrinsics have none, so a
    /// reusable body that uses one cannot be published (spec 246 D-7). Refusing it here names the Elsa 3 node, where the
    /// publisher would only report an unresolved dependency.
    /// </summary>
    private static void RefuseIntrinsics(Elsa3ConversionPlan plan)
    {
        // A node already refused for its own reason (an End outside the root flowchart) is reported once.
        var refused = RefusedNodeIds(plan);
        foreach (var node in plan.Nodes.Values
                     .Where(node => node.Intrinsic is not null || node.Resolved?.Source == Elsa3ResolvedSource.Intrinsic)
                     .Where(node => !refused.Contains(node.Node.NodeId))
                     .OrderBy(node => node.JsonPath, StringComparer.Ordinal))
        {
            // A step conversion synthesized after an HTTP Endpoint (plan D-12) is reported on the endpoint the engineer
            // knows, with the capture that caused it.
            if (node.SynthesizedFrom is var (endpoint, output))
            {
                plan.Diagnostics.Add(Elsa3ConversionDiagnostics.ForDefinition(
                    plan,
                    Elsa3MigrationDiagnosticSeverity.Error,
                    Elsa3ActivityDiagnosticCodes.ReusableIntrinsicUnsupported,
                    $"Reusable workflow node '{endpoint.NodeId}' ({endpoint.Type}) captures its '{output}' output, which needs a step a graph activity cannot hold yet.",
                    Elsa3ReusableGuidance.HttpProjection(output),
                    node.JsonPath,
                    new Dictionary<string, string> { ["ActivityNodeId"] = endpoint.NodeId, ["ActivityType"] = endpoint.Type }));
                continue;
            }

            plan.Diagnostics.Add(Elsa3ConversionDiagnostics.ForDefinition(
                plan,
                Elsa3MigrationDiagnosticSeverity.Error,
                Elsa3ActivityDiagnosticCodes.ReusableIntrinsicUnsupported,
                $"Reusable workflow node '{node.Node.NodeId}' ({node.Node.Type}) becomes an Elsa 4 engine intrinsic, which a graph activity cannot hold yet.",
                Elsa3ReusableGuidance.Intrinsic(node.Node.Type),
                node.JsonPath,
                new Dictionary<string, string> { ["ActivityNodeId"] = node.Node.NodeId, ["ActivityType"] = node.Node.Type }));
        }
    }

    private static string RootDoneKey(Elsa3ConversionPlan plan) =>
        plan.Definition.Root is { } root && plan.Find(root)?.Resolved?.Source == Elsa3ResolvedSource.Replacement
            ? Elsa3OutcomeKey.DoneKey
            : Elsa3ActivityOutcomes.Done;

    public async ValueTask<Elsa3MigrationResult<Elsa3NodeConversionContext>> PrepareAsync(
        Elsa3ConversionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Definition.Root is null)
            return Elsa3MigrationResult<Elsa3NodeConversionContext>.Failure(
                MappingFailed(request, new InvalidOperationException("The definition has no root activity.")));

        try
        {
            var (plan, _) = await PrepareCoreAsync(request, cancellationToken);
            if (plan.HasErrors)
                return Elsa3MigrationResult<Elsa3NodeConversionContext>.Failure(Ordered(plan.Diagnostics));

            return Elsa3MigrationResult<Elsa3NodeConversionContext>.Success(
                new Elsa3PreparedNodeConversionContext(request, plan),
                Ordered(plan.Diagnostics.Where(diagnostic => !diagnostic.IsError)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return Elsa3MigrationResult<Elsa3NodeConversionContext>.Failure(MappingFailed(request, exception));
        }
    }

    public ValueTask<Elsa3MigrationResult<ActivityNode>> ConvertNodeAsync(
        Elsa3NodeConversionContext context,
        Elsa3Activity node,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(node);
        cancellationToken.ThrowIfCancellationRequested();
        if (context is not Elsa3PreparedNodeConversionContext preparedContext)
            throw new ArgumentException(
                $"The context was created by another node converter ('{context.GetType().FullName}'); prepare it with this converter's {nameof(PrepareAsync)}.",
                nameof(context));

        var plan = preparedContext.Plan;
        var nodeId = Elsa3ConversionDiagnostics.NodeId(node);
        var matches = plan.Nodes.Where(item => StringComparer.Ordinal.Equals(Elsa3ConversionDiagnostics.NodeId(item.Key), nodeId)).ToArray();
        if (matches.Length != 1)
            throw new ArgumentException(
                matches.Length == 0
                    ? $"Node '{nodeId}' is not part of the definition this context was prepared from."
                    : $"Node id '{nodeId}' is used by more than one node of the prepared definition.",
                nameof(node));

        var (prepared, nodePlan) = (matches[0].Key, matches[0].Value);
        try
        {
            var diagnostics = Ordered(plan.Diagnostics.Where(diagnostic => IsWithin(diagnostic.Path, nodePlan.JsonPath)));
            if (diagnostics.Any(diagnostic => diagnostic.IsError))
                return ValueTask.FromResult(Elsa3MigrationResult<ActivityNode>.Failure(diagnostics));

            // A FlowFork, FlowJoin or Start became connections or the start node of its flowchart; it has no node.
            if (nodePlan.GraphConstruct is not null)
                return ValueTask.FromResult(Elsa3MigrationResult<ActivityNode>.Failure(
                    Elsa3ConversionDiagnostics.Error(plan, nodePlan, Elsa3ActivityDiagnosticCodes.UnsupportedMode,
                        $"Elsa 3 '{prepared.Type}' becomes connections or the start node of its flowchart, so it has no Elsa 4 node of its own.",
                        "Convert the enclosing flowchart instead.")));

            var mapped = activityMapper.Map(plan, prepared);
            return ValueTask.FromResult(mapped is null
                ? Elsa3MigrationResult<ActivityNode>.Failure(MappingFailed(context.Request, new InvalidOperationException($"Node '{nodeId}' produced no Elsa 4 activity.")))
                : Elsa3MigrationResult<ActivityNode>.Success(mapped, diagnostics));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return ValueTask.FromResult(Elsa3MigrationResult<ActivityNode>.Failure(MappingFailed(context.Request, exception)));
        }
    }

    /// <summary>Normalizes a clone of the request's definition and lowers its value flow: the steps both entry points share.</summary>
    private async ValueTask<(Elsa3ConversionPlan Plan, IReadOnlyList<Elsa.Activities.Design.Core.Models.ActivityOutcomeContract> BoundaryOutcomes)> PrepareCoreAsync(
        Elsa3ConversionRequest request,
        CancellationToken cancellationToken)
    {
        var clone = Clone(request.Definition);
        var plan = await normalizer.NormalizeAsync(request, clone, cancellationToken);
        var boundaryOutcomes = request.IsReusable ? Elsa3OutcomeKey.Assemble(plan) : [];
        await LowerAsync(plan, cancellationToken);
        CheckBesideRefusals(plan, Elsa3SwitchSafety.Validate);
        if (request.IsReusable)
            RefuseIntrinsics(plan);
        return (plan, boundaryOutcomes);
    }

    /// <summary>Whether <paramref name="path"/> is the node at <paramref name="nodePath"/> or inside it.</summary>
    private static bool IsWithin(string? path, string nodePath) =>
        path is not null &&
        path.StartsWith(nodePath, StringComparison.Ordinal) &&
        (path.Length == nodePath.Length || path[nodePath.Length] is '.' or '[');

    private async ValueTask LowerAsync(Elsa3ConversionPlan plan, CancellationToken cancellationToken)
    {
        if (plan.Definition.Root is null)
            return;

        // A refusal before lowering (an unmapped activity, say) still refuses the definition, but the scan goes on, so
        // one migration run reports every script and component finding (#6231). A plan refused for its shape (duplicate
        // node ids) may not lower at all; its own refusal then stands for what lowering would have found.
        var refused = plan.HasErrors;
        IReadOnlyList<Elsa3MigrationDiagnostic> lowered;
        try
        {
            var inventory = await memoryReferenceGraph.CollectAsync(
                plan.Definition.Root, node => ContractOf(plan, node), plan.JsonPaths, cancellationToken, plan.SourceName);
            lowered = valueFlowLowerer.Lower(plan, inventory, node => AncestorScopeNames(plan, node));
        }
        catch (Exception) when (refused && !cancellationToken.IsCancellationRequested)
        {
            return;
        }

        plan.Diagnostics.AddRange(lowered);
        WarnBulkDispatchOuterWrites(plan);
    }

    /// <summary>
    /// Runs a check of the lowered tree after a refusal too, keeping only what it finds on nodes not already refused: a
    /// check that reads an unresolved node as unproven would otherwise repeat that node's own refusal.
    /// </summary>
    private static void CheckBesideRefusals(Elsa3ConversionPlan plan, Action<Elsa3ConversionPlan> check)
    {
        var refusedNodes = RefusedNodeIds(plan);
        var before = plan.Diagnostics.Count;
        check(plan);
        if (refusedNodes.Count == 0)
            return;
        for (var index = plan.Diagnostics.Count - 1; index >= before; index--)
            if (plan.Diagnostics[index].Metadata.TryGetValue("ActivityNodeId", out var nodeId) && refusedNodes.Contains(nodeId))
                plan.Diagnostics.RemoveAt(index);
    }

    /// <summary>The node ids an error diagnostic already names.</summary>
    private static HashSet<string> RefusedNodeIds(Elsa3ConversionPlan plan) =>
        plan.Diagnostics
            .Where(diagnostic => diagnostic.IsError && diagnostic.Metadata.ContainsKey("ActivityNodeId"))
            .Select(diagnostic => diagnostic.Metadata["ActivityNodeId"])
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Spec 1242 FR-020a: Elsa 3 ran a bulk dispatch's branches one at a time, Elsa 4 runs them concurrently, so a branch
    /// that writes a variable declared outside the bulk node can now lose updates. Elsa 4 allows the write and warns at
    /// publish (spec 266), so the conversion warns too and still converts. A variable a composite inside the branch
    /// declares is not an outer write.
    /// </summary>
    private static void WarnBulkDispatchOuterWrites(Elsa3ConversionPlan plan)
    {
        foreach (var bulk in plan.BulkDispatches.Values)
        {
            var inside = Descendants(bulk.Bulk).Select(Elsa3ConversionDiagnostics.NodeId).ToHashSet(StringComparer.Ordinal);
            foreach (var node in bulk.Branches.SelectMany(Descendants))
            {
                if (plan.Find(node) is not { Resolved: { } resolved } nodePlan)
                    continue;

                var written = new List<string>();
                if (nodePlan.Intrinsic is { Kind: AuthoredWorkflowIntrinsicKind.Set, Variable: { } variable } &&
                    (variable.DeclaringScopeId is null || !inside.Contains(variable.DeclaringScopeId)))
                    written.Add($"variable '{variable.ReferenceKey}'");
                // An output can only be captured into a workflow variable (the lowerer refuses a container variable).
                written.AddRange(node.AdditionalProperties
                    .Where(property => resolved.Outputs.Any(output => StringComparer.Ordinal.Equals(output.ReferenceKey, property.Key)) &&
                                       Elsa3TypedValues.TryGetExpression(property.Value, out var type, out _) &&
                                       StringComparer.Ordinal.Equals(type, "Variable"))
                    .Select(property => $"output '{plan.SourceName(node, property.Key)}' into a workflow variable"));
                if (plan.BulkDispatchBranchScriptWrites.Contains(node))
                    written.Add("variables from its script");
                if (written.Count == 0)
                    continue;

                plan.Diagnostics.Add(Elsa3ConversionDiagnostics.Warning(plan, nodePlan,
                    Elsa3ActivityDiagnosticCodes.BulkDispatchOuterWrite,
                    $"'{Elsa3ConversionDiagnostics.NodeId(node)}' in a branch of bulk dispatch '{bulk.BulkNodeId}' writes {string.Join(", ", written)}, declared outside the bulk dispatch. Elsa 3 ran the branches one at a time; Elsa 4 runs them concurrently, so a shared counter or list can lose updates.",
                    "Record each child's result in the child workflow, or compute the total after the bulk dispatch. Publishing adds Elsa 4's own ValueFlow/ConcurrentWrite warning."));
            }
        }

        static IEnumerable<Elsa3Activity> Descendants(Elsa3Activity root)
        {
            var stack = new Stack<Elsa3Activity>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                foreach (var child in node.Activities ?? [])
                    stack.Push(child);
            }
        }
    }

    /// <summary>
    /// The names an Elsa 4 script can read from <paramref name="node"/> other than the workflow's own variables, nearest
    /// scope first, each with the node that declares it (spec 1222 FR-031): an enclosing For's <c>index</c>, an enclosing
    /// ForEach's <c>currentItem</c> and <c>currentIndex</c>, and an enclosing container's variables.
    /// </summary>
    private static IReadOnlyList<(string Name, string ScopeNodeId)> AncestorScopeNames(Elsa3ConversionPlan plan, Elsa3Activity node)
    {
        var names = new List<(string Name, string ScopeNodeId)>();
        var parent = plan.Find(node)?.Parent;
        while (parent is not null && plan.Find(parent) is { } parentPlan)
        {
            var scopeNodeId = !string.IsNullOrWhiteSpace(parent.NodeId) ? parent.NodeId : parent.Id;
            if (parentPlan.Resolved?.ActivityTypeKey is { } typeKey &&
                Elsa3LoopVariableReferencesRewrite.CurrentValueVariables.TryGetValue(typeKey, out var loopValue))
            {
                names.Add((loopValue, scopeNodeId));
                if (loopValue == "currentItem")
                    names.Add(("currentIndex", scopeNodeId));
            }
            // A bulk dispatch gives its Dispatch node and branches each pass's currentItem and currentIndex (spec 1242).
            else if (StringComparer.Ordinal.Equals(parentPlan.Resolved?.ActivityTypeKey, Elsa3BuiltInActivityMap.BulkDispatchWorkflows))
            {
                names.Add(("currentItem", scopeNodeId));
                names.Add(("currentIndex", scopeNodeId));
            }

            names.AddRange(parentPlan.Variables.Select(variable => (variable.Name, scopeNodeId)));
            parent = parentPlan.Parent;
        }

        return names;

    }

    /// <summary>The node's declared contract, plus its Elsa 4 loop variables as outputs so lowering can resolve them.</summary>
    private static Elsa3NodeContract? ContractOf(Elsa3ConversionPlan plan, Elsa3Activity node)
    {
        if (plan.Find(node) is not { Resolved: { } resolved } nodePlan)
            return null;
        if (nodePlan.LoopVariables.Count == 0)
            return new Elsa3NodeContract(resolved.Inputs, resolved.Outputs);

        var loopOutputs = nodePlan.LoopVariables.Select(item => new Elsa.Activities.Design.Core.Models.OutputDefinition(
            item.Value, item.Key, new Elsa.Primitives.Models.TypeReference(Elsa.Primitives.Models.TypeAliasConvention.AnyAlias), null, item.Value, null, true));
        return new Elsa3NodeContract(resolved.Inputs, resolved.Outputs.Concat(loopOutputs).ToArray());
    }

    private static Elsa3WorkflowDefinition Clone(Elsa3WorkflowDefinition definition) =>
        // Never mutate the caller's Elsa 3 DTO. The clone is an importer-only transition object; successful mapping
        // publishes only ordinary Elsa 4 authored state.
        JsonSerializer.SerializeToElement(definition, Elsa.Serialization.Core.JsonBaseline.Options).Deserialize<Elsa3WorkflowDefinition>(Elsa.Serialization.Core.JsonBaseline.Options)
        ?? throw new JsonException("Elsa 3 workflow definition clone produced no value.");

    private static IReadOnlyList<Elsa3MigrationDiagnostic> Ordered(IEnumerable<Elsa3MigrationDiagnostic> diagnostics) =>
        diagnostics
            .OrderBy(diagnostic => diagnostic.Path ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();

    /// <summary>The last-resort wrapper for an unexpected exception; input never reaches it (research R-3).</summary>
    private static Elsa3MigrationDiagnostic MappingFailed(Elsa3ConversionRequest request, Exception exception)
    {
        var definitionId = string.IsNullOrWhiteSpace(request.Definition.DefinitionId) ? "<unspecified>" : request.Definition.DefinitionId;
        var metadata = request.DiagnosticMetadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(request.Definition.DefinitionId))
            metadata["DefinitionId"] = request.Definition.DefinitionId;
        return new Elsa3MigrationDiagnostic(
            Elsa3MigrationDiagnosticSeverity.Error,
            Elsa3MigrationDiagnosticCodes.DefinitionMappingFailed,
            $"Elsa 3 workflow definition '{definitionId}' could not be mapped: the converter failed with {exception.GetType().Name}.",
            guidance: "Review the Elsa 3 definition and installed Elsa 4 activity catalog before retrying import.",
            metadata: metadata);
    }
}
