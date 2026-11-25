using System.Text.Json;
using Elsa.Activities.ControlFlow.Loops;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Elsa.Workflows.Design.Validations.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using AnalyzeEndpoint = Elsa.Workflows.Design.Api.Endpoints.Authoring.AnalyzeScopedVariables.Endpoint;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// The scoped-variable analysis resolves iteration scopes, which read a loop's inputs. A posted draft whose node has no
/// <c>inputs</c> or <c>outputs</c> list is refused as a bad request naming the node and the list, before any scope is
/// resolved.
/// </summary>
public sealed class ScopedVariableAnalysisArgumentListTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structures;
    private readonly StubActivityCatalog _catalog = new();

    public ScopedVariableAnalysisArgumentListTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _structures = _provider.CreateScope().ServiceProvider.GetRequiredService<IActivityStructureService>();
        _catalog.Add("av-script", designFacets:
            [new ActivityDesignFacet("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new { ports = Array.Empty<object>(), scriptInput = "script" }))]);
    }

    public void Dispose() => _provider.Dispose();

    // The script in the loop body makes the analysis resolve iteration scopes, which read the loop's inputs.
    [Fact]
    public async Task A_loop_without_an_inputs_list_is_refused_at_the_loop()
    {
        var script = new ActivityNode("script", "av-script",
            [new ArgumentState("script", ArgumentValue.FromClr("variables.count = 1;", "Literal"), null, null, null, null)], []);
        var forEach = new ActivityNode("each", "for-each", null!, [], Loop(script));

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => AnalyzeAsync(forEach, "script"));

        Assert.StartsWith("each/inputs", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Graph/MalformedArgumentList", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_nested_node_with_a_null_outputs_list_is_refused_at_that_node()
    {
        var forEach = new ActivityNode("each", "for-each", [], [], Loop(new ActivityNode("body", "test", [], null!)));

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => AnalyzeAsync(forEach, null));

        Assert.StartsWith("body/outputs", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Graph/MalformedArgumentList", refused.Message, StringComparison.Ordinal);
    }

    private static ActivityNodeStructure Loop(ActivityNode body) =>
        new(Elsa.Activities.ForEach.Activities.ForEach.StructureKind, "1.0.0",
            JsonSerializer.SerializeToElement(new LoopAuthoredStructure(body), new JsonSerializerOptions(JsonSerializerDefaults.Web)));

    private async Task<ScopedVariableAnalysisResponse> AnalyzeAsync(ActivityNode root, string? nodeId)
    {
        var resolver = new ScopedVariableResolver(_structures);
        var analyzer = new ScriptBodyWriteAnalyzer(new ScriptVariableReadResolver(_provider.GetServices<IScriptVariableReadAnalyzer>()), _structures);
        var endpoint = new AnalyzeEndpoint(new ScopedVariableAuthoringContract(resolver, new ScopedVariablePicker(resolver)), _structures, analyzer, _catalog);
        var state = new WorkflowDefinitionState([], root, [], [], null);
        return await endpoint.HandleAsync(new AnalyzeScopedVariablesRequest(state.ToStateView(), nodeId), CancellationToken.None);
    }
}
