using System.Text.Json;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.Services;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 236 US4 (FR-016 to FR-018, SC-004, SC-006 at compile level): the collection import converts every selected item
/// through the same converter as single-definition import, lowering value flow and reporting warnings, and a reusable
/// workflow becomes a schema-2 graph activity whose outputs reach its direct-start wrapper.
/// </summary>
public sealed class Elsa3CollectionLoweringTests : IAsyncDisposable
{
    private readonly RealActivityCatalog _catalog = RealActivityCatalog.Create();
    private readonly ReusableActivityImportFixtures.CapturingCommand _command = new();
    private readonly ServiceProvider _services;

    public Elsa3CollectionLoweringTests() =>
        _services = ReusableActivityImportFixtures.ImportServices(_catalog, _command);

    public static TheoryData<string> ValueFlowCases() => ["literal", "variable", "result", "loop", "javascript"];

    [Theory]
    [MemberData(nameof(ValueFlowCases))]
    public async Task Collection_apply_stores_the_bindings_single_definition_import_stores(string kind)
    {
        var definition = Plain(kind, ValueFlowRoot(kind));

        var single = await ConversionServices.ConvertAsync(_catalog, definition);
        await ApplyAsync(definition);

        Assert.True(single.Succeeded, Describe(single.Diagnostics));
        var stored = Assert.Single(_command.Mutation!.Workflows).Version.State;
        Assert.Equal(JsonSerializer.Serialize(single.Value!.State.RootActivity), JsonSerializer.Serialize(stored.RootActivity));
    }

    [Fact]
    public async Task A_dangling_reference_refuses_the_apply_and_commits_nothing()
    {
        var definition = Plain("dangling", Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Captured("nowhere")))));

        var refusal = await Assert.ThrowsAsync<ReusableActivityImportValidationException>(() => ApplyAsync(definition));

        Assert.Contains(refusal.Diagnostics, diagnostic => diagnostic.Code == Elsa3ImportDiagnosticCodes.DanglingReference);
        Assert.Null(_command.Mutation);
    }

    [Fact]
    public async Task The_callers_collection_is_not_modified()
    {
        var definition = Plain("unchanged", ValueFlowRoot("loop"));
        var before = JsonSerializer.Serialize(definition);

        await ApplyAsync(definition);

        Assert.Equal(before, JsonSerializer.Serialize(definition));
    }

    [Fact]
    public async Task Conversion_warnings_reach_the_apply_result()
    {
        var definition = Plain("warned", Sequence("root", Activity("send", "Elsa.FlowSendHttpRequest", ("url", Literal("https://example.org")))));

        var result = await ApplyAsync(definition);

        Assert.Contains(result.Warnings!, warning => warning.Code == Elsa3ActivityDiagnosticCodes.HttpOutcomeSingle);
    }

    [Fact]
    public async Task A_reusable_workflow_with_an_output_is_a_schema_two_activity_whose_wrapper_exposes_the_output()
    {
        var reusable = ReusableActivityImportFixtures.Workflow(
            "child", "child-v1", 1, reusable: true,
            Sequence("root", Activity("set", "Elsa.SetOutput", ("outputName", Literal("CustomOutput")), ("outputValue", Literal("42")))));
        reusable.Outputs = [new Elsa3WorkflowArgumentDefinition { Name = "CustomOutput", Type = "String" }];

        await ApplyAsync(reusable);

        var mutation = _command.Mutation!;
        var activity = Assert.Single(mutation.Activities);
        Assert.Equal("2", activity.Version.ProviderSchemaVersion);
        var mapping = Assert.Single(activity.Version.DescriptorPayload.GetProperty("outputMappings").EnumerateArray());
        Assert.Equal("CustomOutput", mapping.GetProperty("outputReferenceKey").GetString());
        Assert.Equal("done", Assert.Single(activity.Version.DescriptorPayload.GetProperty("outcomeMappings").EnumerateArray()).GetProperty("boundaryOutcomeReferenceKey").GetString());

        var wrapper = Assert.Single(mutation.Workflows).Version.State;
        Assert.Equal(_catalog.VersionIdOf("Elsa.Activities.Sequence.Activities.Sequence"), wrapper.RootActivity!.ActivityVersionId);
        var children = wrapper.RootActivity.Structure!.Payload.GetProperty("activities").EnumerateArray().ToArray();
        Assert.Equal(activity.Version.Id, children[0].GetProperty("activityVersionId").GetString());
        Assert.Equal("elsa.intrinsic.set-output@1", children[1].GetProperty("activityVersionId").GetString());
        Assert.Equal("CustomOutput", Assert.Single(wrapper.Outputs).ReferenceKey);
    }

    [Fact]
    public async Task A_reference_node_binds_through_the_replacement_without_a_catalog_lookup()
    {
        var child = ReusableActivityImportFixtures.Workflow(
            "child", "child-v1", 1, reusable: true,
            Sequence("child-root", Activity("write", "Elsa.WriteLine", ("text", Literal("x")))),
            new Elsa3WorkflowArgumentDefinition { Name = "Message", Type = "String" });
        var reference = ReusableActivityImportFixtures.Reference("call", targetVersionId: "child-v1");
        reference.AdditionalProperties["message"] = Expression("JavaScript", "getVariable('greeting')");
        var parent = Plain("parent", Sequence("root", reference));
        parent.Variables = [new Elsa3Variable { Id = "greeting", Name = "greeting", TypeName = "String" }];

        await ApplyAsync(child, parent);

        var parentState = _command.Mutation!.Workflows.Single(workflow => workflow.SourceDefinitionId == "parent").Version.State;
        var call = Node(parentState, "call");
        var message = Input(call, "Message");
        Assert.Equal("JavaScript", message.Value.ExpressionType);
        Assert.Contains("args.greeting", Source(message), StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync() => await _services.DisposeAsync();

    private async Task<ReusableActivityImportApplyResult> ApplyAsync(params Elsa3WorkflowDefinition[] definitions)
    {
        await using var scope = _services.CreateAsyncScope();
        var importer = scope.ServiceProvider.GetRequiredService<IReusableActivityCollectionImporter>();
        var collection = ReusableActivityImportFixtures.Collection(definitions);
        var plan = await importer.AnalyzeAsync(collection);
        return await importer.ApplyAsync(new(plan.PlanId, collection, definitions.Select(definition => definition.Id).ToArray()));
    }

    private static Elsa3WorkflowDefinition Plain(string id, Elsa3Activity root)
    {
        var definition = ReusableActivityImportFixtures.Workflow(id, $"{id}-v1", 1, reusable: false, root);
        definition.Variables = [new Elsa3Variable { Id = "total", Name = "total", TypeName = "String" }];
        return definition;
    }

    private static Elsa3Activity ValueFlowRoot(string kind) => kind switch
    {
        "literal" => Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Literal("hello")))),
        "variable" => Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Captured("total")))),
        "result" => Sequence("root",
            Activity("read", "Elsa.ReadLine", ("result", Captured("line"))),
            Activity("write", "Elsa.WriteLine", ("text", Captured("line")))),
        "loop" => LoopRoot(),
        "javascript" => Sequence("root", Activity("write", "Elsa.WriteLine", ("text", Expression("JavaScript", "getVariable('total') + '!'")))),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    private static Elsa3Activity LoopRoot()
    {
        var loop = Activity("loop", "Elsa.ForEach", ("items", Literal("[1]", "Object")), ("currentValue", Captured("item-slot")));
        loop.AdditionalProperties["body"] = Port(Activity("inside", "Elsa.WriteLine", ("text", Captured("item-slot"))));
        return Sequence("root", loop);
    }
}
