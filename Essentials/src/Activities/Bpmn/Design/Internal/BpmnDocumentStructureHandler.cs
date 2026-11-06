using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>
/// Stateless singleton contribution for complete-source drafts. Compilation deliberately refuses
/// until complete-content admission and derived lowering have been qualified with #2736.
/// </summary>
internal sealed class BpmnDocumentStructureHandler : IActivityStructureHandler
{
    private const string ModelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly HashSet<string> IdentityNamespaces = new(StringComparer.Ordinal)
    {
        ModelNamespace, "http://www.omg.org/spec/BPMN/20100524/DI",
        "http://www.omg.org/spec/DD/20100524/DC", "http://www.omg.org/spec/DD/20100524/DI"
    };
    private static readonly HashSet<string> FlowNodeNames = new(StringComparer.Ordinal)
    {
        "task", "userTask", "serviceTask", "manualTask", "scriptTask", "businessRuleTask", "sendTask", "receiveTask",
        "callActivity", "subProcess", "adHocSubProcess", "transaction", "startEvent", "endEvent", "boundaryEvent",
        "intermediateCatchEvent", "intermediateThrowEvent", "exclusiveGateway", "inclusiveGateway", "parallelGateway",
        "eventBasedGateway", "complexGateway"
    };
    private static readonly HashSet<string> CompetingFields = new(StringComparer.Ordinal)
    {
        "elements", "sequenceFlows", "diagram", "pools", "lanes", "messageFlows", "isTransaction"
    };
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        // The document/binding extension dictionaries preserve future metadata. Unknown typed
        // native-node members must refuse, rather than disappear when children are replaced.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public string Kind => BpmnActivityIdentities.StructureKind;
    public string SchemaVersion => BpmnAuthoredDocument.SchemaVersion;
    public Type AuthoredPayloadType => typeof(BpmnAuthoredDocument);
    public bool SupportsScopedVariables => true;

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
        [new(BpmnActivityIdentities.ActivitiesSlotName, Read(activity).Activities)];

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => Read(activity).Variables;

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        var current = Read(activity);
        if (childProjections.Count != 1 || childProjections.Single().Name != BpmnActivityIdentities.ActivitiesSlotName ||
            childProjections.Single().Activities is null)
            throw Invalid("exactly one Bpmn.Activities child slot is required");

