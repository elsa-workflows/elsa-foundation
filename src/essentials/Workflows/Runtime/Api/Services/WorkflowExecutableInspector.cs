using System.Text.Json;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>Runtime-owned, Design-independent inspection of immutable executable artifacts and their roots.</summary>
public sealed class WorkflowExecutableInspector(
    IWorkflowExecutableStore executableStore,
    IWorkflowExecutableSourceReferenceStore referenceStore,
    IWorkflowExecutionStateStore executionStore,
    TimeProvider? timeProvider = null) : IWorkflowExecutableInspector
{
    private const int PreviewLength = 80;
    private const string BpmnStructureKind = "elsa.bpmn.structure";
    private const string BpmnStructureSchemaVersion = "1.0.0";
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    Task<WorkflowExecutablesListView> IWorkflowExecutableInspector.ListAsync(ListWorkflowExecutables request, CancellationToken cancellationToken) =>
        ListAsync(request.Scope, request.IncludeRetired, cancellationToken).AsTask();

    async Task<WorkflowExecutableDetailsView> IWorkflowExecutableInspector.GetAsync(GetWorkflowExecutable request, CancellationToken cancellationToken) =>
        await GetAsync(request.ArtifactId, request.Ref, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowExecutable), request.ArtifactId);

    async Task<WorkflowExecutableInputSourcesView> IWorkflowExecutableInspector.GetInputSourcesAsync(GetWorkflowExecutableInputSources request, CancellationToken cancellationToken) =>
        await GetInputSourcesAsync(request.ArtifactId, request.SourceReferenceId, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowExecutableSourceReference), request.SourceReferenceId);

    async Task<ExecutableProvenanceView> IWorkflowExecutableInspector.GetProvenanceAsync(GetWorkflowExecutableProvenance request, CancellationToken cancellationToken) =>
        await GetProvenanceAsync(request.ArtifactId, cancellationToken)
        ?? throw EntityNotFoundException.ForEntity(typeof(WorkflowExecutable), request.ArtifactId);

    public async ValueTask<WorkflowExecutablesListView> ListAsync(
        WorkflowExecutableListScope scope = WorkflowExecutableListScope.Published,
        bool includeRetired = false,
        CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        var retainedCounts = await RetainedCountsAsync(cancellationToken);
        var referencesByArtifact = (await referenceStore.ListAllAsync(cancellationToken: cancellationToken))
            .GroupBy(reference => reference.ArtifactId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var items = new List<WorkflowExecutableSummaryView>();
        foreach (var executable in await executableStore.ListAllAsync(cancellationToken))
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
        var references = await referenceStore.ListAllByArtifactAsync(artifactId, cancellationToken);
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
                    chosen.Layout)
                {
                    ActivityPresentation = chosen.ActivityPresentation
                },
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

        var authoredInputs = reference.AuthoredInputs.Select(input =>
        {
            var redacted = RedactsAuthoredInput(executable, input);
            return new WorkflowExecutableAuthoredInputView(
                input.ExecutableNodeId,
                input.InputKey,
                input.ExpressionType,
                redacted ? null : input.Value,
                redacted,
                redacted ? "redacted" : "allowed");
        }).ToArray();
        var compiledInputs = executable.Nodes
            .SelectMany(node => node.InputBindings.Values.Select(binding => new WorkflowExecutableCompiledInputView(
                node.ExecutableNodeId,
                Binding(binding, includeSourceDetails: !binding.EffectivePolicy.HidesValue(), includeSecretReference: true),
                binding.EffectivePolicy.HidesValue() ? "redacted" : "allowed")))
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
        var references = (await referenceStore.ListAllByArtifactAsync(artifactId, cancellationToken))
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

    /// <summary>
    /// An authored input's own sensitivity flag is only the author's choice: an activity's input declaration reaches the
    /// compiled policy, not the authored record (spec 188, FR-007). So an authored value is shown only when its own flag,
    /// and every compiled binding and pinned input contract the executable holds for that input, allow it. A record's
    /// node id is the authored node id the publish sidecar records. The compiler keeps that id as the executable node id
    /// of an authored node, including a container's children (proved for a sequence by a publish test), so a node is
    /// matched by its executable id, or by its authored id for a reusable activity placed as the workflow root, whose
    /// executable id is generated.
    /// </summary>
    /// <remarks>
    /// An unmatched record is redacted, fail closed, rather than shown on its authored flag alone. Unmatched means that no
    /// executable node matched by the record's node id holds a compiled input binding or a pinned input contract for the
    /// record's input key: the node is missing from the executable, or the node is there but the executable compiled
    /// nothing for that input. Its <c>isSensitive</c> then reports the redaction, not a known sensitivity.
    /// </remarks>
    private static bool RedactsAuthoredInput(WorkflowExecutable executable, WorkflowExecutableAuthoredInputRecord input)
    {
        if (input.IsSensitive)
            return true;

        var verdicts = executable.Nodes
            .Where(node => StringComparer.Ordinal.Equals(node.ExecutableNodeId, input.ExecutableNodeId) ||
                           StringComparer.Ordinal.Equals(node.AuthoredActivityId, input.ExecutableNodeId))
            .SelectMany(node => CompiledVerdicts(node, input.InputKey))
            .ToArray();
        return verdicts.Length == 0 || verdicts.Any(hidden => hidden);
    }

    private static IEnumerable<bool> CompiledVerdicts(ExecutableNode node, string inputKey)
    {
        if (node.InputBindings.TryGetValue(inputKey, out var binding))
            yield return binding.EffectivePolicy.HidesValue();
        if (node.ActivityContract?.Inputs.GetValueOrDefault(inputKey) is { } contract)
            yield return contract.Policy.HidesValue();
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
                .ToArray(),
            ProjectBpmnStructure(node));

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
        if (value.ValueKind != JsonValueKind.Object || !TryReadRequiredString(value, "nodeId", out var nodeId))
            return null;

        var port = value.TryGetProperty("port", out var portValue) && portValue.ValueKind == JsonValueKind.String
            ? portValue.GetString()
            : null;
        return new(nodeId, port);
    }

    // BPMN's executable structure is activity-owned, so Runtime API does not take a dependency on the BPMN module.
    // This projection deliberately exposes only the semantic element/flow fields used by inspection clients.
    private static WorkflowExecutableBpmnStructureView? ProjectBpmnStructure(ExecutableNode node)
    {
        if (node.Structure is not { } structure ||
            !StringComparer.Ordinal.Equals(structure.Kind, BpmnStructureKind) ||
            !StringComparer.Ordinal.Equals(structure.SchemaVersion, BpmnStructureSchemaVersion) ||
            structure.Payload.ValueKind != JsonValueKind.Object)
            return null;

        var elements = new List<WorkflowExecutableBpmnElementView>();
        if (structure.Payload.TryGetProperty("elements", out var elementValues) && elementValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in elementValues.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object ||
                    !TryReadRequiredString(element, "elementId", out var elementId) ||
                    !TryReadRequiredString(element, "elementType", out var elementType))
                    continue;

                elements.Add(new(
                    elementId,
                    elementType,
                    ReadOptionalString(element, "childNodeId"),
                    ReadOptionalString(element, "name")));
            }
        }

        var sequenceFlows = new List<WorkflowExecutableBpmnSequenceFlowView>();
        if (structure.Payload.TryGetProperty("sequenceFlows", out var flowValues) && flowValues.ValueKind == JsonValueKind.Array)
        {
            foreach (var flow in flowValues.EnumerateArray())
            {
                if (flow.ValueKind != JsonValueKind.Object ||
                    !TryReadRequiredString(flow, "flowId", out var flowId) ||
                    !TryReadRequiredString(flow, "sourceRef", out var sourceRef) ||
                    !TryReadRequiredString(flow, "targetRef", out var targetRef))
                    continue;

                sequenceFlows.Add(new(
                    flowId,
                    sourceRef,
                    targetRef,
                    ReadOptionalString(flow, "name"),
                    ReadOptionalString(flow, "conditionOutcome"),
                    ReadOptionalBoolean(flow, "isDefault")));
            }
        }

        return new(elements, sequenceFlows);
    }

    private static bool TryReadRequiredString(JsonElement value, string propertyName, out string result)
    {
        result = "";
        if (!value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
            return false;

        // Retain source-faithful identifiers; validation must not rewrite authored identity.
        result = property.GetString()!;
        return true;
    }

    private static string? ReadOptionalString(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;

    private static bool? ReadOptionalBoolean(JsonElement value, string propertyName) =>
        value.TryGetProperty(propertyName, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? property.GetBoolean()
            : null;

    /// <summary>
    /// Projects one compiled binding. Source details stay out unless <paramref name="includeSourceDetails"/> allows them.
    /// A secret reference is not a value (spec 188), so where <paramref name="includeSecretReference"/> allows it a
    /// secret read shows its reference, as its summary and in <see cref="WorkflowExecutableInputBindingView.Secret"/>,
    /// even though its policy keeps every other source detail out.
    /// </summary>
    private static WorkflowExecutableInputBindingView Binding(
        RuntimeInputBinding binding,
        bool includeSourceDetails,
        bool includeSecretReference = false)
    {
        var secret = includeSecretReference || includeSourceDetails ? binding.Secret : null;
        return new(
            binding.InputName,
            binding.Source.ToString(),
            includeSourceDetails ? Preview(binding) : secret?.Name,
            binding.InputKey,
            binding.EffectivePolicy.HidesValue(),
            includeSourceDetails ? binding.LiteralValue : null,
            includeSourceDetails ? binding.Expression : null,
            includeSourceDetails ? binding.WorkflowRequest : null,
            includeSourceDetails ? binding.Variable : null,
            includeSourceDetails ? binding.ActivityResult : null,
            includeSourceDetails ? binding.ConversionPlan : null,
            includeSourceDetails ? binding.Metadata : null,
            secret);
    }

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
            // The reference, never a value: activation alone resolves a secret.
            RuntimeInputBindingSource.SecretRead => binding.Secret?.Name,
            _ => null
        };
        return text is null || text.Length <= PreviewLength ? text : $"{text[..PreviewLength]}…";
    }
}

/// <summary>
/// The executable inspection operations the runtime endpoints dispatch to. Each method takes the endpoint's
/// wire contract; a missing artifact or source reference surfaces as an <see cref="EntityNotFoundException"/>.
/// </summary>
public interface IWorkflowExecutableInspector
{
    Task<WorkflowExecutablesListView> ListAsync(ListWorkflowExecutables request, CancellationToken cancellationToken);
    Task<WorkflowExecutableDetailsView> GetAsync(GetWorkflowExecutable request, CancellationToken cancellationToken);
    Task<WorkflowExecutableInputSourcesView> GetInputSourcesAsync(GetWorkflowExecutableInputSources request, CancellationToken cancellationToken);
    Task<ExecutableProvenanceView> GetProvenanceAsync(GetWorkflowExecutableProvenance request, CancellationToken cancellationToken);
}
