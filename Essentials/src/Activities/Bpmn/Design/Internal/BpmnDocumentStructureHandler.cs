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
/// Stateless singleton contribution for complete-source drafts. Source validation retains unsupported
/// content; execution stays closed until lowering is qualified on every route.
/// </summary>
internal sealed class BpmnDocumentStructureHandler : IActivityStructureHandler
{
    internal const string ModelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
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
    private static readonly HashSet<string> HeaderAuthorityFields = new(CompetingFields, StringComparer.Ordinal)
    {
        "structure", "documentXml", "processId", "activities", "bindings", "scopeHeaders"
    };
    internal static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
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

    // Retention and XML routing are not an execution/occurrence proof for this authored schema.
    public ActivityChildResultFacts AssessChildResult(ActivityNode activity, string producerChildNodeId, string consumerChildNodeId) =>
        new(ActivityChildResultAvailability.Unknown) { EarlierProducerRunCanReachConsumer = null };

    public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) =>
        BpmnDocumentScopeProjection.Project(activity, Read(activity).ProcessId);

    public IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity) => Read(activity).Variables;

    public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections)
    {
        return BpmnDocumentScopeProjection.Replace(activity, Read(activity).ProcessId, childProjections);
    }

    public ActivityNodeStructure CompileExecutableStructure(ActivityNode activity)
    {
        Read(activity);
        throw new NotSupportedException("Complete-source BPMN compilation is not qualified. Retain this draft until workflow, reusable-template and Test Drive routes qualify complete semantics and exact resolved catalog types.");
    }

    public ActivityNodeStructure RemapExecutableStructure(ActivityNodeStructure structure, IReadOnlyDictionary<string, string> authoredToExecutableNodeIds) =>
        throw new NotSupportedException("Complete-document BPMN authoring is not an executable structure schema.");

    internal static BpmnAuthoredDocument Read(ActivityNode activity)
    {
        if (activity.Structure is not { } structure || structure.Kind != BpmnActivityIdentities.StructureKind || structure.SchemaVersion != BpmnAuthoredDocument.SchemaVersion)
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
            document.Activities is null || document.Bindings is null || document.Variables is null || document.ScopeHeaders is null)
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
            if (!BpmnXmlIdentity.IsValid(id.Value)) throw new InvalidOperationException("Invalid BPMN XML identity.");
            if (!identities.TryAdd(id.Value, element))
                throw Invalid($"XML identity '{id.Value}' is duplicated");
        }
        if (!identities.TryGetValue(document.ProcessId, out var process) || process.Name != model + "process" || process.Parent != xml.Root)
            throw Invalid("the selected process must be a direct BPMN definitions process");

        ValidateActivities(document.Activities, activity.NodeId);
        foreach (var native in document.Activities)
            BpmnDocumentScopeProjection.RejectPhysicalView(native);
        var activities = document.Activities.ToDictionary(child => child.NodeId, StringComparer.Ordinal);
        var scopeHeaders = new Dictionary<string, BpmnDocumentScopeHeader>(StringComparer.Ordinal);
        var nodeIds = activities.Keys.Append(activity.NodeId).ToHashSet(StringComparer.Ordinal);
        foreach (var header in document.ScopeHeaders)
        {
            if (header is null || string.IsNullOrWhiteSpace(header.ScopeElementId) || string.IsNullOrWhiteSpace(header.NodeId) ||
                string.IsNullOrWhiteSpace(header.ActivityVersionId) || header.Inputs is null || header.Outputs is null || header.Variables is null ||
                !Enum.IsDefined(header.ExecutionPlacement) || !nodeIds.Add(header.NodeId) || !scopeHeaders.TryAdd(header.ScopeElementId, header) ||
                header.ScopeElementId == document.ProcessId || !identities.TryGetValue(header.ScopeElementId, out var scopeElement) ||
                !BpmnDocumentScopeProjection.HasStandardScopeChain(scopeElement, xml.Root))
                throw Invalid("scope headers require distinct native identities, exact versions and standard XML scope addresses outside the physical root");
            if (header.AdditionalProperties?.Keys.Any(HeaderAuthorityFields.Contains) == true)
                throw Invalid("a physical scope header cannot persist an independent structure or semantic document");
            ValidateVariables(header.Variables);
        }
        var boundElements = new HashSet<string>(StringComparer.Ordinal);
        var boundActivities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var binding in document.Bindings)
        {
            if (binding is null || string.IsNullOrWhiteSpace(binding.ElementId))
                throw Invalid("a binding requires an elementId");
            RejectCompetingFields(binding.AdditionalProperties);
            if (!identities.TryGetValue(binding.ElementId, out var element) || element.Parent is not { } parent || !BpmnDocumentScopeProjection.IsScope(parent) ||
                element.Name.Namespace != model || !FlowNodeNames.Contains(element.Name.LocalName))
                throw Invalid("native bindings require direct flow nodes of a declared standard XML scope");
            foreach (var ancestor in element.Ancestors().Where(BpmnDocumentScopeProjection.IsScope))
                if ((string?)ancestor.Attribute("id") != document.ProcessId && !scopeHeaders.ContainsKey((string?)ancestor.Attribute("id") ?? ""))
                    throw Invalid("every configured binding's standard scope ancestors require compact scope headers");
            if (!boundElements.Add(binding.ElementId))
                throw Invalid("an element has duplicate binding entries");
            if (binding.ChildNodeId is null && binding.ListenerNodeId is null)
                throw Invalid("a binding requires a child or listener reference");
            foreach (var nodeId in new[] { binding.ChildNodeId, binding.ListenerNodeId }.OfType<string>())
            {
                if (!activities.TryGetValue(nodeId, out var child) || !boundActivities.Add(nodeId))
                    throw Invalid("each native child or listener must resolve exactly once");
                if (element.Name.LocalName is "subProcess" or "adHocSubProcess" or "transaction" &&
                    child.Structure?.Kind == BpmnActivityIdentities.StructureKind)
                    throw Invalid("a standard subprocess body cannot also persist an independent BPMN activity body; scope projection is not qualified");
            }
        }
        if (boundActivities.Count != activities.Count)
            throw Invalid("a native activity has no binding");
        ValidateVariables(document.Variables);
        foreach (var header in document.ScopeHeaders)
        {
            var element = identities[header.ScopeElementId];
            foreach (var ancestor in element.Ancestors().Where(BpmnDocumentScopeProjection.IsScope))
                if ((string?)ancestor.Attribute("id") != document.ProcessId && !scopeHeaders.ContainsKey((string?)ancestor.Attribute("id") ?? ""))
                    throw Invalid("a configured scope's standard ancestors require compact scope headers");
        }
        if (structure.Payload.TryGetProperty("bindings", out var rawBindings) && rawBindings.ValueKind == JsonValueKind.Array)
            foreach (var binding in rawBindings.EnumerateArray())
                RejectDuplicateMembers(binding);
        if (structure.Payload.TryGetProperty("scopeHeaders", out var rawHeaders) && rawHeaders.ValueKind == JsonValueKind.Array)
            foreach (var header in rawHeaders.EnumerateArray())
                RejectDuplicateMembers(header);
        return document;
    }

    internal static XDocument ParseXml(string source)
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

    internal static void ValidateActivities(IEnumerable<ActivityNode> activities, string ownerId)
    {
        var identities = new HashSet<string>(StringComparer.Ordinal) { ownerId };
        foreach (var child in activities)
        {
            if (child is null || string.IsNullOrWhiteSpace(child.NodeId) || string.IsNullOrWhiteSpace(child.ActivityVersionId) ||
                child.Inputs is null || child.Outputs is null || !identities.Add(child.NodeId))
                throw Invalid("native activities require distinct identities, versions, inputs and outputs");
        }
    }

    private static void ValidateVariables(IEnumerable<VariableDefinition> variables)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var variable in variables)
            if (variable is null || string.IsNullOrWhiteSpace(variable.ReferenceKey) || variable.Type is null || !keys.Add(variable.ReferenceKey))
                throw Invalid("scoped variables require distinct reference keys and types");
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
