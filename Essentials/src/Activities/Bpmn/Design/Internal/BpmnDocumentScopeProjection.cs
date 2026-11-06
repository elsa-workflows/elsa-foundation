using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Pure source views. XML and native records are stored only on the physical owner.</summary>
internal static class BpmnDocumentScopeProjection
{
    private static JsonSerializerOptions Json => BpmnDocumentStructureHandler.SerializerOptions;

    internal static bool IsScope(XElement element) => element.Name.NamespaceName == BpmnDocumentStructureHandler.ModelNamespace &&
        element.Name.LocalName is "process" or "subProcess" or "adHocSubProcess" or "transaction";

    internal static bool HasStandardScopeChain(XElement element, XElement definitions)
    {
        for (XElement? current = element; current is not null && IsScope(current); current = current.Parent)
            if (current.Name.LocalName == "process") return current.Parent == definitions;
        return false;
    }

    internal static ActivityNode CreateProcessView(ActivityNode owner, string processId)
    {
        var source = BpmnDocumentStructureHandler.Read(owner);
        var index = Index(source);
        if (!index.TryGetValue(processId, out var process) || process.Name.LocalName != "process" || process.Parent?.Name.LocalName != "definitions")
            throw Invalid("an independent process view requires a direct definitions process");
        return processId == source.ProcessId ? owner : CreateView(owner, processId);
    }

    internal static IReadOnlyCollection<ActivityChildProjection> Project(ActivityNode owner, string scopeId)
    {
        var source = BpmnDocumentStructureHandler.Read(owner);
        var index = Index(source);
        if (!index.TryGetValue(scopeId, out var scope) || !IsScope(scope)) throw Invalid("the scope address is unavailable");
        var nativeIds = NativeIds(source, index, new HashSet<string>(StringComparer.Ordinal) { scopeId });
        var native = source.Activities.Where(child => nativeIds.Contains(child.NodeId));
        var scopes = scope.Elements().Where(IsScope)
            .Where(element => source.ScopeHeaders.Any(header => header.ScopeElementId == (string?)element.Attribute("id")))
            .Select(element => CreateView(owner, (string)element.Attribute("id")!));
        return [new(BpmnActivityIdentities.ActivitiesSlotName, native.Concat(scopes).ToArray())
        { OutcomeProjections = BpmnDocumentAdmission.Outcomes(owner, scopeId) }];
    }

    internal static ActivityNode Replace(ActivityNode owner, string scopeId, IReadOnlyCollection<ActivityChildProjection> projections)
    {
        var source = BpmnDocumentStructureHandler.Read(owner);
        if (projections.Count != 1 || projections.Single().Name != BpmnActivityIdentities.ActivitiesSlotName || projections.Single().Activities is null)
            throw Invalid("exactly one Bpmn.Activities child slot is required");
        var previous = Project(owner, scopeId).Single().Activities.ToArray();
        var children = projections.Single().Activities.ToArray();
        if (previous.Length != children.Length) throw Invalid("ownership addition/removal requires an explicit canonical document edit");
        BpmnDocumentStructureHandler.ValidateActivities(children, owner.NodeId);
        var previousNative = previous.Where(node => !IsView(node)).ToArray();
        var nextNative = children.Where(node => !IsView(node)).ToArray();
        if (previousNative.Length != nextNative.Length) throw Invalid("native and standard scope roles cannot be interchanged implicitly");
        foreach (var child in nextNative) RejectPhysicalView(child);
        var expectedViews = previous.Where(IsView).ToDictionary(node => ReadView(node).ScopeElementId, StringComparer.Ordinal);
        var nextViews = new Dictionary<string, ActivityNode>(StringComparer.Ordinal);
        foreach (var child in children.Where(IsView))
        {
            var view = ReadView(child);
            if (!expectedViews.ContainsKey(view.ScopeElementId) || !nextViews.TryAdd(view.ScopeElementId, child))
                throw Invalid("a child view does not name its expected owned XML scope");
        }
        if (nextViews.Count != expectedViews.Count) throw Invalid("an owned standard scope view is missing");

        var remap = NativeRemap(previousNative, nextNative);
        var index = Index(source);
        // An independent process is not part of this generic lexical tree. Its complete identities
        // must be handled by the future BPMN-owned source transform, never a partial clone.
        if (source.ScopeHeaders.Any(header => index[header.ScopeElementId].Name.LocalName == "process") &&
            (remap.Any(pair => pair.Key != pair.Value) || nextViews.Any(pair => pair.Value.NodeId != expectedViews[pair.Key].NodeId)))
            throw new NotSupportedException("Identity replacement across a multi-process document requires the complete BPMN-owned transform.");

        var directIds = NativeIds(source, index, new HashSet<string>(StringComparer.Ordinal) { scopeId });
        var replacementQueue = new Queue<ActivityNode>(nextNative);
        var updated = source with
        {
            Activities = source.Activities.Select(child => directIds.Contains(child.NodeId) ? replacementQueue.Dequeue() : child).ToArray(),
            Bindings = source.Bindings.Select(binding => index[binding.ElementId].Parent?.Attribute("id")?.Value == scopeId
                ? binding with { ChildNodeId = Map(binding.ChildNodeId), ListenerNodeId = Map(binding.ListenerNodeId) }
                : binding).ToArray()
        };
        foreach (var child in nextViews.Values)
        {
            var view = ReadView(child);
            var candidateOwner = ApplyHeader(view.Owner, child, view);
            var candidate = BpmnDocumentStructureHandler.Read(candidateOwner);
            var ownedScopes = OwnedScopes(source, view.ScopeElementId);
            AssertUnownedUnchanged(owner, source, candidateOwner, candidate, ownedScopes);
            updated = MergeOwned(updated, candidate, ownedScopes);
        }
        return Write(owner, updated);

        string? Map(string? id) => id is not null && remap.TryGetValue(id, out var replacement) ? replacement : id;
    }

