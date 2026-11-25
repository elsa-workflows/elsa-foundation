using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Primitives.Models;
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
/// Spec 1231 FR-036 (T024): the scoped-variable analysis of a selected script activity carries the variables its body
/// may write, resolved as publication pins them, so Studio shows the node's may-write set. A node without a literal
/// script body gets none.
/// </summary>
public sealed class ScriptBodyWriteAnalysisEndpointTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structures;
    private readonly StubActivityCatalog _catalog = new();

    public ScriptBodyWriteAnalysisEndpointTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _structures = _provider.CreateScope().ServiceProvider.GetRequiredService<IActivityStructureService>();
        _catalog.Add("av-script", designFacets:
            [new ActivityDesignFacet("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new { ports = Array.Empty<object>(), scriptInput = "script" }))]);
        _catalog.Add("av-other");
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task A_script_node_carries_the_variables_its_body_may_write()
    {
        var response = await AnalyzeAsync("av-script", "variables.order = { total: 1 }; return variables.count + 1;");

        var writes = Assert.IsType<ScriptBodyWriteSet>(response.ScriptWrites);
        var target = Assert.Single(writes.Variables);
        Assert.Equal(("order", VariableReference.WorkflowScopeId, "order-key"), (target.Name, target.ScopeId, target.ReferenceKey));
        Assert.False(writes.MayWriteEveryVisibleVariable);
    }

    [Fact]
    public async Task A_script_node_with_a_computed_write_may_write_every_visible_variable()
    {
        var response = await AnalyzeAsync("av-script", "const k = 'order'; variables[k] = 1;");

        Assert.True(response.ScriptWrites!.MayWriteEveryVisibleVariable);
    }

    [Fact]
    public async Task A_node_without_a_script_body_carries_no_script_writes()
    {
        Assert.Null((await AnalyzeAsync("av-other", "variables.order = 1;")).ScriptWrites);
    }

    private async Task<ScopedVariableAnalysisResponse> AnalyzeAsync(string versionId, string script)
    {
        var state = new WorkflowDefinitionState(
            [new VariableDefinition("order-key", "order", new TypeReference("Any"), null, null), new VariableDefinition("count-key", "count", new TypeReference("Int32"), null, null)],
            new ActivityNode("script", versionId, [new ArgumentState("script", ArgumentValue.FromClr(script, "Literal"), null, null, null, null)], []),
            [],
            [],
            null);
        var resolver = new ScopedVariableResolver(_structures);
        var analyzer = new ScriptBodyWriteAnalyzer(new ScriptVariableReadResolver(_provider.GetServices<IScriptVariableReadAnalyzer>()), _structures);
        var endpoint = new AnalyzeEndpoint(new ScopedVariableAuthoringContract(resolver, new ScopedVariablePicker(resolver)), _structures, analyzer, _catalog);
        return await endpoint.HandleAsync(new AnalyzeScopedVariablesRequest(state.ToStateView(), "script"), CancellationToken.None);
    }
}
