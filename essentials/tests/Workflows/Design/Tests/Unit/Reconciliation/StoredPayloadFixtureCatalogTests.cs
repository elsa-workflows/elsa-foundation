using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Reconciliation.Models;
using Elsa.Workflows.Design.Tests.Unit.Reconciliation.Git;
using Elsa.Workflows.Design.Tests.Unit.Reconciliation.Json;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation;

/// <summary>
/// FR-024a's typed catalog of the current file fixtures produced for IPayloadSerializer readers.
/// Git version files contain WorkflowDefinitionState; workflow catalog files contain reconciliation models.
/// The activity catalog's literal fixture is separately read by the real payload serializer in
/// JsonActivityCatalogReaderTests.Read_DeserializesModels_WithUiMetadataAndDescriptorElement.
/// </summary>
/// <remarks>
/// This is a catalog of payload-reader fixtures, not a rule for every JSON file in the repository.
/// LegacyWorkflowHashFixture and the current ETL/BPMN captures use their owner codecs and retain their
/// concrete byte/hash/replay tests. Their archived originals are precise-refusal evidence. Elsa 3 import
/// documents, database upcaster row dictionaries and opaque carried values are not camelCase payload contracts.
/// </remarks>
public sealed class StoredPayloadFixtureCatalogTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Authored_input_and_output_metadata_keep_raw_JSON_with_the_callers_profile(bool payloadProfile, bool nullDefault)
    {
        const string raw = """{ "OrderID" : 9007199254740993, "amount" : 1.00, "nil" : null, "text" : "\u0061 <+>", "isNullable" : true }""";
        using var metadata = JsonDocument.Parse(raw);
        using var defaultValue = JsonDocument.Parse(nullDefault ? "null" : raw);
        var input = new InputDefinition("input", "InputName", new TypeReference("Any"), null, "Input", null, true,
            PropertyInfo: metadata.RootElement, UISpecifications: metadata.RootElement, IsRequired: true,
            DefaultValue: defaultValue.RootElement, DefaultSyntax: "Literal");
        var missingDefault = new InputDefinition("missing", "MissingDefault", new TypeReference("Any"), null, "Missing", null, true);
        var output = new OutputDefinition("output", "OutputName", new TypeReference("Any"), null, "Output", null, true,
            PropertyInfo: metadata.RootElement, UISpecifications: metadata.RootElement, IsRequired: true);
        var state = WorkflowDefinitionState.Empty with { Inputs = [input, missingDefault], Outputs = [output] };
        var payload = SerializationServices.PayloadSerializer();
        var json = payloadProfile ? payload.Serialize(state) : JsonSerializer.Serialize(state, JsonBaseline.Options);
        var restored = payloadProfile ? payload.Deserialize<WorkflowDefinitionState>(json)
            : JsonSerializer.Deserialize<WorkflowDefinitionState>(json, JsonBaseline.Options)!;

        using var written = JsonDocument.Parse(json);
        var writtenInput = written.RootElement.GetProperty("inputs")[0];
        var writtenOutput = written.RootElement.GetProperty("outputs")[0];
        foreach (var member in new[] { writtenInput, writtenOutput })
        {
            Assert.False(member.TryGetProperty("isNullable", out _));
            Assert.True(member.GetProperty("isRequired").GetBoolean());
            Assert.Equal(raw, member.GetProperty("propertyInfo").GetRawText());
            Assert.Equal(raw, member.GetProperty("uiSpecifications").GetRawText());
        }
        Assert.Equal(defaultValue.RootElement.GetRawText(), writtenInput.GetProperty("defaultValue").GetRawText());
        Assert.False(written.RootElement.GetProperty("inputs")[1].TryGetProperty("defaultValue", out _));

        var readInput = Assert.Single(restored.Inputs, member => member.ReferenceKey == "input");
        var readOutput = Assert.Single(restored.Outputs);
        Assert.False(readInput.IsNullable); Assert.False(readOutput.IsNullable);
        Assert.True(readInput.IsRequired); Assert.True(readOutput.IsRequired);
        Assert.Equal(raw, readInput.PropertyInfo!.Value.GetRawText());
        Assert.Equal(raw, readInput.UISpecifications!.Value.GetRawText());
        Assert.Equal(raw, readOutput.PropertyInfo!.Value.GetRawText());
        Assert.Equal(raw, readOutput.UISpecifications!.Value.GetRawText());
        Assert.True(readInput.DefaultValue.HasValue); // A Literal JSON null remains a present default.
        Assert.Equal(defaultValue.RootElement.GetRawText(), readInput.DefaultValue.Value.GetRawText());
        Assert.Null(Assert.Single(restored.Inputs, member => member.ReferenceKey == "missing").DefaultValue);
    }

    private sealed record Fixture(string Source, Type Contract, Func<string> Json);

    // These are the actual fixture producers consumed by the composed file/Git tests, rather
    // than copies of their documents or an independent serialization profile.
    private static readonly IReadOnlyDictionary<string, Fixture> Fixtures = new Dictionary<string, Fixture>(StringComparer.Ordinal)
    {
        ["git-empty-seed"] = new("GitTestFixtures.cs#GitTestSupport.EmptyStateJson", typeof(WorkflowDefinitionState), () => GitTestSupport.EmptyStateJson),
        ["git-authored-state"] = new("GitTestFixtures.cs#GitTestSupport.AuthoredStateJson", typeof(WorkflowDefinitionState), () => GitTestSupport.AuthoredStateJson),
        ["workflow-catalog"] = new("JsonWorkflowReconciliationSourceTests.cs#CurrentCatalogFixtureJson", typeof(WorkflowVersionReconciliationModel[]), () => JsonWorkflowReconciliationSourceTests.CurrentCatalogFixtureJson)
    };

    [Theory]
    [InlineData("git-empty-seed")]
    [InlineData("git-authored-state")]
    [InlineData("workflow-catalog")]
    public void Current_payload_file_fixtures_keep_all_their_members_through_exact_reads(string name)
    {
        var fixture = Fixtures[name];
        Verify(fixture, fixture.Json());
    }

    [Fact]
    public void The_fixture_guard_refuses_a_wrongly_cased_owned_member()
    {
        var fixture = Fixtures["workflow-catalog"];
        var wrong = fixture.Json().Replace("\"name\":\"Catalog sentinel\"", "\"Name\":\"Catalog sentinel\"", StringComparison.Ordinal);
        Assert.NotEqual(fixture.Json(), wrong);
        var error = Assert.Throws<InvalidDataException>(() => Verify(fixture, wrong));
        Assert.Contains("FR-024a", error.Message, StringComparison.Ordinal);
    }

    private static void Verify(Fixture fixture, string json)
    {
        IPayloadSerializer serializer = SerializationServices.PayloadSerializer();
        using var before = JsonDocument.Parse(json);
        var state = fixture.Contract == typeof(WorkflowDefinitionState)
            ? before.RootElement
            : before.RootElement[0].GetProperty("state");
        foreach (var member in new[] { "variables", "rootActivity", "inputs", "outputs", "strategyOptions" })
            Require(state.TryGetProperty(member, out _), fixture, "$", "missing current owned member '" + member + "'");
        if (fixture.Contract != typeof(WorkflowDefinitionState))
            Require(before.RootElement[0].TryGetProperty("name", out _), fixture, "$[0]", "missing current owned member 'name'");

        var restored = serializer.Deserialize(json, fixture.Contract);
        using var after = JsonDocument.Parse(serializer.Serialize(restored));
        KeepFields(before.RootElement, after.RootElement, fixture, "$");
        if (state.GetProperty("variables").GetArrayLength() > 0)
        {
            var restoredState = fixture.Contract == typeof(WorkflowDefinitionState)
                ? (WorkflowDefinitionState)restored
                : ((WorkflowVersionReconciliationModel[])restored)[0].State;
            var value = Assert.Single(restoredState.Variables).Default!.Value!.Value;
            Assert.Equal(GitTestSupport.AuthoredRawFixtureValue, value.GetRawText());
        }
    }

    // Compare names to what the real reader/writer retains, not to a recursive lowercase
    // convention. Uppercase dictionary/data keys therefore stay legal and must survive.
    // Newly emitted optional metadata may be present after the read; no input field may vanish.
    private static void KeepFields(JsonElement before, JsonElement after, Fixture fixture, string path)
    {
        Require(before.ValueKind == after.ValueKind, fixture, path, "changed JSON kind");
        switch (before.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in before.EnumerateObject())
                {
                    Require(after.TryGetProperty(property.Name, out var value), fixture, path, "lost member '" + property.Name + "'");
                    KeepFields(property.Value, value, fixture, path + "." + property.Name);
                }
                break;
            case JsonValueKind.Array:
                Require(before.GetArrayLength() == after.GetArrayLength(), fixture, path, "changed item count");
                for (var index = 0; index < before.GetArrayLength(); index++)
                    KeepFields(before[index], after[index], fixture, path + "[" + index + "]");
                break;
            default:
                Require(JsonElement.DeepEquals(before, after), fixture, path, "changed a stored value");
                break;
        }
    }

    private static void Require(bool condition, Fixture fixture, string path, string reason)
    {
        if (!condition) throw new InvalidDataException($"FR-024a: {fixture.Source} {reason} at {path}.");
    }
}