    internal static bool IsView(ActivityNode activity) => activity.Structure?.Kind == BpmnDocumentScopeView.Kind;

    internal static BpmnDocumentScopeView ReadView(ActivityNode activity)
    {
        if (activity.Structure is not { Kind: BpmnDocumentScopeView.Kind, SchemaVersion: BpmnDocumentScopeView.SchemaVersion } structure)
            throw Invalid("an exact internal scope view is required");
        BpmnDocumentScopeView view;
        try { view = structure.Payload.Deserialize<BpmnDocumentScopeView>(Json) ?? throw Invalid("the view context is missing"); }
        catch (JsonException exception) { throw new InvalidOperationException("Invalid BPMN scope view context.", exception); }
        if (view.Owner is null || view.Variables is null) throw Invalid("a view requires its complete canonical owner and variables");
        var source = BpmnDocumentStructureHandler.Read(view.Owner);
        if (source.ScopeHeaders.All(header => header.ScopeElementId != view.ScopeElementId))
            throw Invalid("the view's compact scope header is unavailable");
        return view;
    }

    internal static ActivityNode ReplaceView(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> children)
    {
        var view = ReadView(activity);
        var owner = ApplyHeader(view.Owner, activity, view);
        return CreateView(Replace(owner, view.ScopeElementId, children), view.ScopeElementId);
    }