        var previous = current.Activities.ToArray();
        var children = childProjections.Single().Activities.ToArray();
        if (children.Length != previous.Length)
            throw Invalid("adding or removing native ownership requires an explicit document/binding edit");
        ValidateActivities(children, activity.NodeId);
        var previousIds = previous.Select(child => child.NodeId).ToHashSet(StringComparer.Ordinal);
        var nextIds = children.Select(child => child.NodeId).ToHashSet(StringComparer.Ordinal);
        var remap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!previousIds.SetEquals(nextIds))
        {
            // Generic subtree cloning replaces identities in projection order. Existing identities
            // cannot move while others are renamed: that would ambiguously retarget native config.
            for (var index = 0; index < previous.Length; index++)
            {
                if ((nextIds.Contains(previous[index].NodeId) || previousIds.Contains(children[index].NodeId)) &&
                    previous[index].NodeId != children[index].NodeId)
                    throw Invalid("simultaneous child reordering and identity replacement is ambiguous");
                remap.Add(previous[index].NodeId, children[index].NodeId);
            }
        }

        var updated = current with
        {
            Activities = children,
            Bindings = current.Bindings.Select(binding => binding with
            {
                ChildNodeId = Remap(binding.ChildNodeId),
                ListenerNodeId = Remap(binding.ListenerNodeId)
            }).ToArray()
        };
        var result = activity with
        {
            Structure = new(Kind, SchemaVersion, JsonSerializer.SerializeToElement(updated, SerializerOptions))
        };
        Read(result); // Replacement is atomic, including the binding invariants.
        return result;

        string? Remap(string? id) => id is not null && remap.TryGetValue(id, out var replacement) ? replacement : id;
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        Read(activity);
        throw new NotSupportedException(
            "Complete-document BPMN authoring schema 2.0.0 cannot compile until complete-content capability admission and derived lowering are qualified (#2736). This applies to nested processes and CanStartWorkflow=false.");
    }

    public ActivityNodeStructure RemapExecutableStructure(ActivityNodeStructure structure, IReadOnlyDictionary<string, string> authoredToExecutableNodeIds) =>
        throw new NotSupportedException("Complete-document BPMN authoring is not an executable structure schema.");

    private BpmnAuthoredDocument Read(ActivityNode activity)
    {
        if (activity.Structure is not { } structure || structure.Kind != Kind || structure.SchemaVersion != SchemaVersion)
            throw Invalid("the exact complete-document kind and schema version are required");
        if (structure.Payload.ValueKind != JsonValueKind.Object)
            throw Invalid("payload must be an object");
        RejectDuplicateMembers(structure.Payload);
        BpmnAuthoredDocument document;
        try
        {
            document = structure.Payload.Deserialize<BpmnAuthoredDocument>(SerializerOptions)
                ?? throw Invalid("payload is missing");
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException("Invalid complete-document BPMN authoring payload.", exception);
        }

        RejectCompetingFields(document.AdditionalProperties);
        if (string.IsNullOrWhiteSpace(document.DocumentXml) || string.IsNullOrWhiteSpace(document.ProcessId) ||
            document.Activities is null || document.Bindings is null || document.Variables is null)
            throw Invalid("documentXml, processId, activities, bindings and variables are required");

        var xml = ParseXml(document.DocumentXml);
        XNamespace model = ModelNamespace;
        if (xml.Root?.Name != model + "definitions")
            throw Invalid("source must contain BPMN definitions");
        var identities = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var element in xml.Root.DescendantsAndSelf())
        {
            if (!IdentityNamespaces.Contains(element.Name.NamespaceName) || element.Attribute("id") is not { } id)
                continue;
            try { XmlConvert.VerifyNCName(id.Value); }
            catch (XmlException exception) { throw new InvalidOperationException("Invalid BPMN XML identity.", exception); }
            if (!identities.TryAdd(id.Value, element))
                throw Invalid($"XML identity '{id.Value}' is duplicated");
        }
        if (!identities.TryGetValue(document.ProcessId, out var process) || process.Name != model + "process" || process.Parent != xml.Root)
            throw Invalid("the selected process must be a direct BPMN definitions process");

        ValidateActivities(document.Activities, activity.NodeId);
        var activities = document.Activities.ToDictionary(child => child.NodeId, StringComparer.Ordinal);
        var boundElements = new HashSet<string>(StringComparer.Ordinal);
        var boundActivities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in document.Bindings)
        {
            if (binding is null || string.IsNullOrWhiteSpace(binding.ElementId))
                throw Invalid("a binding requires an elementId");
            RejectCompetingFields(binding.AdditionalProperties);
            if (!identities.TryGetValue(binding.ElementId, out var element) || element.Parent != process ||
                element.Name.Namespace != model || !FlowNodeNames.Contains(element.Name.LocalName))
                throw Invalid("native bindings currently require direct flow nodes of the selected process; standard nested-scope projection is not qualified");
            if (!boundElements.Add(binding.ElementId))
                throw Invalid("an element has duplicate binding entries");
            if (binding.ChildNodeId is null && binding.ListenerNodeId is null)
                throw Invalid("a binding requires a child or listener reference");
            foreach (var nodeId in new[] { binding.ChildNodeId, binding.ListenerNodeId }.OfType<string>())
            {
                if (!activities.TryGetValue(nodeId, out var child) || !boundActivities.Add(nodeId))
                    throw Invalid("each native child or listener must resolve exactly once");
                if (element.Name.LocalName is "subProcess" or "adHocSubProcess" or "transaction" &&
                    child.Structure?.Kind == Kind)
                    throw Invalid("a standard subprocess body cannot also persist an independent BPMN activity body; scope projection is not qualified");
            }
        }
        if (boundActivities.Count != activities.Count)
            throw Invalid("a native activity has no binding");
        var variableKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in document.Variables)
        {
            if (variable is null || string.IsNullOrWhiteSpace(variable.ReferenceKey) || variable.Type is null || !variableKeys.Add(variable.ReferenceKey))
                throw Invalid("scoped variables require distinct reference keys and types");
        }
        if (structure.Payload.TryGetProperty("bindings", out var rawBindings) && rawBindings.ValueKind == JsonValueKind.Array)
            foreach (var binding in rawBindings.EnumerateArray())
                RejectDuplicateMembers(binding);
        return document;
    }

    private static XDocument ParseXml(string source)
    {
        try
        {
            using var reader = XmlReader.Create(new StringReader(source), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        }
        catch (XmlException exception)
        {
            throw new InvalidOperationException("Invalid complete-document BPMN XML source.", exception);
        }
    }

    private static void ValidateActivities(IEnumerable<ActivityNode> activities, string ownerId)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal) { ownerId };
        foreach (var child in activities)
        {
            if (child is null || string.IsNullOrWhiteSpace(child.NodeId) || string.IsNullOrWhiteSpace(child.ActivityVersionId) ||
                child.Inputs is null || child.Outputs is null || !identities.Add(child.NodeId))
                throw Invalid("native activities require distinct identities, versions, inputs and outputs");
        }
    }

    private static void RejectCompetingFields(Dictionary<string, JsonElement>? properties)
    {
        if (properties is not null && properties.Keys.Any(CompetingFields.Contains))
            throw Invalid("flat graph and DI mirrors cannot accompany the canonical XML document");
    }

    private static void RejectDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw Invalid("payload and binding records must be objects");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!names.Add(property.Name))
                throw Invalid("duplicate payload or binding members are ambiguous");
    }

    private static InvalidOperationException Invalid(string message) => new($"Invalid complete-document BPMN authoring: {message}.");
}
