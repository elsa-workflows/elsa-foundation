using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

/// <summary>Validates retained placement identity/relationship data without reconstructing or recompiling a body.</summary>
internal sealed class RuntimePlacementReleaseInspector(IReleaseRuntimeSiteReader reader) : IReleaseOccurrenceInspector
{
    private static readonly string[] MetadataKeys = ["activity.invocationOrigin", "activity.placementNamespace", "activity.templateNodeId", "activity.definitionId",
        "activity.definitionVersionId", "activity.version", "activity.templateHash", "activity.sourceReferenceId"];
    private static readonly JsonSerializerOptions OriginJson = new() { PropertyNameCaseInsensitive = false, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, MaxDepth = 32 };
    private readonly ConditionalWeakTable<IReleaseContentReader, ConcurrentDictionary<ReleaseMemberKey, Lazy<PlacementIndex>>> _members = new();
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.runtime-placement-release-profile", "1");
    public IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; } = Array.AsReadOnly(MetadataKeys.Select(key => new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Metadata, key, 1))
        .Append(new(ReleaseRuntimeSiteKinds.Placement, "activity-template", 1)).ToArray());
    public ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!SupportedSites.Contains(site.Site) || site.Input is not null) throw Refuse("placement_site_unsupported");
            var context = reader.ReadNode(site, content);
            if (context.Workflow is null) throw Refuse("template_placement_unsupported");
            var index = _members.GetValue(content, _ => new()).GetOrAdd(context.Member.Key,
                _ => new(() => new(context, reader, content, cancellationToken), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            if (!index.Nodes.Contains(context.Node.ExecutableNodeId)) throw Refuse("placement_node_unbound");
            return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified));
        }
        catch (OperationCanceledException) { throw; }
        catch (ReleaseRuntimeArtifactException exception)
        { return ValueTask.FromResult(Failed(exception.Code)); }
        catch (Exception exception) when (exception is JsonException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return ValueTask.FromResult(Failed("invalid_placement_captured_content")); }
        ReleaseOccurrenceFacts Failed(string code) => new(code.Contains("unsupported", StringComparison.Ordinal) ? ReleaseSemanticStatus.Unsupported : ReleaseSemanticStatus.Invalid,
            diagnostics: [new(code, site.Member, site.Origin, site.Input)]);
    }

    private sealed class PlacementIndex
    {
        public HashSet<string> Nodes { get; } = new(StringComparer.Ordinal);
        public PlacementIndex(ReleaseRuntimeNodeContext context, IReleaseRuntimeSiteReader reader, IReleaseContentReader content, CancellationToken token)
        {
            var workflow = context.Workflow!;
            var frames = new Dictionary<string, Frame>(StringComparer.Ordinal);
            var groups = new Dictionary<string, List<ExecutableNode>>(StringComparer.Ordinal);
            foreach (var node in workflow.Nodes)
            {
                token.ThrowIfCancellationRequested();
                var hasAny = MetadataKeys.Any(node.Metadata.ContainsKey);
                if (!hasAny) continue;
                if (!node.Metadata.TryGetValue("activity.invocationOrigin", out var text) || !node.Metadata.TryGetValue("activity.placementNamespace", out var space) ||
                    !node.Metadata.TryGetValue("activity.templateNodeId", out var templateNode) || string.IsNullOrWhiteSpace(templateNode)) throw Refuse("incomplete_placement_origin");
                ReleaseRuntimeArtifactCodec.CheckJson(System.Text.Encoding.UTF8.GetBytes(text), new());
                var segments = JsonSerializer.Deserialize<ActivityInvocationOriginSegment[]>(text, OriginJson) ?? throw Refuse("invalid_placement_origin");
                if (segments.Length < 3 || segments[0].Kind != ActivityInvocationOriginSegmentKind.WorkflowRoot || segments[1].Kind != ActivityInvocationOriginSegmentKind.AuthoredNode ||
                    segments[2].Kind != ActivityInvocationOriginSegmentKind.TemplateBoundary || segments.Any(segment => !Enum.IsDefined(segment.Kind) || string.IsNullOrWhiteSpace(segment.Id)))
                    throw Refuse("invalid_placement_origin");
                if (segments[0].Id != workflow.Identity.DefinitionVersionId) throw Refuse("placement_workflow_origin_mismatch");
                for (var offset = 3; offset < segments.Length; offset += 2)
                    if (offset + 1 >= segments.Length || segments[offset].Kind != ActivityInvocationOriginSegmentKind.NestedPlacement || segments[offset + 1].Kind != ActivityInvocationOriginSegmentKind.TemplateBoundary)
                        throw Refuse("invalid_placement_origin");
                var origin = new ActivityInvocationOrigin(segments);
                if (space != origin.ComputePlacementNamespace()) throw Refuse("placement_namespace_mismatch");
                if (!groups.TryGetValue(space, out var nodes)) groups.Add(space, nodes = []);
                nodes.Add(node); Nodes.Add(node.ExecutableNodeId);
                if (!node.Metadata.ContainsKey("activity.templateHash")) continue;
                foreach (var key in MetadataKeys) if (!node.Metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value)) throw Refuse("incomplete_placement_association");
                if (frames.ContainsKey(space)) throw Refuse("duplicate_placement_boundary");
                var activity = reader.ReadActivityVersion(new(ReleaseRuntimeCodecs.ActivityVersionKind, node.Metadata["activity.definitionVersionId"]), content);
                var template = reader.ReadTemplate(new(ReleaseRuntimeCodecs.TemplateKind, activity.TemplateId), content);
                if (activity.Scope != context.Scope || template.Scope != context.Scope || template.Template.TemplateHash != activity.TemplateHash ||
                    node.Metadata["activity.definitionId"] != activity.DefinitionId || node.Metadata["activity.version"] != activity.Version ||
                    node.Metadata["activity.templateHash"] != activity.TemplateHash || node.Metadata["activity.sourceReferenceId"] != activity.SourceReferenceId ||
                    segments[^1].Id != activity.VersionId || node.Metadata["activity.templateNodeId"] != template.Template.Root.ExecutableNodeId ||
                    node.ActivityType != activity.ActivityTypeKey || node.ActivityTypeVersion != activity.Version)
                    throw Refuse("placement_catalog_association_mismatch");
                frames.Add(space, new(origin, node, activity, template.Template));
            }
            if (frames.Count != groups.Count) throw Refuse("orphan_placement_origin");
            var slots = workflow.Nodes.SelectMany(node => node.ChildSlots.Select(slot => ((node.ExecutableNodeId, slot.Name), slot.Activities.ToArray())))
                .ToDictionary(pair => pair.Item1, pair => pair.Item2);
            var expectedSlotKeys = new HashSet<(string, string)>();
            foreach (var (space, frame) in frames)
            {
                token.ThrowIfCancellationRequested();
                if (frame.Origin.Segments.Count > 3)
                {
                    var parentOrigin = new ActivityInvocationOrigin(frame.Origin.Segments.Take(frame.Origin.Segments.Count - 2).ToArray());
                    var occurrence = frame.Origin.Segments[^2].Id;
                    if (!frames.TryGetValue(parentOrigin.ComputePlacementNamespace(), out var parent) ||
                        !parent.Template.DirectDependencies.Any(dependency => dependency.OccurrenceId == occurrence && dependency.DefinitionVersionId == frame.Activity.VersionId))
                        throw Refuse("placement_parent_frame_missing");
                }
                var nodes = groups[space];
                if (nodes.Count != frame.Template.NodesById.Count || nodes.Select(node => node.Metadata["activity.templateNodeId"]).Distinct(StringComparer.Ordinal).Count() != nodes.Count)
                    throw Refuse("placement_template_node_inventory_mismatch");
                foreach (var node in nodes)
                {
                    if (!frame.Template.NodesById.TryGetValue(node.Metadata["activity.templateNodeId"], out var original) ||
                        node.Descriptor.ConsumerKey != original.Descriptor.ConsumerKey || node.Descriptor.SchemaVersion != original.Descriptor.SchemaVersion || node.IntrinsicKind != original.IntrinsicKind)
                        throw Refuse("placement_template_node_mismatch");
                    var generated = "node-" + new ActivityInvocationOrigin(frame.Origin.Segments.Append(new(ActivityInvocationOriginSegmentKind.NestedPlacement, "node:" + original.ExecutableNodeId)).ToArray()).ComputePlacementNamespace();
                    if (node.ExecutableNodeId != generated && !(ReferenceEquals(node, frame.Root) && !ReferenceEquals(node, workflow.RootActivity) && frame.Origin.Segments.Count == 3 && node.ExecutableNodeId == frame.Origin.Segments[1].Id))
                        throw Refuse("placement_executable_node_id_mismatch");
                    if (node.Descriptor.ConsumerKey == WellKnownRuntimeActivityConsumers.ClrActivity)
                    {
                        if (!node.Descriptor.Payload.TryGetProperty("typeAlias", out var alias) || !original.Descriptor.Payload.TryGetProperty("typeAlias", out var sourceAlias) || !JsonElement.DeepEquals(alias, sourceAlias))
                            throw Refuse("placement_clr_alias_mismatch");
                        if (ReferenceEquals(node, frame.Root))
                            foreach (var field in new[] { "definitionId", "definitionVersionId", "version", "templateHash", "sourceReferenceId" })
                                if (!node.Descriptor.Payload.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String || value.GetString() != node.Metadata["activity." + field])
                                    throw Refuse("placement_clr_descriptor_mismatch");
                    }
                }
                // Current CLR-source and activity-graph producers place children as exact occurrence templates.
                // An unrelated provider with inline template children requires its own qualified extension.
                if (frame.Template.NodesById.Values.Any(node => node.ChildSlots.Count != 0)) throw Refuse("inline_template_placement_unsupported");
                var occurrences = new Dictionary<string, Frame>(StringComparer.Ordinal);
                foreach (var dependency in frame.Template.DirectDependencies)
                {
                    var origin = new ActivityInvocationOrigin(frame.Origin.Segments.Concat(new[]
                    {
                        new ActivityInvocationOriginSegment(ActivityInvocationOriginSegmentKind.NestedPlacement, dependency.OccurrenceId),
                        new ActivityInvocationOriginSegment(ActivityInvocationOriginSegmentKind.TemplateBoundary, dependency.DefinitionVersionId)
                    }).ToArray());
                    if (!frames.TryGetValue(origin.ComputePlacementNamespace(), out var child) || child.Activity.VersionId != dependency.DefinitionVersionId ||
                        child.Activity.DefinitionId != dependency.DefinitionId || child.Activity.Version != dependency.Version || child.Template.TemplateId != dependency.TemplateId || child.Template.TemplateHash != dependency.TemplateHash)
                        throw Refuse("placement_dependency_frame_mismatch");
                    occurrences.Add(dependency.OccurrenceId, child);
                }
                var expectedSlots = new Dictionary<(string, string), List<(int Position, string Node)>>();
                foreach (var dependency in frame.Template.DirectDependencies)
                {
                    var parent = dependency.ParentOccurrenceId is null ? frame.Root : occurrences[dependency.ParentOccurrenceId].Root;
                    var slot = (parent.ExecutableNodeId, dependency.ChildSlotName);
                    if (!expectedSlots.TryGetValue(slot, out var children)) expectedSlots.Add(slot, children = []);
                    children.Add((dependency.ChildIndex, occurrences[dependency.OccurrenceId].Root.ExecutableNodeId));
                }
                foreach (var (slot, children) in expectedSlots)
                {
                    expectedSlotKeys.Add(slot);
                    var ordered = children.OrderBy(child => child.Position).ToArray();
                    if (ordered.Where((child, index) => child.Position != index).Any() || !slots.TryGetValue(slot, out var actual) || !actual.Select(node => node.ExecutableNodeId).SequenceEqual(ordered.Select(child => child.Node), StringComparer.Ordinal))
                        throw Refuse("placement_child_slot_inventory_mismatch");
                }
            }
            foreach (var node in workflow.Nodes.Where(node => Nodes.Contains(node.ExecutableNodeId)))
                foreach (var slot in node.ChildSlots)
                    if (!expectedSlotKeys.Contains((node.ExecutableNodeId, slot.Name))) throw Refuse("placement_extra_child_slot");
        }
    }
    private sealed record Frame(ActivityInvocationOrigin Origin, ExecutableNode Root, ReleaseActivityVersion Activity, ExecutableActivityTemplate Template);
    private static ReleaseRuntimeArtifactException Refuse(string code) => ReleaseRuntimeArtifactCodec.Refuse(code);
}