    internal static ActivityNode Write(ActivityNode owner, BpmnAuthoredDocument source)
    {
        var payload = JsonSerializer.SerializeToNode(source, Json)!.AsObject();
        if (source.ScopeHeaders.Count == 0 && !owner.Structure!.Payload.TryGetProperty("scopeHeaders", out _)) payload.Remove("scopeHeaders");
        var result = owner with { Structure = new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion, JsonSerializer.SerializeToElement(payload, Json)) };
        BpmnDocumentStructureHandler.Read(result);
        return result;
    }

    internal static void RejectPhysicalView(ActivityNode activity)
    {
        if (IsView(activity))
            throw Invalid("internal projected scope structures cannot be physically authored or persisted");
    }

    private static ActivityNode CreateView(ActivityNode owner, string scopeId)
    {
        var source = BpmnDocumentStructureHandler.Read(owner);
        var header = source.ScopeHeaders.SingleOrDefault(item => item.ScopeElementId == scopeId)
            ?? throw Invalid("the scope has no compact native header");
        return new(header.NodeId, header.ActivityVersionId, header.Inputs, header.Outputs,
            new(BpmnDocumentScopeView.Kind, BpmnDocumentScopeView.SchemaVersion,
                JsonSerializer.SerializeToElement(new BpmnDocumentScopeView(owner, scopeId, header.Variables), Json)))
        { ExecutionPlacement = header.ExecutionPlacement };
    }

    private static ActivityNode ApplyHeader(ActivityNode owner, ActivityNode activity, BpmnDocumentScopeView view)
    {
        if (activity.Intrinsic is not null) throw Invalid("a standard scope header cannot become an intrinsic");
        var source = BpmnDocumentStructureHandler.Read(owner);
        return Write(owner, source with
        {
            ScopeHeaders = source.ScopeHeaders.Select(header => header.ScopeElementId == view.ScopeElementId ? header with
            {
                NodeId = activity.NodeId, ActivityVersionId = activity.ActivityVersionId,
                Inputs = activity.Inputs.ToArray(), Outputs = activity.Outputs.ToArray(),
                Variables = view.Variables, ExecutionPlacement = activity.ExecutionPlacement
            } : header).ToArray()
        });
    }

    private static Dictionary<string, XElement> Index(BpmnAuthoredDocument source) =>
        BpmnDocumentStructureHandler.ParseXml(source.DocumentXml).Root!.DescendantsAndSelf()
            .Where(element => element.Name.NamespaceName == BpmnDocumentStructureHandler.ModelNamespace && element.Attribute("id") is not null)
            .ToDictionary(element => (string)element.Attribute("id")!, StringComparer.Ordinal);

    private static HashSet<string> OwnedScopes(BpmnAuthoredDocument source, string scopeId)
    {
        var scope = Index(source)[scopeId];
        return scope.DescendantsAndSelf().Where(IsScope).Select(element => (string?)element.Attribute("id"))
            .OfType<string>().ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> NativeIds(BpmnAuthoredDocument source, Dictionary<string, XElement> index, HashSet<string> scopes) =>
        source.Bindings.Where(binding => scopes.Contains((string?)index[binding.ElementId].Parent?.Attribute("id") ?? ""))
            .SelectMany(binding => new[] { binding.ChildNodeId, binding.ListenerNodeId }).OfType<string>().ToHashSet(StringComparer.Ordinal);

    private static Dictionary<string, string> NativeRemap(ActivityNode[] previous, ActivityNode[] next)
    {
        var before = previous.Select(child => child.NodeId).ToHashSet(StringComparer.Ordinal);
        var after = next.Select(child => child.NodeId).ToHashSet(StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (before.SetEquals(after)) return map;
        for (var index = 0; index < previous.Length; index++)
        {
            if ((after.Contains(previous[index].NodeId) || before.Contains(next[index].NodeId)) && previous[index].NodeId != next[index].NodeId)
                throw Invalid("simultaneous native reordering and identity replacement is ambiguous");
            map.Add(previous[index].NodeId, next[index].NodeId);
        }
        return map;
    }

    private static void AssertUnownedUnchanged(ActivityNode originalOwner, BpmnAuthoredDocument original, ActivityNode candidateOwner,
        BpmnAuthoredDocument candidate, HashSet<string> scopes)
    {
        if (!Equal(originalOwner with { Structure = null }, candidateOwner with { Structure = null }) ||
            !Equal(Unowned(original, scopes), Unowned(candidate, scopes)))
            throw Invalid("a child scope snapshot changed unowned source or configuration");
        if (!original.ScopeHeaders.Where(header => scopes.Contains(header.ScopeElementId)).Select(header => header.ScopeElementId).ToHashSet(StringComparer.Ordinal)
                .SetEquals(candidate.ScopeHeaders.Where(header => scopes.Contains(header.ScopeElementId)).Select(header => header.ScopeElementId)))
            throw Invalid("scope header ownership cannot be added or removed through a view");
        var index = Index(original);
        var before = original.Bindings.Where(binding => scopes.Contains((string?)index[binding.ElementId].Parent?.Attribute("id") ?? "")).ToArray();
        var after = candidate.Bindings.Where(binding => scopes.Contains((string?)index[binding.ElementId].Parent?.Attribute("id") ?? "")).ToArray();
        if (!before.Select(binding => binding.ElementId).ToHashSet(StringComparer.Ordinal).SetEquals(after.Select(binding => binding.ElementId)))
            throw Invalid("binding ownership cannot be added or removed through a view");
    }

    private static BpmnAuthoredDocument Unowned(BpmnAuthoredDocument source, HashSet<string> scopes)
    {
        var index = Index(source);
        var ownedNative = NativeIds(source, index, scopes);
        return source with
        {
            Activities = source.Activities.Where(child => !ownedNative.Contains(child.NodeId)).ToArray(),
            Bindings = source.Bindings.Where(binding => !scopes.Contains((string?)index[binding.ElementId].Parent?.Attribute("id") ?? "")).ToArray(),
            ScopeHeaders = source.ScopeHeaders.Where(header => !scopes.Contains(header.ScopeElementId)).ToArray()
        };
    }

    private static BpmnAuthoredDocument MergeOwned(BpmnAuthoredDocument target, BpmnAuthoredDocument incoming, HashSet<string> scopes)
    {
        var index = Index(target);
        var oldIds = NativeIds(target, index, scopes);
        var newIds = NativeIds(incoming, index, scopes);
        var replacements = new Queue<ActivityNode>(incoming.Activities.Where(child => newIds.Contains(child.NodeId)));
        if (oldIds.Count != newIds.Count) throw Invalid("native ownership cannot be added or removed through a view");
        var bindings = incoming.Bindings.ToDictionary(binding => binding.ElementId, StringComparer.Ordinal);
        var headers = incoming.ScopeHeaders.ToDictionary(header => header.ScopeElementId, StringComparer.Ordinal);
        return target with
        {
            Activities = target.Activities.Select(child => oldIds.Contains(child.NodeId) ? replacements.Dequeue() : child).ToArray(),
            Bindings = target.Bindings.Select(binding => scopes.Contains((string?)index[binding.ElementId].Parent?.Attribute("id") ?? "") ? bindings[binding.ElementId] : binding).ToArray(),
            ScopeHeaders = target.ScopeHeaders.Select(header => scopes.Contains(header.ScopeElementId) ? headers[header.ScopeElementId] : header).ToArray()
        };
    }

    private static bool Equal<T>(T left, T right) => JsonElement.DeepEquals(JsonSerializer.SerializeToElement(left, Json), JsonSerializer.SerializeToElement(right, Json));
    private static InvalidOperationException Invalid(string message) => new($"Invalid BPMN document scope projection: {message}.");
}
