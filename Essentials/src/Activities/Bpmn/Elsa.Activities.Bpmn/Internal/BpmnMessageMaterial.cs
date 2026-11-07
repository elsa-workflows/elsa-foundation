using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;
using Bpmn.Model;
using Bpmn.Semantics;
using Elsa.Activities.Bpmn.Core;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

/// <summary>Closed material factory/reader only; the existing runtime driver deliberately does not admit this profile.</summary>
internal static class BpmnMessageMaterial
{
    private const int MaximumBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions Wire = CreateOptions();
    internal static RuntimeModelMaterial Create(BpmnProcessDefinition process, string correlationInputName,
        IReadOnlyCollection<BpmnMessageBinding> bindings, WorkflowExecutableIdentity sourceLabels)
    {
        ArgumentNullException.ThrowIfNull(process); ArgumentNullException.ThrowIfNull(sourceLabels);
        var definition = new BpmnMessageModelDefinition(JsonSerializer.SerializeToElement(process, Wire), correlationInputName, bindings);
        ValidateGraph(definition);
        var inputs = new WorkflowExecutableInputContract(1, [new(correlationInputName, new("String"), true)]);
        var provisional = new RuntimeModelMaterial(1, sourceLabels, new(BpmnModelProtocol.Key, BpmnModelProtocol.Version),
            BpmnMessageModelDefinition.Profile, Encode(definition), inputs, [], [], []);
        var hash = provisional.ComputeContentHash();
        return new(1, new(hash, sourceLabels.DefinitionId, sourceLabels.DefinitionVersionId, sourceLabels.ArtifactVersion, hash),
            provisional.Protocol, provisional.Profile, provisional.Definition, inputs, [], [], []);
    }

