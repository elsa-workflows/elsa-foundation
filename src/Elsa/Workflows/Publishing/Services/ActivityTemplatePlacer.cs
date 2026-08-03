using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

public interface IActivityPlacementHasher
{
    string ComputeHash(byte[] canonicalOrigin);
}

public sealed class Sha256ActivityPlacementHasher : IActivityPlacementHasher
{
    public string ComputeHash(byte[] canonicalOrigin) => Convert.ToHexStringLower(SHA256.HashData(canonicalOrigin));
}

public sealed record ActivityTemplatePlacementRequest(
    ActivityDefinitionVersionPublication Publication,
    ExecutableActivityTemplate Template,
    WorkflowExecutableSourceReference SourceReference,
    ActivityInvocationOrigin InvocationOrigin,
    string ActivityTypeKey,
    IReadOnlyDictionary<string, RuntimeInputBinding> BoundaryInputBindings,
    IReadOnlyDictionary<string, RuntimeOutputCapture> BoundaryOutputCaptures,
    // #1007: when this placement is a child inside a workflow container (Sequence, Flowchart, …), the
    // container's compiled structure addresses its children by the authored node id. The boundary node
    // must therefore carry that authored node id as its ExecutableNodeId so the runtime resolves the
    // placed child; a workflow-root placement leaves this false and keeps its content-addressed id.
    bool BindBoundaryRootToAuthoredNode = false);

public sealed record ActivityTemplatePlacement(
    ExecutableNode Root,
    IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets,
    ExecutableLayoutSidecar LayoutSidecar,
    IReadOnlyCollection<Elsa.Activities.Runtime.Core.Models.RuntimeRequirement> RuntimeRequirements);

