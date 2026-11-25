using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Api.Projections;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;
using Xunit;
using RenamePlanEndpoint = Elsa.Workflows.Design.Api.Endpoints.Authoring.PlanVariableRename.Endpoint;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>
/// Spec 1222 FR-022 (T036): the rename-plan endpoint refuses an incomplete request, passes the draft to the planner, and
/// names a script activity's script input from the catalog's <c>elsa.outcomes</c> facet so its body is planned too.
/// The flag reason crosses the wire as a name, not a number.
/// </summary>
public sealed class PlanVariableRenameEndpointTests
{
    private static readonly WorkflowDefinitionState State = new(
        [new VariableDefinition("order-key", "order", new TypeReference("Any"), null, null)],
        new ActivityNode("script", "av-script", [new ArgumentState("script", ArgumentValue.FromClr("return variables.order;", "Literal"), null, null, null, null)], []),
        [],
        [],
        null);

    [Theory]
    [InlineData(null, "order-key", "purchase")]
    [InlineData("workflow", " ", "purchase")]
    [InlineData("workflow", "order-key", "")]
    public async Task An_incomplete_request_is_refused(string? scopeId, string? key, string? newName)
    {
        var endpoint = new RenamePlanEndpoint(new RecordingPlanner());

        await Assert.ThrowsAsync<ArgumentException>(() =>
            endpoint.HandleAsync(new PlanVariableRenameRequest(State.ToStateView(), scopeId, key, newName), CancellationToken.None));
    }

    [Fact]
    public async Task The_script_input_comes_from_the_catalog_facet()
    {
        var catalog = new StubActivityCatalog().Add("av-script", designFacets:
            [new ActivityDesignFacet("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new { ports = Array.Empty<object>(), scriptInput = "script" }))]);
        var planner = new RecordingPlanner();

        await new RenamePlanEndpoint(planner, catalog).HandleAsync(
            new PlanVariableRenameRequest(State.ToStateView(), VariableReference.WorkflowScopeId, "order-key", "purchase"),
            CancellationToken.None);

        Assert.Equal((VariableReference.WorkflowScopeId, "order-key", "purchase"), planner.Request);
        Assert.Equal("script", planner.ScriptInput);
    }

    [Fact]
    public void The_flag_reason_serializes_as_its_name()
    {
        var json = JsonSerializer.Serialize(new ScriptVariableRenameFlag("a", "Text", ScriptVariableRenameFlagReason.ComputedRead, "m"), new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"reason\":\"ComputedRead\"", json, StringComparison.Ordinal);
    }

    private sealed class RecordingPlanner : IScriptVariableRenamePlanner
    {
        public (string ScopeId, string Key, string NewName)? Request { get; private set; }
        public string? ScriptInput { get; private set; }

        public async ValueTask<ScriptVariableRenamePlan> PlanAsync(
            WorkflowDefinitionState state,
            string scopeId,
            string referenceKey,
            string newName,
            Func<ActivityNode, CancellationToken, ValueTask<string?>>? scriptInputKeyOf = null,
            CancellationToken cancellationToken = default)
        {
            Request = (scopeId, referenceKey, newName);
            ScriptInput = scriptInputKeyOf is null ? null : await scriptInputKeyOf(state.RootActivity!, cancellationToken);
            return new ScriptVariableRenamePlan("order", newName, [], []);
        }
    }
}
