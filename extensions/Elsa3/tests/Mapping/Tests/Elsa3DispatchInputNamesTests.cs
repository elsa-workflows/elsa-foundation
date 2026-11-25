using System.Text.Json;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// #5808: Elsa 3 DispatchWorkflow/ExecuteWorkflow passed any input name to the child and matched names in any letter case,
/// so a name the child did not declare was ignored. Elsa 4 accepts only the names the child declares, spelled as declared.
/// A literal map to a child in the same import is spelled as declared or refused; any other map is warned.
/// </summary>
public sealed class Elsa3DispatchInputNamesTests
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Targets =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["acme-child"] = ["OrderId", "Region"] };

    [Theory]
    [InlineData("Elsa.DispatchWorkflow")]
    [InlineData("Elsa.ExecuteWorkflow")]
    public async Task A_literal_map_of_declared_names_converts_unchanged(string type)
    {
        var result = await ConvertAsync(Dispatch(type, Literal("""{"OrderId":1,"Region":"north"}""", "ObjectDictionary")));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("""{"OrderId":1,"Region":"north"}""", InputsText(result));
        Assert.DoesNotContain(result.Diagnostics, IsDispatchDiagnostic);
    }

    [Theory]
    [InlineData("Elsa.DispatchWorkflow")]
    [InlineData("Elsa.ExecuteWorkflow")]
    public async Task A_key_in_another_letter_case_is_spelled_as_the_child_declares_it(string type)
    {
        var result = await ConvertAsync(Dispatch(type, Literal("""{"orderid":1,"Region":"north"}""", "ObjectDictionary")));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("""{"OrderId":1,"Region":"north"}""", InputsText(result));
        var info = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Info, info.Severity);
        Assert.Contains("'orderid'", info.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_static_Object_expression_map_is_spelled_as_declared_and_stays_an_Object_expression()
    {
        var result = await ConvertAsync(Dispatch("Elsa.DispatchWorkflow", Expression("Object", """{"region":"north"}""", "Object")));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Contains("\"Region\"", InputsText(result), StringComparison.Ordinal);
        Assert.DoesNotContain("\"region\"", InputsText(result), StringComparison.Ordinal);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DispatchInputNames);
    }

    [Theory]
    [InlineData("Elsa.DispatchWorkflow")]
    [InlineData("Elsa.ExecuteWorkflow")]
    public async Task An_undeclared_key_is_refused_with_the_declared_names(string type)
    {
        var result = await ConvertAsync(Dispatch(type, Literal("""{"OrderId":1,"Debug":true}""", "ObjectDictionary")));

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DispatchInputUndeclared);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Error, error.Severity);
        Assert.Equal("dispatch", error.Metadata["ActivityNodeId"]);
        Assert.Contains("'Debug'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'OrderId', 'Region'", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Every_undeclared_key_is_reported()
    {
        var result = await ConvertAsync(Dispatch("Elsa.DispatchWorkflow", Literal("""{"Debug":true,"Trace":1}""", "ObjectDictionary")));

        Assert.False(result.Succeeded);
        Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DispatchInputUndeclared));
    }

    [Fact]
    public async Task Two_keys_that_name_one_input_are_refused()
    {
        // Elsa 3 copied the map into a case-insensitive dictionary, which faulted on these two keys.
        var result = await ConvertAsync(Dispatch("Elsa.DispatchWorkflow", Literal("""{"OrderId":1,"orderId":2}""", "ObjectDictionary")));

        Assert.False(result.Succeeded);
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous);
    }

    [Fact]
    public async Task A_child_that_declares_no_inputs_refuses_every_key()
    {
        var targets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal) { ["acme-child"] = [] };

        var result = await ConvertAsync(Dispatch("Elsa.DispatchWorkflow", Literal("""{"OrderId":1}""", "ObjectDictionary")), targets);

        Assert.False(result.Succeeded);
        var error = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DispatchInputUndeclared);
        Assert.DoesNotContain("It declares", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_literal_map_to_a_child_outside_the_import_is_not_checked()
    {
        var result = await ConvertAsync(Dispatch("Elsa.DispatchWorkflow", Literal("""{"debug":true}""", "ObjectDictionary")),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Equal("""{"debug":true}""", InputsText(result));
        Assert.DoesNotContain(result.Diagnostics, IsDispatchDiagnostic);
    }

    [Theory]
    [InlineData("Elsa.DispatchWorkflow")]
    [InlineData("Elsa.ExecuteWorkflow")]
    public async Task A_script_map_converts_with_a_warning_naming_the_declared_inputs(string type)
    {
        var result = await ConvertAsync(Dispatch(type, Expression("JavaScript", "return { orderId: 1 };", "ObjectDictionary")));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        var warning = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DispatchInputNames);
        Assert.Equal(Elsa3MigrationDiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("'OrderId', 'Region'", warning.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_script_map_to_a_child_outside_the_import_still_warns()
    {
        var result = await ConvertAsync(Dispatch("Elsa.DispatchWorkflow", Expression("JavaScript", "return { OrderId: 1 };", "ObjectDictionary")),
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal));

        Assert.True(result.Succeeded, Describe(result.Diagnostics));
        Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DispatchInputNames);
    }

    [Fact]
    public async Task An_unbound_or_empty_map_raises_nothing()
    {
        var unbound = await ConvertAsync(Sequence("root", Activity("dispatch", "Elsa.DispatchWorkflow", ("workflowDefinitionId", Literal("acme-child")))));
        var empty = await ConvertAsync(Dispatch("Elsa.DispatchWorkflow", Literal("{}", "ObjectDictionary")));

        Assert.True(unbound.Succeeded, Describe(unbound.Diagnostics));
        Assert.True(empty.Succeeded, Describe(empty.Diagnostics));
        Assert.DoesNotContain(unbound.Diagnostics.Concat(empty.Diagnostics), IsDispatchDiagnostic);
    }

    [Fact]
    public async Task A_collection_import_checks_the_map_against_the_newest_selected_child_version()
    {
        // Version 1 declares OrderId; version 2, the one the dispatch pins once both publish, declares only Region.
        var child1 = ReusableActivityImportFixtures.Workflow("acme-child", "acme-child-v1", 1, false, ReusableActivityImportFixtures.Leaf("c1"), Argument("OrderId"));
        var child2 = ReusableActivityImportFixtures.Workflow("acme-child", "acme-child-v2", 2, false, ReusableActivityImportFixtures.Leaf("c2"), Argument("Region"));
        var caller = ReusableActivityImportFixtures.Workflow("acme-caller", "acme-caller-v1", 1, false,
            Dispatch("Elsa.DispatchWorkflow", Literal("""{"OrderId":1}""", "ObjectDictionary")));

        var refusal = await Assert.ThrowsAsync<ReusableActivityImportValidationException>(() => ApplyAsync(child1, child2, caller));

        var error = Assert.Single(refusal.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.DispatchInputUndeclared);
        Assert.Contains("'Region'", error.Guidance, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_collection_import_spells_the_key_as_the_child_declares_it()
    {
        var child = ReusableActivityImportFixtures.Workflow("acme-child", "acme-child-v1", 1, false, ReusableActivityImportFixtures.Leaf("c1"), Argument("OrderId"));
        var caller = ReusableActivityImportFixtures.Workflow("acme-caller", "acme-caller-v1", 1, false,
            Dispatch("Elsa.DispatchWorkflow", Literal("""{"orderId":1}""", "ObjectDictionary")));
        var command = new ReusableActivityImportFixtures.CapturingCommand();

        await ApplyAsync(command, child, caller);

        var stored = command.Mutation!.Workflows.Single(workflow => workflow.Version.Id == "acme-caller-v1").Version.State;
        Assert.Equal("""{"OrderId":1}""", Text(Input(Node(stored, "dispatch"), "Inputs")));
    }

    private static Elsa3Activity Dispatch(string type, JsonElement input) =>
        Sequence("root", Activity("dispatch", type, ("workflowDefinitionId", Literal("acme-child")), ("input", input)));

    private static Elsa3WorkflowArgumentDefinition Argument(string name) => new() { Name = name, Type = "Object" };

    private static bool IsDispatchDiagnostic(Elsa3MigrationDiagnostic diagnostic) =>
        diagnostic.Code is Elsa3ActivityDiagnosticCodes.DispatchInputNames or Elsa3ActivityDiagnosticCodes.DispatchInputUndeclared or
            Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten or Elsa3ActivityDiagnosticCodes.LiteralKeyAmbiguous;

    private static string? InputsText(Elsa3MigrationResult<Elsa3.Mapping.Conversion.Elsa3ConvertedDefinition> result)
    {
        var argument = Input(Node(result.Value!.State, "dispatch"), "Inputs");
        return argument.Value.Value is JsonElement element && element.TryGetProperty("source", out var source)
            ? source.GetString()
            : Text(argument);
    }

    private static Task<Elsa3MigrationResult<Elsa3.Mapping.Conversion.Elsa3ConvertedDefinition>> ConvertAsync(
        Elsa3Activity root, IReadOnlyDictionary<string, IReadOnlyList<string>>? targets = null) =>
        ConversionServices.ConvertAsync(RealActivityCatalog.Create(), ConversionServices.Definition(root), dispatchTargetInputs: targets ?? Targets);

    private static Task<ReusableActivityImportApplyResult> ApplyAsync(params Elsa3WorkflowDefinition[] definitions) =>
        ApplyAsync(new ReusableActivityImportFixtures.CapturingCommand(), definitions);

    private static async Task<ReusableActivityImportApplyResult> ApplyAsync(
        ReusableActivityImportFixtures.CapturingCommand command, params Elsa3WorkflowDefinition[] definitions)
    {
        await using var services = ReusableActivityImportFixtures.ImportServices(RealActivityCatalog.Create(), command);
        await using var scope = services.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<IReusableActivityCollectionImporter>();
        var collection = ReusableActivityImportFixtures.Collection(definitions);
        var plan = await importer.AnalyzeAsync(collection);
        return await importer.ApplyAsync(new(plan.PlanId, collection, definitions.Select(definition => definition.Id).ToArray()));
    }
}