    internal static BpmnMessageModelDefinition ReadChecked(RuntimeModelMaterial material)
    {
        ArgumentNullException.ThrowIfNull(material);
        if (material.Protocol != new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) ||
            material.Profile != BpmnMessageModelDefinition.Profile || material.Outputs.Count != 0 || material.Dependencies.Count != 0 ||
            material.RuntimeRequirements.Count != 0 || material.InputContract.Version != 1 || material.Identity.ArtifactHash != material.ComputeContentHash()) throw Invalid();
        var definition = Decode(material.Definition);
        var inputs = material.InputContract.Inputs.ToArray();
        if (inputs.Length != 1 || inputs[0].Name != definition.CorrelationInputName || inputs[0].Type != new TypeReference("String") ||
            !inputs[0].IsRequired || inputs[0].DefaultValue is not null) throw Invalid();
        return definition;
    }

    internal static JsonElement Encode(BpmnMessageModelDefinition definition)
    {
        var document = new Document(7, BpmnMessageModelDefinition.EnclosingOwner, definition.CorrelationInputName,
            definition.Process, definition.Bindings.Select(binding => new Binding(binding.ElementId, binding.BindingId,
                binding.Role, binding.MessageName, binding.BusinessKeySource, binding.PayloadType)).ToArray());
        var encoded = JsonSerializer.SerializeToElement(document, Wire);
        if (System.Text.Encoding.UTF8.GetByteCount(encoded.GetRawText()) > MaximumBytes) throw Invalid();
        return encoded;
    }

    internal static BpmnMessageModelDefinition Decode(JsonElement value)
    {
        try
        {
            if (System.Text.Encoding.UTF8.GetByteCount(value.GetRawText()) > MaximumBytes) throw Invalid();
            NoDuplicateFields(value); Fields(value, "formatVersion", "ownerPolicy", "correlationInputName", "process", "bindings");
            foreach (var item in value.GetProperty("bindings").EnumerateArray())
            {
                Fields(item, "elementId", "bindingId", "role", "messageName", "businessKeySource", "payloadType");
                Fields(item.GetProperty("payloadType"), "alias", "collectionKind", "schema", "schemaVersion");
            }
            var decoded = value.Deserialize<Document>(Wire) ?? throw Invalid();
            if (decoded.FormatVersion != 7 || decoded.OwnerPolicy != BpmnMessageModelDefinition.EnclosingOwner || decoded.Bindings is null) throw Invalid();
            var definition = new BpmnMessageModelDefinition(decoded.Process, decoded.CorrelationInputName, decoded.Bindings.Select(binding =>
                new BpmnMessageBinding(binding.ElementId, binding.BindingId, binding.Role, binding.MessageName, binding.BusinessKeySource, binding.PayloadType)).ToArray());
            ValidateGraph(definition); return definition;
        }
        catch (Exception error) when (error is JsonException or ArgumentException or FormatException or InvalidOperationException or NullReferenceException)
        { throw Invalid(); }
    }

    internal static BpmnGraph ValidateGraph(BpmnMessageModelDefinition definition)
    {
        try
        {
            var process = definition.Process.Deserialize<BpmnProcessDefinition>(Wire) ?? throw Invalid();
            if (process.IsTransaction || process.Variables.Count != 0 || process.Elements.Count > 4096 || process.SequenceFlows.Count > 8192) throw Invalid();
            var inventory = definition.Bindings.ToDictionary(binding => binding.ElementId, StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in process.Elements)
            {
                if (element.ElementType is not (BpmnElementTypes.StartEvent or BpmnElementTypes.EndEvent or BpmnElementTypes.ParallelGateway or BpmnElementTypes.IntermediateCatchEvent) ||
                    element.LoopCharacteristics is not null || element.IsForCompensation || element.TriggeredByEvent || element.ListenerBindingRef is not null ||
                    element.IsTransaction || element.Script is not null || element.Properties.Count != 0) throw Invalid();
                var message = element.ElementType == BpmnElementTypes.IntermediateCatchEvent || element.ElementType == BpmnElementTypes.StartEvent && element.EventDefinitions.Count != 0;
                if (!message)
                {
                    if (element.EventDefinitions.Count != 0 || element.BindingRef is not null || inventory.ContainsKey(element.ElementId)) throw Invalid();
                    continue;
                }
                if (element.EventDefinitions.Count != 1 || element.EventDefinitions.Single().Type != BpmnEventDefinitionTypes.Message ||
                    element.EventDefinitions.Single().Properties.Count != 1 ||
                    !element.EventDefinitions.Single().Properties.TryGetValue(BpmnEventDefinitionProperties.Name, out var name) ||
                    !inventory.TryGetValue(element.ElementId, out var binding) || name != binding.MessageName || !seen.Add(element.ElementId) ||
                    binding.Role != (element.ElementType == BpmnElementTypes.StartEvent ? BpmnMessageBinding.Start : BpmnMessageBinding.Catch) ||
                    element.BindingRef != binding.BindingId) throw Invalid();
            }
            if (seen.Count != inventory.Count) throw Invalid();
            // A Message start is selected with BpmnStartSignal by the future owned driver; it is never fake work.
            return BpmnGraph.Build(process, definition.Bindings.Where(binding => binding.Role == BpmnMessageBinding.Catch)
                .Select(binding => new BpmnBoundWork(binding.BindingId!)).ToArray(), BpmnHostCapabilities.None);
        }
        catch (Exception error) when (error is JsonException or BpmnExecutionException or ArgumentException or InvalidOperationException or NullReferenceException)
        { throw Invalid(); }
    }

    private static BpmnModelValidationException Invalid() => new("The explicit durable Message material or complete binding inventory is unsupported.");
    private static void Fields(JsonElement value, params string[] expected)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal)) is false) throw Invalid();
    }
    private static void NoDuplicateFields(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject()) { if (!names.Add(property.Name)) throw Invalid(); NoDuplicateFields(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) NoDuplicateFields(item);
    }
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, PropertyNameCaseInsensitive = false, NumberHandling = JsonNumberHandling.Strict, MaxDepth = 128 };
        options.Converters.Add(new TextConverter()); return options;
    }
    private sealed record Document(int FormatVersion, string OwnerPolicy, string CorrelationInputName, JsonElement Process, Binding[] Bindings);
    private sealed record Binding(string ElementId, string? BindingId, string Role, string MessageName, string BusinessKeySource, ValueTypeDescriptor PayloadType);
    private sealed class TextConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DecodeText(reader.GetString());
        public override string ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DecodeText(reader.GetString());
        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WriteStringValue(EncodeText(value));
        public override void WriteAsPropertyName(Utf8JsonWriter writer, string value, JsonSerializerOptions options) => writer.WritePropertyName(EncodeText(value));
        private static string EncodeText(string text)
        {
            var bytes = new byte[checked(text.Length * 2)];
            for (var index = 0; index < text.Length; index++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(index * 2, 2), text[index]);
            return "u16:" + Convert.ToBase64String(bytes);
        }
        private static string DecodeText(string? text)
        {
            if (text is null || !text.StartsWith("u16:", StringComparison.Ordinal)) throw new JsonException();
            var bytes = Convert.FromBase64String(text[4..]); if (bytes.Length % 2 != 0) throw new JsonException();
            var chars = new char[bytes.Length / 2];
            for (var index = 0; index < chars.Length; index++) chars[index] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(index * 2, 2));
            if (EncodeText(new string(chars)) != text) throw new JsonException(); return new string(chars);
        }
    }
}
