using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Runtime.Core.Models;
using LibModel = global::Bpmn.Model;
using Process = Elsa.Activities.Bpmn.Core.BpmnActivityIdentities;

namespace Elsa.Activities.Bpmn.Interchange.Services;

/// <summary>Mechanical authored projection; the direct runtime validates the executable profile and graph.</summary>
internal static class BpmnAuthoredDirectProjection
{
    internal const string DiagramNodePrefix = "bpmn-diagram:";
    private static readonly JsonSerializerOptions Strict = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static (BpmnModelDefinition Definition, BpmnModelPublicationProvenance Source, IReadOnlyList<ServiceSelection> Services) Project(
        WorkflowDefinitionVersion version, WorkflowDefinitionVersionLayout? layout)
    {
        try
        {
            var state = ReadState(version);
            if (state is null || state.Variables is null || state.Inputs is null || state.Outputs is null ||
                state.Variables.Any() || state.Inputs.Any() || state.Outputs.Any() || state.StrategyOptions is not null)
                throw new BpmnModelValidationException("Direct authored BPMN currently requires a closed workflow without variables or strategy overrides.");
            var root = state.RootActivity;
            if (root is null || root.Intrinsic is not null || root.ExecutionPlacement != ActivityExecutionPlacement.Inline ||
                root.Inputs is null || root.Outputs is null || root.Inputs.Any() || root.Outputs.Any() ||
                root.Structure is not { Kind: Process.StructureKind, SchemaVersion: Process.StructureSchemaVersion } structure)
                throw new BpmnModelValidationException("Direct publication requires a supported root BpmnProcess without native arguments or placement overrides.");
            var authored = structure.Payload.Deserialize<BpmnAuthoredStructure>(Strict)
                ?? throw new BpmnModelValidationException("The authored BPMN structure is missing.");
            if (authored.Activities.Count != 0 || authored.Pools.Count != 0 || authored.MessageFlows.Count != 0)
                throw new BpmnModelValidationException("The initial authored direct profile does not support native children or collaboration/message-flow bindings.");
            var humans = new List<string>();
            var claims = new List<string>();
            var services = new List<ServiceSelection>();
            var elements = new List<LibModel.BpmnElement>();
            foreach (var element in authored.Elements)
            {
                if (element.ChildNodeId is not null || element.ListenerNodeId is not null)
                    throw new BpmnModelValidationException($"Element '{element.ElementId}' has native child/listener intent; it cannot be reinterpreted as direct human work.");
                var binding = element.ElementType == BpmnElementTypes.UserTask ? "human:" + element.ElementId : null;
                if (binding is not null) humans.Add(binding);
                if (element.ElementType == BpmnElementTypes.ServiceTask)
                {
                    if (!element.Properties.TryGetValue(BpmnDirectBindingMetadata.PropertyKey, out var serviceMetadata))
                        throw InvalidBinding(element.ElementId);
                    var service = ReadServiceBinding(element.ElementId, serviceMetadata);
                    services.Add(service);
                    binding = service.Binding.BindingId;
                }
                else if (element.Properties.TryGetValue(BpmnDirectBindingMetadata.PropertyKey, out var metadata) &&
                    ValidateHumanBinding(element, metadata)) claims.Add(binding!);
                elements.Add(new LibModel.BpmnElement(element.ElementId, element.ElementType, element.Name, binding,
                    element.LaneId, element.DefaultFlowId,
                    element.EventDefinitions.Select(value => new LibModel.BpmnEventDefinition(value.Type, value.Properties)).ToArray(),
                    element.Properties.Where(value => value.Key != BpmnDirectBindingMetadata.PropertyKey)
                        .ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal),
                    element.AttachedToRef, element.CancelActivity,
                    element.LoopCharacteristics is { } loop ? new LibModel.BpmnLoopCharacteristics(loop.IsSequential, loop.Cardinality, loop.CollectionVariable, loop.ItemVariable) : null,
                    element.IsForCompensation, element.CompensationHandlerElementId, element.IsTransaction, element.TriggeredByEvent));
            }
            if (services.Count > 0 && (services.Count != 1 || humans.Count != 0))
                throw new BpmnModelValidationException("The authored native-service profile requires one service task and no human bindings.");
            var process = new LibModel.BpmnProcessDefinition(version.DefinitionId, IsExecutable: true,
                IsTransaction: authored.IsTransaction, Elements: elements,
                SequenceFlows: authored.SequenceFlows.Select(flow => new LibModel.BpmnSequenceFlow(flow.FlowId,
                    flow.SourceRef, flow.TargetRef, flow.Name, flow.ConditionOutcome, flow.IsDefault)).ToArray(),
                Lanes: authored.Lanes.Select(lane => new LibModel.BpmnLane(lane.LaneId, lane.PoolId, lane.Name)).ToArray(),
                Variables: authored.Variables.Select(variable => new LibModel.BpmnVariableDeclaration(variable.Name)).ToArray());
            var records = (layout?.Records ?? []).Select(record => new WorkflowExecutableLayoutRecord(record.NodeId,
                record.X, record.Y, record.Width, record.Height, record.AdditionalProperties)).ToList();
            if (authored.Diagram is { } diagram)
            {
                var id = DiagramNodePrefix + root.NodeId;
                if (records.Any(record => record.NodeId == id))
                    throw new BpmnModelValidationException("The source layout uses a reserved direct BPMN diagram identity.");
                records.Add(new(id, 0, 0, AdditionalProperties: diagram));
            }
            var presentation = (layout?.ActivityPresentation ?? []).Select(record =>
                new WorkflowExecutableActivityPresentationRecord(record.NodeId, record.DisplayName, record.Description)).ToList();
            if (!presentation.Any(record => record.ExecutableNodeId == root.NodeId))
                presentation.Add(new(root.NodeId, "BPMN process"));
            // Claim-required work selects the explicit format 6 profile; unclaimed human processes keep format 1 unchanged.
            var material = services.Count != 0
                ? new BpmnModelDefinition(3, JsonSerializer.SerializeToElement(process, Strict), humans, services.Select(value => value.Binding).ToArray())
                : claims.Count != 0
                    ? new BpmnModelDefinition(6, JsonSerializer.SerializeToElement(process, Strict), humans, null, null, null, claims)
                    : new BpmnModelDefinition(1, JsonSerializer.SerializeToElement(process, Strict), humans);
            return (material, new(version.Id, version.Version, records, presentation), services);
        }
        catch (JsonException exception)
        {
            throw new BpmnModelValidationException($"Authored BPMN version '{version.Id}' has invalid or unsupported source JSON.", exception);
        }
    }

    internal static WorkflowDefinitionState? ReadState(WorkflowDefinitionVersion version)
    {
        try
        {
            return string.IsNullOrWhiteSpace(version.StateSource) ? version.State
                : JsonSerializer.Deserialize<WorkflowDefinitionState>(version.StateSource, Strict);
        }
        catch (JsonException exception)
        {
            throw new BpmnModelValidationException($"Authored BPMN version '{version.Id}' has invalid or unsupported source JSON.", exception);
        }
    }

    /// <summary>Validates one owned human binding and returns whether its completion requires a claim.</summary>
    private static bool ValidateHumanBinding(BpmnElement element, string metadata)
    {
        using var document = JsonDocument.Parse(BpmnDirectBindingMetadata.Validate(metadata, element.ElementId));
        var value = document.RootElement;
        var properties = value.EnumerateObject().ToArray();
        if (properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length ||
            properties.Any(property => property.Name is not ("formatVersion" or "kind" or "claimRequired")) ||
            !value.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number) || number != 1 ||
            !value.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String || kind.GetString() != "human" ||
            element.ElementType != BpmnElementTypes.UserTask ||
            value.TryGetProperty("claimRequired", out var claim) && claim.ValueKind is not (JsonValueKind.False or JsonValueKind.True))
            throw new BpmnModelValidationException($"Element '{element.ElementId}' requires a supported direct human binding.");
        return value.TryGetProperty("claimRequired", out var required) && required.ValueKind == JsonValueKind.True;
    }

    internal sealed record ServiceSelection(BpmnNativeServiceBinding Binding, string SourceReferenceId);

    private static ServiceSelection ReadServiceBinding(string elementId, string metadata)
    {
        using var document = JsonDocument.Parse(BpmnDirectBindingMetadata.Validate(metadata, elementId));
        var value = document.RootElement;
        RequireProperties(value, elementId, "formatVersion", "kind", "sourceReferenceId", "executable", "businessErrorCode", "faultPolicy");
        if (ReadInteger(value, "formatVersion", elementId) != 1 || ReadString(value, "kind", elementId) != "nativeService")
            throw InvalidBinding(elementId);
        var pin = value.GetProperty("executable");
        RequireProperties(pin, elementId, "artifactId", "definitionId", "definitionVersionId", "artifactVersion", "artifactHash");
        var identity = new WorkflowExecutableIdentity(ReadString(pin, "artifactId", elementId), ReadString(pin, "definitionId", elementId),
            ReadString(pin, "definitionVersionId", elementId), ReadString(pin, "artifactVersion", elementId), ReadString(pin, "artifactHash", elementId));
        var policy = value.GetProperty("faultPolicy");
        RequireProperties(policy, elementId, "formatVersion", "disposition", "maxAttempts");
        var attempts = ReadInteger(policy, "maxAttempts", elementId);
        var disposition = ReadString(policy, "disposition", elementId) switch
        {
            "FinalFault" => BpmnServiceFaultDisposition.FinalFault,
            "HoldForRetry" => BpmnServiceFaultDisposition.HoldForRetry,
            _ => throw InvalidBinding(elementId)
        };
        if (ReadInteger(policy, "formatVersion", elementId) != 1 || attempts is < 1 or > BpmnServiceFaultPolicy.MaximumAttempts ||
            disposition == BpmnServiceFaultDisposition.FinalFault && attempts != 1)
            throw InvalidBinding(elementId);
        return new(new("service:" + elementId, identity, ReadString(value, "businessErrorCode", elementId), new(1, disposition, attempts)),
            ReadString(value, "sourceReferenceId", elementId));
    }

    private static void RequireProperties(JsonElement value, string elementId, params string[] names)
    {
        if (value.ValueKind != JsonValueKind.Object) throw InvalidBinding(elementId);
        var actual = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (actual.Length != names.Length || !actual.ToHashSet(StringComparer.Ordinal).SetEquals(names))
            throw InvalidBinding(elementId);
    }

    private static string ReadString(JsonElement value, string name, string elementId) =>
        value.GetProperty(name) is { ValueKind: JsonValueKind.String } property && !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()! : throw InvalidBinding(elementId);

    private static int ReadInteger(JsonElement value, string name, string elementId) =>
        value.GetProperty(name) is { ValueKind: JsonValueKind.Number } property && property.TryGetInt32(out var number)
            ? number : throw InvalidBinding(elementId);

    private static BpmnModelValidationException InvalidBinding(string elementId) =>
        new($"Element '{elementId}' requires a complete supported native-service binding, published pin/source and explicit fault policy.");
}