/// <summary>
/// Iteratively expands exact immutable templates under canonical invocation origins. Every placed
/// identity uses the full SHA-256 of a length-framed origin; readable origins remain in metadata.
/// </summary>
public sealed class ActivityTemplatePlacer(
    IActivityDefinitionVersionPublicationStore publications,
    IExecutableActivityTemplateReader templates,
    IWorkflowExecutableSourceReferenceReader sourceReferences,
    IActivityPlacementHasher placementHasher,
    IActivityStructureService activityStructureService,
    RuntimeInputBindingCompiler inputBindingCompiler)
{
    private static readonly JsonSerializerOptions OccurrenceSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async ValueTask<ActivityTemplatePlacement> PlaceAsync(
        ActivityTemplatePlacementRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateExactBinding(request.Publication, request.Template, request.SourceReference, directSelection: true);

        var generatedIds = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<PlacementFrame>();
        stack.Push(new(request.Publication, request.Template, request.SourceReference, request.InvocationOrigin, request.ActivityTypeKey, null, null, "activity-graph", 0, null));
        PlacementBuild? completed = null;

        while (stack.TryPeek(out var frame))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (frame.NextDependencyIndex < frame.Dependencies.Length)
            {
                var dependency = frame.Dependencies[frame.NextDependencyIndex++];
                var publication = await publications.FindAsync(dependency.DefinitionVersionId, cancellationToken)
                                  ?? throw new InvalidOperationException($"Exact activity dependency version '{dependency.DefinitionVersionId}' was not found during placement.");
                var template = await templates.FindAsync(dependency.TemplateId, cancellationToken)
                               ?? throw new InvalidOperationException($"Exact activity dependency template '{dependency.TemplateId}' was not found during placement.");
                var sourceReference = await sourceReferences.FindAsync(publication.SourceReferenceId, cancellationToken)
                                      ?? throw new InvalidOperationException($"Exact activity dependency Source Reference '{publication.SourceReferenceId}' was not found during placement.");
                // This exact child was already closed into the immutable selected parent template. Retirement
                // blocks new direct catalog selection but does not invalidate that retained closure; revocation
                // remains the stronger placement-time policy fact.
                ValidateExactBinding(publication, template, sourceReference, directSelection: false);
                if (!StringComparer.Ordinal.Equals(dependency.TemplateHash, template.TemplateHash) ||
                    !StringComparer.Ordinal.Equals(dependency.DefinitionId, publication.DefinitionId) ||
                    !StringComparer.Ordinal.Equals(dependency.Version, publication.Version))
                    throw new InvalidOperationException($"Dependency occurrence '{dependency.OccurrenceId}' does not match its exact published template binding.");

                var childOrigin = Extend(
                    frame.Origin,
                    new(ActivityInvocationOriginSegmentKind.NestedPlacement, dependency.OccurrenceId),
                    new(ActivityInvocationOriginSegmentKind.TemplateBoundary, dependency.DefinitionVersionId));
                stack.Push(new(
                    publication,
                    template,
                    sourceReference,
                    childOrigin,
                    publication.ActivityTypeKey,
                    dependency.OccurrenceId,
                    dependency.ParentOccurrenceId,
                    dependency.ChildSlotName,
                    dependency.ChildIndex,
                    dependency.OccurrenceOverlay));
                continue;
            }

            var build = BuildPlacement(frame, generatedIds, request, cancellationToken);
            stack.Pop();
            if (stack.TryPeek(out var parent))
                parent.Children.Add(build);
            else
                completed = build;
        }

        var result = completed ?? throw new InvalidOperationException("Activity template placement produced no root.");
        return new(
            result.Root,
            result.ResumeTargets,
            new(result.LayoutSegments),
            result.RuntimeRequirements);
    }

    private PlacementBuild BuildPlacement(
        PlacementFrame frame,
        ISet<string> generatedIds,
        ActivityTemplatePlacementRequest rootRequest,
        CancellationToken cancellationToken)
    {
        var boundaryRootId = ResolveBoundaryRootAuthoredNodeId(frame, rootRequest);
        var nodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var node in frame.Template.NodesById.Values.OrderBy(x => x.ExecutableNodeId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            nodeIds[node.ExecutableNodeId] =
                boundaryRootId is not null && StringComparer.Ordinal.Equals(node.ExecutableNodeId, frame.Template.Root.ExecutableNodeId)
                    ? boundaryRootId
                    : GenerateId("node", frame.Origin, $"node:{node.ExecutableNodeId}", generatedIds);
        }

        var placedNodes = new Dictionary<string, ExecutableNode>(StringComparer.Ordinal);
        var pending = new Stack<(ExecutableNode Node, bool Visited)>();
        pending.Push((frame.Template.Root, false));
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!current.Visited)
            {
                pending.Push((current.Node, true));
                foreach (var child in current.Node.ChildSlots.SelectMany(x => x.Activities).Reverse())
                    pending.Push((child, false));
                continue;
            }

            var childSlots = current.Node.ChildSlots
                .Select(slot => new ExecutableChildSlot(
                    slot.Name,
                    slot.Activities.Select(child => placedNodes[child.ExecutableNodeId]).ToArray(),
                    slot.OperatorSchedulingCapability))
                .ToList();
            if (ReferenceEquals(current.Node, frame.Template.Root) && frame.Children.Count > 0)
            {
                foreach (var slot in ReconstructOccurrenceRoots(frame.Children)
                             .GroupBy(x => x.ChildSlotName, StringComparer.Ordinal)
                             .OrderBy(x => x.Key, StringComparer.Ordinal))
                    MergeChildSlot(childSlots, slot.Key, slot.OrderBy(x => x.ChildIndex).Select(x => x.Root));
            }

            var isBoundary = ReferenceEquals(current.Node, frame.Template.Root);
            var descriptor = isBoundary
                ? StampBoundaryDescriptor(current.Node.Descriptor, frame, frame.Children, nodeIds)
                : current.Node.Descriptor;
            var metadata = current.Node.Metadata.ToDictionary(x => x.Key, x => x.Value, StringComparer.Ordinal);
            metadata["activity.invocationOrigin"] = JsonSerializer.Serialize(frame.Origin.Segments);
            metadata["activity.placementNamespace"] = placementHasher.ComputeHash(frame.Origin.ToCanonicalBytes());
            metadata["activity.templateNodeId"] = current.Node.ExecutableNodeId;
            if (isBoundary)
            {
                metadata["activity.definitionId"] = frame.Publication.DefinitionId;
                metadata["activity.definitionVersionId"] = frame.Publication.DefinitionVersionId;
                metadata["activity.version"] = frame.Publication.Version;
                metadata["activity.templateHash"] = frame.Template.TemplateHash;
                metadata["activity.sourceReferenceId"] = frame.SourceReference.SourceReferenceId;
                if (StringComparer.Ordinal.Equals(descriptor.ConsumerKey, "elsa.graph-activity"))
                    metadata["graph.templateHash"] = frame.Template.TemplateHash;
            }

            var inputBindings = isBoundary && stackIsRoot(frame)
                ? rootRequest.BoundaryInputBindings
                : isBoundary && frame.OccurrenceOverlay is not null
                    ? CompileOccurrenceInputs(current.Node, frame)
                    : current.Node.InputBindings;
            var outputCaptures = isBoundary && stackIsRoot(frame)
                ? rootRequest.BoundaryOutputCaptures
                : current.Node.OutputCaptures;
            var activityContract = isBoundary
                ? StampBoundaryContract(
                    current.Node.ActivityContract,
                    descriptor,
                    frame.ActivityTypeKey,
                    frame.Publication.Version)
                : current.Node.ActivityContract;
            placedNodes[current.Node.ExecutableNodeId] = new(
                nodeIds[current.Node.ExecutableNodeId],
                isBoundary && stackIsRoot(frame) ? rootRequest.InvocationOrigin.Segments.LastOrDefault(x => x.Kind == ActivityInvocationOriginSegmentKind.AuthoredNode)?.Id ?? current.Node.AuthoredActivityId : current.Node.AuthoredActivityId,
                isBoundary ? frame.ActivityTypeKey : current.Node.ActivityType,
                isBoundary ? frame.Publication.Version : current.Node.ActivityTypeVersion,
                descriptor,
                inputBindings,
                outputCaptures,
                metadata,
                childSlots,
                isBoundary && frame.OccurrenceOverlay is not null
                    ? frame.OccurrenceOverlay.Structure
                    : current.Node.Structure,
                activityContract,
                current.Node.IntrinsicKind,
                current.Node.IntrinsicVariable);
        }

        var resumeTargets = new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal);
        foreach (var target in frame.Template.ResumeTargets.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var id = GenerateId("resume", frame.Origin, $"resume:{target.Key}", generatedIds);
            resumeTargets.Add(id, target.Value with
            {
                ResumeTargetId = id,
                ExecutableNodeId = nodeIds[target.Value.ExecutableNodeId],
                LocalResumeTargetId = target.Value.LocalResumeTargetId ?? target.Value.ResumeTargetId
            });
        }
        foreach (var child in frame.Children)
            foreach (var target in child.ResumeTargets)
                resumeTargets.Add(target.Key, target.Value);

        var layoutSegments = new List<ExecutableLayoutBoundarySegment>();
        var sourceSegment = frame.SourceReference.LayoutSidecar.BoundarySegments.FirstOrDefault();
        var records = (sourceSegment?.Records ?? [])
            .Select(record => record with
            {
                ExecutableNodeId = nodeIds.GetValueOrDefault(record.TemplateNodeId, record.ExecutableNodeId)
            })
            .ToArray();
        var layoutId = GenerateId("layout", frame.Origin, "layout:boundary", generatedIds);
        layoutSegments.Add(new(
            layoutId,
            frame.Origin,
            frame.Template.TemplateHash,
            records,
            frame.Children.Select(x => x.Origin).ToArray()));
        layoutSegments.AddRange(frame.Children.SelectMany(x => x.LayoutSegments));

        var requirements = frame.Template.RuntimeRequirements
            .Concat(frame.Children.SelectMany(x => x.RuntimeRequirements))
            .Distinct()
            .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
            .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
            .ToArray();
        return new(
            placedNodes[frame.Template.Root.ExecutableNodeId],
            resumeTargets,
            layoutSegments,
            requirements,
            frame.Origin,
            frame.OccurrenceId,
            frame.ParentOccurrenceId,
            frame.ChildSlotName,
            frame.ChildIndex);

        static bool stackIsRoot(PlacementFrame candidate) => candidate.OccurrenceId is null;
    }

    private IReadOnlyList<PlacementBuild> ReconstructOccurrenceRoots(IReadOnlyList<PlacementBuild> occurrences)
    {
        if (occurrences.Count == 0)
            return [];

        var byId = occurrences.ToDictionary(
            x => x.OccurrenceId ?? throw new InvalidOperationException("A nested placement occurrence must have an identity."),
            StringComparer.Ordinal);
        var children = occurrences
            .Where(x => x.ParentOccurrenceId is not null)
            .GroupBy(x => x.ParentOccurrenceId!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.Ordinal);
        foreach (var parentId in children.Keys)
            if (!byId.ContainsKey(parentId))
                throw new InvalidOperationException($"Activity placement occurrence '{parentId}' is not present in the exact template dependency set.");

        var remaining = occurrences.ToDictionary(
            x => x.OccurrenceId!,
            x => children.GetValueOrDefault(x.OccurrenceId!, []).Length,
            StringComparer.Ordinal);
        var ready = new Queue<string>(remaining.Where(x => x.Value == 0).Select(x => x.Key).Order(StringComparer.Ordinal));
        var rebuilt = new Dictionary<string, PlacementBuild>(StringComparer.Ordinal);
        while (ready.TryDequeue(out var occurrenceId))
        {
            var occurrence = byId[occurrenceId];
            var root = occurrence.Root;
            if (children.TryGetValue(occurrenceId, out var authoredChildren))
            {
                var childSlots = root.ChildSlots.ToList();
                foreach (var slot in authoredChildren
                             .GroupBy(x => x.ChildSlotName, StringComparer.Ordinal)
                             .OrderBy(x => x.Key, StringComparer.Ordinal))
                    MergeChildSlot(childSlots, slot.Key, slot.OrderBy(x => x.ChildIndex).Select(x => rebuilt[x.OccurrenceId!].Root));
                var placedNodeIds = authoredChildren.ToDictionary(
                    x => x.OccurrenceId!,
                    x => rebuilt[x.OccurrenceId!].Root.ExecutableNodeId,
                    StringComparer.Ordinal);
                root = CloneWithChildSlots(root, childSlots, RewriteStructure(root.Structure, placedNodeIds));
            }

            rebuilt.Add(occurrenceId, occurrence with { Root = root });
            if (occurrence.ParentOccurrenceId is null)
                continue;
            remaining[occurrence.ParentOccurrenceId]--;
            if (remaining[occurrence.ParentOccurrenceId] == 0)
                ready.Enqueue(occurrence.ParentOccurrenceId);
        }

        if (rebuilt.Count != occurrences.Count)
            throw new InvalidOperationException("Activity placement occurrence structure contains a cycle.");
        return occurrences
            .Where(x => x.ParentOccurrenceId is null)
            .OrderBy(x => x.ChildSlotName, StringComparer.Ordinal)
            .ThenBy(x => x.ChildIndex)
            .Select(x => rebuilt[x.OccurrenceId!])
            .ToArray();
    }

    private static void MergeChildSlot(
        IList<ExecutableChildSlot> slots,
        string slotName,
        IEnumerable<ExecutableNode> authoredChildren)
    {
        var existingIndex = slots.ToList().FindIndex(x => StringComparer.Ordinal.Equals(x.Name, slotName));
        var existing = existingIndex < 0 ? [] : slots[existingIndex].Activities;
        var capability = existingIndex < 0 ? null : slots[existingIndex].OperatorSchedulingCapability;
        var merged = new ExecutableChildSlot(slotName, existing.Concat(authoredChildren).ToArray(), capability);
        if (existingIndex < 0)
            slots.Add(merged);
        else
            slots[existingIndex] = merged;
    }

    private static Elsa.Activities.Runtime.Core.Models.ActivityContract? StampBoundaryContract(
        Elsa.Activities.Runtime.Core.Models.ActivityContract? contract,
        RuntimeActivityDescriptor descriptor,
        string activityTypeKey,
        string activityVersion)
    {
        if (contract is null)
            return null;

        return new Elsa.Activities.Runtime.Core.Models.ActivityContract(
            activityTypeKey,
            activityVersion,
            contract.DescriptorKind,
            descriptor.Payload,
            contract.Inputs.Values,
            contract.Result,
            contract.Outcomes,
            contract.Activation,
            contract.SideEffectProfile);
    }

    private static ExecutableNode CloneWithChildSlots(
        ExecutableNode node,
        IReadOnlyCollection<ExecutableChildSlot> childSlots,
        ExecutableActivityStructure? structure) => new(
        node.ExecutableNodeId,
        node.AuthoredActivityId,
        node.ActivityType,
        node.ActivityTypeVersion,
        node.Descriptor,
        node.InputBindings,
        node.OutputCaptures,
        node.Metadata,
        childSlots,
        structure,
        node.ActivityContract,
        node.IntrinsicKind,
        node.IntrinsicVariable);

    private IReadOnlyDictionary<string, RuntimeInputBinding> CompileOccurrenceInputs(
        ExecutableNode templateRoot,
        PlacementFrame frame)
    {
        var overlay = frame.OccurrenceOverlay!;
        var inputStates = overlay.InputBindings.Deserialize<ArgumentState[]>(OccurrenceSerializerOptions) ?? [];
        var contract = templateRoot.ActivityContract;
        if (contract is null)
        {
            if (inputStates.Length == 0)
                return templateRoot.InputBindings;

            throw new InvalidOperationException(
                $"Occurrence '{frame.OccurrenceId}' carries authored inputs but its exact template root has no pinned activity contract.");
        }

        return inputBindingCompiler.CompileAll(frame.OccurrenceId!, contract.Inputs.Values, inputStates);
    }

    private ExecutableActivityStructure? RewriteStructure(
        ExecutableActivityStructure? structure,
        IReadOnlyDictionary<string, string> placedNodeIds)
    {
        if (structure is null)
            return null;

        var rewritten = activityStructureService.RemapExecutableStructure(
            new ActivityNodeStructure(structure.Kind, structure.SchemaVersion, structure.Payload),
            placedNodeIds);
        return new(rewritten.Kind, rewritten.SchemaVersion, rewritten.Payload);
    }

    private RuntimeActivityDescriptor StampBoundaryDescriptor(
        RuntimeActivityDescriptor descriptor,
        PlacementFrame frame,
        IReadOnlyList<PlacementBuild> children,
        IReadOnlyDictionary<string, string> placedNodeIds)
    {
        if (descriptor.Payload.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Boundary descriptor '{descriptor.ConsumerKey}/{descriptor.SchemaVersion}' must contain an object payload.");
        var values = descriptor.Payload.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone(), StringComparer.Ordinal);
        values["definitionId"] = JsonSerializer.SerializeToElement(frame.Publication.DefinitionId);
        values["definitionVersionId"] = JsonSerializer.SerializeToElement(frame.Publication.DefinitionVersionId);
        values["version"] = JsonSerializer.SerializeToElement(frame.Publication.Version);
        values["templateHash"] = JsonSerializer.SerializeToElement(frame.Template.TemplateHash);
        values["sourceReferenceId"] = JsonSerializer.SerializeToElement(frame.SourceReference.SourceReferenceId);
        if (values.TryGetValue("entryNodeId", out var localEntry) && localEntry.ValueKind == JsonValueKind.String &&
            placedNodeIds.TryGetValue(localEntry.GetString() ?? string.Empty, out var placedEntryNodeId))
        {
            values["entryNodeId"] = JsonSerializer.SerializeToElement(placedEntryNodeId);
        }
        if (values.TryGetValue("entryOccurrenceId", out var entry) && entry.ValueKind == JsonValueKind.String)
        {
            var entryOccurrenceId = entry.GetString();
            var entryChild = children.SingleOrDefault(x => StringComparer.Ordinal.Equals(x.OccurrenceId, entryOccurrenceId));
            if (entryChild is not null)
                values["entryNodeId"] = JsonSerializer.SerializeToElement(entryChild.Root.ExecutableNodeId);
        }
        return new(descriptor.ConsumerKey, descriptor.SchemaVersion, JsonSerializer.SerializeToElement(values));
    }

    // #1007: resolves the authored node id the top-level boundary must adopt as its ExecutableNodeId when the
    // placement is nested inside a workflow container. Only the requested (top-level) frame qualifies — nested
    // occurrences keep their content-addressed ids — and only when the caller opts in and the invocation origin
    // carries the authoring node segment. Returns null to fall back to a generated content-addressed id.
    private static string? ResolveBoundaryRootAuthoredNodeId(PlacementFrame frame, ActivityTemplatePlacementRequest rootRequest)
    {
        if (!rootRequest.BindBoundaryRootToAuthoredNode || frame.OccurrenceId is not null)
            return null;

        var authoredNodeId = rootRequest.InvocationOrigin.Segments
            .LastOrDefault(segment => segment.Kind == ActivityInvocationOriginSegmentKind.AuthoredNode)?.Id;
        return string.IsNullOrWhiteSpace(authoredNodeId) ? null : authoredNodeId;
    }

    private string GenerateId(
        string prefix,
        ActivityInvocationOrigin origin,
        string localIdentity,
        ISet<string> generatedIds)
    {
        var identityOrigin = Extend(origin, new ActivityInvocationOriginSegment(ActivityInvocationOriginSegmentKind.NestedPlacement, localIdentity));
        var id = $"{prefix}-{placementHasher.ComputeHash(identityOrigin.ToCanonicalBytes())}";
        if (!generatedIds.Add(id))
            throw new InvalidOperationException($"Activity placement identity collision detected for '{id}'.");
        return id;
    }

    private static ActivityInvocationOrigin Extend(ActivityInvocationOrigin origin, params ActivityInvocationOriginSegment[] segments) =>
        new(origin.Segments.Concat(segments).ToArray());

    private static void ValidateExactBinding(
        ActivityDefinitionVersionPublication publication,
        ExecutableActivityTemplate template,
        WorkflowExecutableSourceReference reference,
        bool directSelection)
    {
        var lifecycleAllowed = directSelection
            ? publication.Lifecycle == Elsa.Activities.Design.Core.Models.ActivityDefinitionVersionLifecycle.Active
            : publication.Lifecycle is Elsa.Activities.Design.Core.Models.ActivityDefinitionVersionLifecycle.Active
                or Elsa.Activities.Design.Core.Models.ActivityDefinitionVersionLifecycle.Retired;
        if (!lifecycleAllowed ||
            !StringComparer.Ordinal.Equals(publication.TemplateId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(publication.TemplateHash, template.TemplateHash) ||
            !StringComparer.Ordinal.Equals(publication.SourceReferenceId, reference.SourceReferenceId) ||
            !StringComparer.Ordinal.Equals(reference.ArtifactId, template.TemplateId) ||
            !StringComparer.Ordinal.Equals(reference.DefinitionVersionId, publication.DefinitionVersionId))
            throw new InvalidOperationException($"Activity version '{publication.DefinitionVersionId}' does not match its exact template/Source Reference binding.");
    }

    private sealed class PlacementFrame(
        ActivityDefinitionVersionPublication publication,
        ExecutableActivityTemplate template,
        WorkflowExecutableSourceReference sourceReference,
        ActivityInvocationOrigin origin,
        string activityTypeKey,
        string? occurrenceId,
        string? parentOccurrenceId,
        string childSlotName,
        int childIndex,
        ExecutableActivityTemplateOccurrenceOverlay? occurrenceOverlay)
    {
        public ActivityDefinitionVersionPublication Publication { get; } = publication;
        public ExecutableActivityTemplate Template { get; } = template;
        public WorkflowExecutableSourceReference SourceReference { get; } = sourceReference;
        public ActivityInvocationOrigin Origin { get; } = origin;
        public string ActivityTypeKey { get; } = activityTypeKey;
        public string? OccurrenceId { get; } = occurrenceId;
        public string? ParentOccurrenceId { get; } = parentOccurrenceId;
        public string ChildSlotName { get; } = childSlotName;
        public int ChildIndex { get; } = childIndex;
        public ExecutableActivityTemplateOccurrenceOverlay? OccurrenceOverlay { get; } = occurrenceOverlay;
        public ExecutableActivityTemplateDependency[] Dependencies { get; } = template.DirectDependencies
            .OrderBy(x => x.OccurrenceId, StringComparer.Ordinal)
            .ThenBy(x => x.DefinitionVersionId, StringComparer.Ordinal)
            .ToArray();
        public int NextDependencyIndex { get; set; }
        public List<PlacementBuild> Children { get; } = [];
    }

    private sealed record PlacementBuild(
        ExecutableNode Root,
        IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> ResumeTargets,
        IReadOnlyList<ExecutableLayoutBoundarySegment> LayoutSegments,
        IReadOnlyCollection<Elsa.Activities.Runtime.Core.Models.RuntimeRequirement> RuntimeRequirements,
        ActivityInvocationOrigin Origin,
        string? OccurrenceId,
        string? ParentOccurrenceId,
        string ChildSlotName,
        int ChildIndex);
}
