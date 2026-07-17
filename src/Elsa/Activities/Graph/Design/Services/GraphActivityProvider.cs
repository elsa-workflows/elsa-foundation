using System.Text;
using System.Text.Json;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Graph.Design.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Graph.Design.Services;

/// <summary>
/// Design-owned compiler for provider manifest schema 1. It never loads mutable definitions and
/// receives every exact dependency identity from the publishing coordinator.
/// </summary>
public sealed class GraphActivityProvider(
    IActivityStructureService activityStructureService,
    ActivityContractAuthoringValidator contractCapabilities) : IActivityProvider, IActivityTemplateProviderCompiler, IActivityTemplateDependencyDiscoverer
{
    public const string Key = "elsa.activity-graph";
    public const string Fingerprint = "elsa.activity-graph/compiler/1.0.0";
    public const string RuntimeConsumerKey = "elsa.graph-activity";
    public const string RuntimeDescriptorSchemaVersion = "1";

    private static readonly IReadOnlySet<string> Schemas = new HashSet<string>(StringComparer.Ordinal)
    {
        ActivityGraphManifest.SchemaVersion
    };

    public string ProviderKey => Key;
    public string CompilerFingerprint => Fingerprint;
    public IReadOnlySet<string> SupportedManifestSchemas => Schemas;
    public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
        "Activity Graph",
        [new(ActivityGraphManifest.SchemaVersion, true, new HashSet<string>(StringComparer.Ordinal) { ActivityGraphManifest.SchemaVersion })],
        new([new("done", "Done", true)]));

    public ValueTask<ActivityContractProposal> ProposeContractAsync(
        ActivityProviderContractProposalRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var diagnostics = ReadManifest(request.Manifest, ProposalSubject(), out _);

        // Schema 1 intentionally does not duplicate public input/output types. A proposal can seed
        // the natural outcome, while the authoritative draft contract remains user/provider owned.
        var done = new ActivityOutcomeContract("done", "Done", true);
        var existingDone = request.Contract.Outcomes.FirstOrDefault(x => StringComparer.Ordinal.Equals(x.ReferenceKey, done.ReferenceKey));
        var changes = existingDone == done
            ? Array.Empty<ActivityContractProposalChange>()
            : [new(
                "outcome:done",
                existingDone is null ? ActivityContractProposalOperation.Add : ActivityContractProposalOperation.Replace,
                ActivityContractMemberKind.Outcome,
                "done",
                Outcome: done)];
        return ValueTask.FromResult(new ActivityContractProposal(
            changes,
            diagnostics));
    }

    public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(
        ActivityProviderManifest manifest,
        ActivityContract contract,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var subject = ProposalSubject();
        var diagnostics = ReadManifest(manifest, subject, out var graph).ToList();
        if (graph is not null)
            ValidateGraph(graph, contract, subject, diagnostics);

        return ValueTask.FromResult(ActivityDiagnosticOrderer.Order(diagnostics));
    }

    public ValueTask<ActivityTemplateCompilation> CompileAsync(
        ActivityTemplateCompilationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var subject = new ActivityDiagnosticSubject(
            "ActivityDraft",
            request.DraftId,
            request.DefinitionId,
            Revision: request.Revision);
        var diagnostics = ReadManifest(request.Provider, subject, out var graph).ToList();
        IReadOnlyList<GraphNode> nodes = [];

        if (graph is not null)
        {
            ValidateGraph(graph, request.Contract, subject, diagnostics);
            nodes = EnumerateNodes(graph.RootActivity);
            ValidateDependencies(nodes, request.ResolvedDirectDependencies, subject, diagnostics);
        }

        var orderedDiagnostics = ActivityDiagnosticOrderer.Order(diagnostics);
        if (graph is null || orderedDiagnostics.Any(x => x.Severity == ActivityDiagnosticSeverity.Error))
            return ValueTask.FromResult(EmptyCompilation(request.ProviderFingerprint, orderedDiagnostics));

        var directDependencies = request.ResolvedDirectDependencies
            .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
            .ThenBy(x => x.VersionId, StringComparer.Ordinal)
            .ToArray();
        var descriptorPayload = BuildBoundaryPayload(graph, request.Contract, nodes, directDependencies);
        var executableRoot = new ExecutableNode(
            "graph-boundary",
            "graph-boundary",
            RuntimeConsumerKey,
            RuntimeDescriptorSchemaVersion,
            new RuntimeActivityDescriptor(RuntimeConsumerKey, RuntimeDescriptorSchemaVersion, descriptorPayload),
            new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal),
            new Dictionary<string, RuntimeOutputCapture>(StringComparer.Ordinal),
            new Dictionary<string, string>(StringComparer.Ordinal));
        var descriptorBytes = Encoding.UTF8.GetByteCount(descriptorPayload.GetRawText());
        var maximumDepth = nodes.Count == 0 ? 0 : nodes.Max(x => x.Depth);
        var measurements = new ActivityResourceMeasurements(
            nodes.Count,
            nodes.Count,
            directDependencies.LongLength,
            maximumDepth,
            descriptorBytes,
            0,
            request.Contract.Inputs.Count + request.Contract.Outputs.Count + graph.Variables.Count);

        return ValueTask.FromResult(new ActivityTemplateCompilation(
            executableRoot,
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal),
            directDependencies,
            [new RuntimeRequirement(RuntimeConsumerKey, RuntimeDescriptorSchemaVersion)],
            request.Contract.Inputs.Select(x => x.StorageDriverKey)
                .Concat(request.Contract.Outputs.Select(x => x.StorageDriverKey))
                .Concat(graph.Variables.Select(x => x.StorageDriverKey))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(x => new RuntimeStorageDriverRequirement(x))
                .ToArray(),
            measurements,
            request.ProviderFingerprint,
            [],
            orderedDiagnostics));
    }

    public ValueTask<ActivityTemplateDependencyDiscovery> DiscoverDependenciesAsync(
        ActivityTemplateDependencyDiscoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var subject = new ActivityDiagnosticSubject("ActivityDraft", request.DraftId, request.DefinitionId, Revision: request.Revision);
        var diagnostics = ReadManifest(request.Provider, subject, out var graph).ToList();
        if (graph is null)
            return ValueTask.FromResult(new ActivityTemplateDependencyDiscovery([], ActivityDiagnosticOrderer.Order(diagnostics)));
        var dependencies = EnumerateNodes(graph.RootActivity)
            .OrderBy(x => x.NodeId, StringComparer.Ordinal)
            .Select(x => new ActivityTemplateDependencyDeclaration(
                x.ActivityVersionId,
                x.NodeId,
                [new("AuthoredNode", x.NodeId)],
                x.ParentOccurrenceId,
                x.ChildSlotName,
                x.ChildIndex))
            .ToArray();
        return ValueTask.FromResult(new ActivityTemplateDependencyDiscovery(dependencies, ActivityDiagnosticOrderer.Order(diagnostics)));
    }

    public ValueTask<ActivityManifestMigration> MigrateAsync(
        ActivityManifestMigrationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var subject = ProposalSubject();
        if (!string.Equals(request.Source.ProviderKey, Key, StringComparison.Ordinal) ||
            !string.Equals(request.TargetSchemaVersion, ActivityGraphManifest.SchemaVersion, StringComparison.Ordinal))
        {
            return ValueTask.FromResult(new ActivityManifestMigration(null, [Diagnostic(
                "activity.provider.migration-unsupported",
                "The graph provider has no deterministic migration for the requested target schema.",
                subject,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sourceSchemaVersion"] = request.Source.SchemaVersion,
                    ["targetSchemaVersion"] = request.TargetSchemaVersion
                })]));
        }

        var diagnostics = ReadManifest(request.Source, subject, out var graph);
        if (graph is null)
            return ValueTask.FromResult(new ActivityManifestMigration(null, diagnostics));

        var migrated = new ActivityProviderManifest(Key, ActivityGraphManifest.SchemaVersion, graph.ToCanonicalJsonElement());
        return ValueTask.FromResult(new ActivityManifestMigration(migrated, diagnostics));
    }

    private static IReadOnlyList<ActivityDiagnostic> ReadManifest(
        ActivityProviderManifest manifest,
        ActivityDiagnosticSubject subject,
        out ActivityGraphManifest? graph)
    {
        var diagnostics = new List<ActivityDiagnostic>();
        graph = null;

        if (!string.Equals(manifest.ProviderKey, Key, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "activity.graph.provider-key-invalid",
                $"Manifest provider key must be '{Key}'.",
                subject,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["actualProviderKey"] = manifest.ProviderKey,
                    ["expectedProviderKey"] = Key
                }));
        }

        if (!string.Equals(manifest.SchemaVersion, ActivityGraphManifest.SchemaVersion, StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "activity.graph.schema-unsupported",
                $"Graph manifest schema '{manifest.SchemaVersion}' is not supported.",
                subject,
                metadata: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["schemaVersion"] = manifest.SchemaVersion
                }));
        }

        if (diagnostics.Count != 0)
            return ActivityDiagnosticOrderer.Order(diagnostics);

        if (ActivityGraphManifest.TryParse(manifest.Payload, out graph, out var errors))
            return [];

        diagnostics.AddRange(errors.Select(error => Diagnostic(
            error.Code,
            error.Message,
            subject,
            error.JsonPointer)));
        return ActivityDiagnosticOrderer.Order(diagnostics);
    }

    private void ValidateGraph(
        ActivityGraphManifest graph,
        ActivityContract contract,
        ActivityDiagnosticSubject subject,
        ICollection<ActivityDiagnostic> diagnostics)
    {
        var candidates = EnumerateNodeCandidates(graph.RootActivity);
        foreach (var candidate in candidates)
        {
            if (!TryReadRequiredString(candidate.Element, "nodeId", out _))
            {
                diagnostics.Add(Diagnostic(
                    "activity.graph.node-id-required",
                    "An authored graph node must declare a non-empty 'nodeId'.",
                    subject,
                    $"{candidate.JsonPointer}/nodeId"));
            }

            if (!TryReadRequiredString(candidate.Element, "activityVersionId", out _))
            {
                diagnostics.Add(Diagnostic(
                    "activity.graph.activity-version-required",
                    "An authored graph node must declare an exact non-empty 'activityVersionId'.",
                    subject,
                    $"{candidate.JsonPointer}/activityVersionId"));
            }
        }

        var nodes = EnumerateNodes(graph.RootActivity);

        foreach (var duplicate in nodes.GroupBy(x => x.NodeId, StringComparer.Ordinal).Where(x => x.Count() > 1))
        {
            foreach (var node in duplicate)
            {
                diagnostics.Add(Diagnostic(
                    "activity.graph.node-id-duplicate",
                    $"Graph node id '{duplicate.Key}' is not unique.",
                    subject,
                    $"{node.JsonPointer}/nodeId",
                    duplicate.Key));
            }
        }

        ValidateForbiddenCapabilities(candidates, subject, diagnostics);

        foreach (var duplicate in graph.Variables.GroupBy(x => x.ReferenceKey, StringComparer.Ordinal).Where(x => x.Count() > 1))
        {
            diagnostics.Add(Diagnostic(
                "activity.graph.variable-reference-duplicate",
                $"Graph variable reference key '{duplicate.Key}' is not unique.",
                subject,
                "/variables",
                duplicate.Key));
        }

        for (var index = 0; index < graph.Variables.Count; index++)
        {
            var variable = graph.Variables[index];
            foreach (var diagnostic in contractCapabilities.ValidateTypeReference(
                         variable.Type,
                         variable.StorageDriverKey,
                         subject,
                         $"/variables/{index}",
                         variable.ReferenceKey,
                         variable.InitialValue?.Value.ValueKind == JsonValueKind.Null))
                diagnostics.Add(diagnostic);
        }

        var outputKeys = contract.Outputs.Select(x => x.ReferenceKey).ToHashSet(StringComparer.Ordinal);
        foreach (var mappingGroup in graph.OutputMappings.GroupBy(x => x.OutputReferenceKey, StringComparer.Ordinal))
        {
            if (!outputKeys.Contains(mappingGroup.Key))
            {
                diagnostics.Add(Diagnostic(
                    "activity.graph.output-mapping-unknown",
                    $"Output mapping '{mappingGroup.Key}' does not name a public output.",
                    subject,
                    "/outputMappings",
                    mappingGroup.Key));
            }

            if (mappingGroup.Count() > 1)
            {
                diagnostics.Add(Diagnostic(
                    "activity.graph.output-mapping-duplicate",
                    $"Public output '{mappingGroup.Key}' has more than one boundary mapping.",
                    subject,
                    "/outputMappings",
                    mappingGroup.Key));
            }
        }

        foreach (var output in contract.Outputs.Where(x => x.IsRequired))
        {
            if (graph.OutputMappings.Count(x => string.Equals(x.OutputReferenceKey, output.ReferenceKey, StringComparison.Ordinal)) != 1)
            {
                diagnostics.Add(Diagnostic(
                    "activity.graph.output-mapping-required",
                    $"Required public output '{output.ReferenceKey}' must have exactly one boundary mapping.",
                    subject,
                    "/outputMappings",
                    output.ReferenceKey));
            }
        }

        var emittedOutcomes = contract.Outcomes.Where(x => x.IsEmitted).ToArray();
        if (emittedOutcomes.Length != 1 || !string.Equals(emittedOutcomes[0].ReferenceKey, "done", StringComparison.Ordinal))
        {
            diagnostics.Add(Diagnostic(
                "activity.graph.outcome-done-required",
                "Graph schema 1 requires exactly one emitted public outcome with reference key 'done'.",
                subject,
                "/contract/outcomes",
                "done"));
        }
    }

    private static void ValidateDependencies(
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<ActivityResolvedDependency> dependencies,
        ActivityDiagnosticSubject subject,
        ICollection<ActivityDiagnostic> diagnostics)
    {
        var nodeOccurrences = nodes.Select(x => x.NodeId).ToHashSet(StringComparer.Ordinal);
        var dependenciesByOccurrence = dependencies
            .GroupBy(x => x.OccurrenceId, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);

        foreach (var node in nodes)
        {
            if (!dependenciesByOccurrence.TryGetValue(node.NodeId, out var matches))
            {
                diagnostics.Add(Diagnostic(
                    "activity.dependency.unresolved",
                    $"Graph node '{node.NodeId}' has no exact resolved dependency.",
                    subject,
                    node.JsonPointer,
                    node.NodeId));
                continue;
            }

            if (matches.Length != 1)
            {
                diagnostics.Add(Diagnostic(
                    "activity.dependency.occurrence-duplicate",
                    $"Graph node '{node.NodeId}' has more than one resolved dependency for the same occurrence.",
                    subject,
                    node.JsonPointer,
                    node.NodeId));
                continue;
            }

            if (!string.Equals(matches[0].VersionId, node.ActivityVersionId, StringComparison.Ordinal))
            {
                diagnostics.Add(Diagnostic(
                    "activity.dependency.version-mismatch",
                    $"Graph node '{node.NodeId}' does not match its exact resolved activity version.",
                    subject,
                    $"{node.JsonPointer}/activityVersionId",
                    node.NodeId));
            }
        }

        foreach (var dependency in dependencies.Where(x => !nodeOccurrences.Contains(x.OccurrenceId)))
        {
            diagnostics.Add(Diagnostic(
                "activity.dependency.occurrence-unused",
                $"Resolved dependency occurrence '{dependency.OccurrenceId}' does not exist in the graph.",
                subject,
                referenceKey: dependency.OccurrenceId));
        }
    }

    private IReadOnlyList<GraphNode> EnumerateNodes(JsonElement root)
    {
        ActivityNode rootNode;
        try
        {
            rootNode = root.Deserialize<ActivityNode>(new JsonSerializerOptions(JsonSerializerDefaults.Web))
                       ?? throw new JsonException("The root activity is null.");
        }
        catch (JsonException)
        {
            return [];
        }

        var pointers = EnumerateNodeCandidates(root)
            .Where(x => TryReadNode(x.Element, out _, out _))
            .GroupBy(x => x.Element.GetProperty("nodeId").GetString()!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First().JsonPointer, StringComparer.Ordinal);
        var nodes = new List<GraphNode>();
        var pending = new Stack<(ActivityNode Node, string? ParentOccurrenceId, string ChildSlotName, int ChildIndex, long Depth)>();
        pending.Push((rootNode, null, "activity-graph", 0, 1));

        while (pending.TryPop(out var item))
        {
            if (string.IsNullOrWhiteSpace(item.Node.NodeId) || string.IsNullOrWhiteSpace(item.Node.ActivityVersionId))
                continue;
            var pointer = pointers.GetValueOrDefault(item.Node.NodeId, "/rootActivity");
            var element = FindNodeElement(root, item.Node.NodeId) ?? root;
            nodes.Add(new(
                item.Node.NodeId,
                item.Node.ActivityVersionId,
                pointer,
                item.Depth,
                element,
                item.ParentOccurrenceId,
                item.ChildSlotName,
                item.ChildIndex));

            foreach (var slot in activityStructureService.ProjectChildren(item.Node).Reverse())
            {
                var children = slot.Activities.ToArray();
                for (var index = children.Length - 1; index >= 0; index--)
                    pending.Push((children[index], item.Node.NodeId, slot.Name, index, item.Depth + 1));
            }
        }

        return nodes;
    }

    private static JsonElement? FindNodeElement(JsonElement root, string nodeId)
    {
        foreach (var candidate in EnumerateNodeCandidates(root))
            if (TryReadNode(candidate.Element, out var candidateId, out _) && StringComparer.Ordinal.Equals(candidateId, nodeId))
                return candidate.Element;
        return null;
    }

    private static IReadOnlyList<NodeCandidate> EnumerateNodeCandidates(JsonElement root)
    {
        var candidates = new List<NodeCandidate>();
        var pending = new Stack<(JsonElement Element, string Pointer, bool IsCandidate)>();
        pending.Push((root, "/rootActivity", true));

        while (pending.TryPop(out var item))
        {
            var hasNodeField = item.Element.ValueKind == JsonValueKind.Object &&
                (item.Element.TryGetProperty("nodeId", out _) || item.Element.TryGetProperty("activityVersionId", out _));
            if (item.IsCandidate || hasNodeField)
                candidates.Add(new(item.Element, item.Pointer));

            if (item.Element.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in item.Element.EnumerateObject().OrderByDescending(x => x.Name, StringComparer.Ordinal))
                {
                    var pointer = $"{item.Pointer}/{EscapePointer(property.Name)}";
                    if (property.Value.ValueKind == JsonValueKind.Array)
                    {
                        var elements = property.Value.EnumerateArray().ToArray();
                        var containsAuthoredNodes = string.Equals(property.Name, "activities", StringComparison.Ordinal);
                        for (var index = elements.Length - 1; index >= 0; index--)
                            pending.Push((elements[index], $"{pointer}/{index}", containsAuthoredNodes));
                    }
                    else
                    {
                        pending.Push((property.Value, pointer, false));
                    }
                }
            }
            else if (item.Element.ValueKind == JsonValueKind.Array)
            {
                var elements = item.Element.EnumerateArray().ToArray();
                for (var index = elements.Length - 1; index >= 0; index--)
                    pending.Push((elements[index], $"{item.Pointer}/{index}", false));
            }
        }

        return candidates;
    }

    private static bool TryReadNode(JsonElement element, out string? nodeId, out string? activityVersionId)
    {
        nodeId = null;
        activityVersionId = null;
        if (element.ValueKind != JsonValueKind.Object ||
            !TryReadRequiredString(element, "nodeId", out nodeId) ||
            !TryReadRequiredString(element, "activityVersionId", out activityVersionId))
            return false;

        return true;
    }

    private static void ValidateForbiddenCapabilities(
        IReadOnlyList<NodeCandidate> candidates,
        ActivityDiagnosticSubject subject,
        ICollection<ActivityDiagnostic> diagnostics)
    {
        foreach (var candidate in candidates.Where(x => x.Element.ValueKind == JsonValueKind.Object))
        {
            foreach (var property in candidate.Element.EnumerateObject())
            {
                var pointer = $"{candidate.JsonPointer}/{EscapePointer(property.Name)}";
                if (IsEnabled(property.Value) && IsTriggerCapability(property.Name))
                    diagnostics.Add(Diagnostic("activity.graph.trigger-entry-forbidden", "Trigger entry is not valid in graph schema 1.", subject, pointer));
                if (IsEnabled(property.Value) && IsRootMutationCapability(property.Name))
                    diagnostics.Add(Diagnostic("activity.graph.workflow-root-mutation-forbidden", "Workflow-root mutation is not valid in graph schema 1.", subject, pointer));

                if (string.Equals(property.Name, "capabilities", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var capability in property.Value.EnumerateArray())
                    {
                        var value = capability.ValueKind == JsonValueKind.String ? capability.GetString() : null;
                        if (value is not null && IsTriggerCapability(value))
                            diagnostics.Add(Diagnostic("activity.graph.trigger-entry-forbidden", "Trigger entry is not valid in graph schema 1.", subject, $"{pointer}/{index}"));
                        if (value is not null && IsRootMutationCapability(value))
                            diagnostics.Add(Diagnostic("activity.graph.workflow-root-mutation-forbidden", "Workflow-root mutation is not valid in graph schema 1.", subject, $"{pointer}/{index}"));
                        index++;
                    }
                }
            }
        }
    }

    private static bool TryReadRequiredString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
            return false;

        value = property.GetString();
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool IsTriggerCapability(string value) => Normalize(value) is
        "trigger" or "istrigger" or "triggerentry" or "canstartworkflow";

    private static bool IsRootMutationCapability(string value) => Normalize(value) is
        "isworkflowroot" or "mutatesworkflowroot" or "workflowrootmutation";

    private static string Normalize(string value) => new(value
        .Where(char.IsLetterOrDigit)
        .Select(char.ToLowerInvariant)
        .ToArray());

    private static bool IsEnabled(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.String => string.Equals(value.GetString(), "true", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    private static JsonElement Canonicalize(JsonElement element)
    {
        using var document = JsonDocument.Parse(element.GetRawText());
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            ActivityGraphCanonicalJson.Write(writer, document.RootElement);
        using var canonical = JsonDocument.Parse(stream.ToArray());
        return canonical.RootElement.Clone();
    }

    private static JsonElement BuildBoundaryPayload(
        ActivityGraphManifest graph,
        ActivityContract contract,
        IReadOnlyList<GraphNode> nodes,
        IReadOnlyList<ActivityResolvedDependency> dependencies)
    {
        var nodesByOccurrence = nodes.ToDictionary(x => x.NodeId, StringComparer.Ordinal);
        var plan = new
        {
            planSchemaVersion = "1",
            entryOccurrenceId = nodes.OrderBy(x => x.Depth).ThenBy(x => x.JsonPointer, StringComparer.Ordinal).First().NodeId,
            occurrences = dependencies.Select(dependency =>
            {
                var node = nodesByOccurrence[dependency.OccurrenceId];
                return new
                {
                    occurrenceId = dependency.OccurrenceId,
                    definitionId = dependency.DefinitionId,
                    definitionVersionId = dependency.VersionId,
                    version = dependency.Version,
                    templateId = dependency.TemplateId,
                    templateHash = dependency.TemplateHash,
                    nodeOrigin = dependency.NodeOrigin,
                    parentOccurrenceId = dependency.ParentOccurrenceId,
                    childSlotName = dependency.ChildSlotName,
                    childIndex = dependency.ChildIndex,
                    authoredDepth = node.Depth,
                    inputBindings = ReadCanonicalArray(node.Element, "inputs"),
                    outputBindings = ReadCanonicalArray(node.Element, "outputs")
                };
            }).ToArray(),
            variables = graph.Variables,
            outputMappings = graph.OutputMappings,
            inputs = contract.Inputs
                .OrderBy(x => x.ReferenceKey, StringComparer.Ordinal)
                .Select(x => new
                {
                    referenceKey = x.ReferenceKey,
                    name = x.Name,
                    type = x.Type,
                    storageDriverKey = x.StorageDriverKey,
                    durability = x.Durability,
                    x.Default
                })
                .ToArray(),
            outputs = contract.Outputs
                .OrderBy(x => x.ReferenceKey, StringComparer.Ordinal)
                .Select(x => new
                {
                    referenceKey = x.ReferenceKey,
                    name = x.Name,
                    type = x.Type,
                    storageDriverKey = x.StorageDriverKey,
                    durability = x.Durability
                })
                .ToArray(),
            inputReferenceKeys = contract.Inputs.Select(x => x.ReferenceKey).Order(StringComparer.Ordinal).ToArray(),
            outputReferenceKeys = contract.Outputs.Select(x => x.ReferenceKey).Order(StringComparer.Ordinal).ToArray(),
            requiredInputReferenceKeys = contract.Inputs.Where(x => x.IsRequired).Select(x => x.ReferenceKey).Order(StringComparer.Ordinal).ToArray(),
            requiredOutputReferenceKeys = contract.Outputs.Where(x => x.IsRequired).Select(x => x.ReferenceKey).Order(StringComparer.Ordinal).ToArray()
        };
        return Canonicalize(JsonSerializer.SerializeToElement(plan, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static JsonElement ReadCanonicalArray(JsonElement node, string propertyName)
    {
        if (!node.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
            return EmptyArray();
        return Canonicalize(value);
    }

    private static ActivityTemplateCompilation EmptyCompilation(
        string providerFingerprint,
        IReadOnlyList<ActivityDiagnostic> diagnostics) => new(
        null,
        new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal),
        [],
        [],
        [],
        new(0, 0, 0, 0, 0, 0, 0),
        providerFingerprint,
        [],
        diagnostics);

    private static JsonElement EmptyArray()
    {
        using var document = JsonDocument.Parse("[]");
        return document.RootElement.Clone();
    }

    private static ActivityDiagnosticSubject ProposalSubject() => new("ActivityDefinition", Key);

    private static ActivityDiagnostic Diagnostic(
        string code,
        string message,
        ActivityDiagnosticSubject subject,
        string? jsonPointer = null,
        string? referenceKey = null,
        IReadOnlyDictionary<string, string>? metadata = null) => new(
        code,
        ActivityDiagnosticSeverity.Error,
        message,
        subject,
        new ActivityDiagnosticLocation(Key, jsonPointer, referenceKey),
        Metadata: metadata ?? new Dictionary<string, string>());

    private static string EscapePointer(string segment) => segment.Replace("~", "~0").Replace("/", "~1");

    private sealed record GraphNode(
        string NodeId,
        string ActivityVersionId,
        string JsonPointer,
        long Depth,
        JsonElement Element,
        string? ParentOccurrenceId,
        string ChildSlotName,
        int ChildIndex);
    private sealed record NodeCandidate(JsonElement Element, string JsonPointer);
}
