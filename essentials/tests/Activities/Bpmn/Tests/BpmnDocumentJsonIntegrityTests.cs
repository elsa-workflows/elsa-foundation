using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Sequence.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;
using NativeSequence = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDocumentJsonIntegrityTests
{
    private const string Raw = """{ "escaped" : "\u0061\/\u003c", "number" : 12345678901234567890123456789012345, "fraction" : 1.0, "nested" : [ null, { "CaseKey" : "v" } ] }""";
    private static readonly JsonSerializerOptions Json = BpmnDocumentStructureHandler.SerializerOptions;
    private readonly BpmnDocumentStructureHandler _handler = new();

    [Fact]
    public void Typed_baseline_serialization_preserves_BPMN_extension_data_raw_values()
    {
        var owner = Owner(withScope: true);
        var source = BpmnDocumentStructureHandler.Read(owner);

        var serialized = JsonSerializer.SerializeToElement(source, Json);

        AssertRawPayloads(serialized, withScope: true);
        Assert.Equal(Raw, serialized.GetProperty("futureMetadata").GetRawText());
        Assert.Equal(Raw, serialized.GetProperty("bindings")[0].GetProperty("futureMetadata").GetRawText());
        Assert.Equal(Raw, serialized.GetProperty("scopeHeaders")[0].GetProperty("futureMetadata").GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void No_op_child_replacement_preserves_raw_literals_defaults_and_extensions(bool withScope)
    {
        var owner = Owner(withScope);
        var original = owner.Structure!.Payload.GetRawText();

        var replaced = _handler.ReplaceChildren(owner, _handler.ProjectChildren(owner));

        AssertRawPayloads(replaced.Structure!.Payload, withScope);
        Assert.Equal(withScope, replaced.Structure.Payload.TryGetProperty("scopeHeaders", out _));
        Assert.Equal(original, owner.Structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_child_identity_replacement_routes_the_binding_and_preserves_carried_raw_data(bool withScope)
    {
        var owner = Owner(withScope);
        var projection = Assert.Single(_handler.ProjectChildren(owner));
        var children = projection.Activities.Select(child => child.NodeId == "native"
            ? child with { NodeId = "edited-native", ActivityVersionId = "edited-version" }
            : child).ToArray();

        var replaced = _handler.ReplaceChildren(owner, [projection with { Activities = children }]);

        var payload = replaced.Structure!.Payload;
        Assert.Equal("edited-native", payload.GetProperty("activities")[0].GetProperty("nodeId").GetString());
        Assert.Equal("edited-version", payload.GetProperty("activities")[0].GetProperty("activityVersionId").GetString());
        Assert.Equal("edited-native", payload.GetProperty("bindings")[0].GetProperty("childNodeId").GetString());
        Assert.Equal(owner.Structure.Payload.GetProperty("documentXml").GetString(), payload.GetProperty("documentXml").GetString());
        Assert.Equal("native", owner.Structure.Payload.GetProperty("bindings")[0].GetProperty("childNodeId").GetString());
        AssertRawPayloads(payload, withScope);
    }

    [Fact]
    public void Explicit_empty_scope_headers_remain_present_after_no_op_replacement()
    {
        var owner = Owner(withScope: false);
        var properties = owner.Structure!.Payload.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        properties["scopeHeaders"] = Parse("[]");
        owner = owner with { Structure = new(owner.Structure.Kind, owner.Structure.SchemaVersion, JsonSerializer.SerializeToElement(properties, Json)) };

        var replaced = _handler.ReplaceChildren(owner, _handler.ProjectChildren(owner));

        Assert.Equal(0, replaced.Structure!.Payload.GetProperty("scopeHeaders").GetArrayLength());
        AssertRawPayloads(replaced.Structure.Payload, withScope: false);
    }

    private static void AssertRawPayloads(JsonElement payload, bool withScope)
    {
        Assert.Equal(Raw, payload.GetProperty("futureMetadata").GetRawText());
        Assert.Equal(Raw, payload.GetProperty("bindings")[0].GetProperty("futureMetadata").GetRawText());
        Assert.Equal(Raw, payload.GetProperty("variables")[0].GetProperty("default").GetProperty("value").GetRawText());
        foreach (var native in payload.GetProperty("activities").EnumerateArray())
        {
            Assert.Equal(Raw, native.GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetRawText());
            var structure = native.GetProperty("structure").GetProperty("payload");
            Assert.Equal(Raw, structure.GetProperty("variables")[0].GetProperty("default").GetProperty("value").GetRawText());
            Assert.Equal(Raw, structure.GetProperty("activities")[0].GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetRawText());
            var typed = structure.Deserialize<SequenceAuthoredStructure>(Json)!;
            Assert.Equal("native-local", Assert.Single(typed.Variables).ReferenceKey);
        }
        if (!withScope) return;
        var header = payload.GetProperty("scopeHeaders")[0];
        Assert.Equal(Raw, header.GetProperty("futureMetadata").GetRawText());
        Assert.Equal(Raw, header.GetProperty("variables")[0].GetProperty("default").GetProperty("value").GetRawText());
        Assert.Equal(Raw, header.GetProperty("inputs")[0].GetProperty("value").GetProperty("value").GetRawText());
        Assert.Equal(Raw, payload.GetProperty("bindings")[1].GetProperty("futureMetadata").GetRawText());
    }

    private static ActivityNode Owner(bool withScope)
    {
        var xml = "<b:definitions xmlns:b=\"http://www.omg.org/spec/BPMN/20100524/MODEL\" id=\"Definitions\" targetNamespace=\"urn:raw\"><b:process id=\"Process\"><b:task id=\"Native\"/>" +
            (withScope ? "<b:subProcess id=\"Scope\"><b:task id=\"Nested\"/></b:subProcess>" : "") + "</b:process></b:definitions>";
        var activities = withScope ? Native("native") + "," + Native("nested") : Native("native");
        var bindings = $$$"""{"elementId":"Native","childNodeId":"native","futureMetadata":{{{Raw}}}}""" +
            (withScope ? $$$""",{"elementId":"Nested","childNodeId":"nested","futureMetadata":{{{Raw}}}}""" : "");
        var headers = withScope ? $$$""", "scopeHeaders":[{"scopeElementId":"Scope","nodeId":"scope","activityVersionId":"bpmn-version","inputs":[{{{Input()}}}],"outputs":[],"variables":[{{{Variable("scope-local")}}}],"futureMetadata":{{{Raw}}}}]""" : "";
        var payload = Parse($$$"""
            {"documentXml":{{{JsonSerializer.Serialize(xml, Json)}}},"processId":"Process","activities":[{{{activities}}}],"bindings":[{{{bindings}}}],"variables":[{{{Variable("process-local")}}}],"futureMetadata":{{{Raw}}}{{{headers}}}}
            """);
        return new("owner", "bpmn-version", [], [], new(BpmnActivityIdentities.StructureKind, BpmnAuthoredDocument.SchemaVersion, payload));
    }

    private static string Native(string id) => $$$$"""
        {"nodeId":"{{{{id}}}}","activityVersionId":"native-version","inputs":[{{{{Input()}}}}],"outputs":[],"structure":{"kind":{{{{JsonSerializer.Serialize(NativeSequence.StructureKind, Json)}}}},"schemaVersion":{{{{JsonSerializer.Serialize(NativeSequence.StructureSchemaVersion, Json)}}}},"payload":{"activities":[{"nodeId":"{{{{id}}}}-leaf","activityVersionId":"leaf-version","inputs":[{{{{Input()}}}}],"outputs":[]}],"variables":[{{{{Variable("native-local")}}}}]}}}
        """;

    private static string Input() => $$$"""{"referenceKey":"payload","value":{"expressionType":"Literal","value":{{{Raw}}}}}""";
    private static string Variable(string key) => $$$"""{"referenceKey":"{{{key}}}","name":"{{{key}}}","type":{"alias":"Object","collectionKind":"single"},"storageDriverType":null,"default":{"expressionType":"Literal","value":{{{Raw}}}}}""";
    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
