using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Services;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// Spec 1222 draft diagnostics (T019, T033): a literal read no visible variable declares is an error
/// (<c>VF-READ-002</c>, FR-012, FR-023); a computed read is reported with the variables it could read
/// (<c>VF-READ-004</c>, FR-009), as a warning. Input expressions and a script activity's script are both checked.
/// </summary>
public sealed class ScriptVariableReadValidatorTests : IDisposable
{
    private const string ScriptVersionId = "av-script";
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structures;
    private readonly ScriptVariableReadResolver _resolver;
    private readonly StubActivityCatalog _catalog = new();

    public ScriptVariableReadValidatorTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        var scope = _provider.CreateScope();
        _structures = scope.ServiceProvider.GetRequiredService<IActivityStructureService>();
        _resolver = new ScriptVariableReadResolver(scope.ServiceProvider.GetServices<IScriptVariableReadAnalyzer>());
        _catalog.Add(ScriptVersionId, designFacets: [new ActivityDesignFacet("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new { ports = Array.Empty<object>(), scriptInput = "script" }))]);
        _catalog.Add("av-write");
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task A_literal_read_of_an_undeclared_variable_is_an_error()
    {
        var error = Assert.Single(await ValidateAsync(new ActivityNode("w", "av-write", [Js("variables.missing + variables.order")], [])));

        Assert.Equal(ScriptVariableReadValidator.UnresolvedReadType, error.Type);
        Assert.Equal(ValidationSeverities.Error, error.Severity);
        Assert.Equal("w/inputs/Text", error.Path);
        Assert.StartsWith(ScriptVariableReadDiagnostics.UnresolvedLiteralRead, error.Message, StringComparison.Ordinal);
        Assert.Contains("'missing'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_computed_read_is_reported_with_its_candidates_as_a_warning()
    {
        var warning = Assert.Single(await ValidateAsync(new ActivityNode("w", "av-write", [Js("getVariable(name)")], [])));

        Assert.Equal(ScriptVariableReadValidator.ComputedReadType, warning.Type);
        Assert.Equal(ValidationSeverities.Warning, warning.Severity);
        Assert.StartsWith(ScriptVariableReadDiagnostics.ComputedReadReported, warning.Message, StringComparison.Ordinal);
        Assert.Contains("'order'", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_script_activity_body_is_checked_too()
    {
        var script = new ArgumentState("script", new ArgumentValue("return variables.nope;", "Literal"), null, null, null, null);

        var findings = await ValidateAsync(new ActivityNode("s", ScriptVersionId, [script], []));

        // The unresolvable name is an error; the body reads variables through its session (spec 1222 FR-033), so a
        // literal read needs no warning of its own.
        var finding = Assert.Single(findings);
        Assert.Equal(ScriptVariableReadValidator.UnresolvedReadType, finding.Type);
        Assert.Equal("s/inputs/script", finding.Path);
    }

    [Fact]
    public async Task A_switch_case_condition_and_its_value_are_each_checked_once()
    {
        // The case condition lives in the Switch structure; Value is consumed by the structure in match-value mode and
        // must be checked there, not a second time among the node's own inputs.
        var switchNode = new ActivityNode("sw", "av-write", [Js("variables.missingValue") with { ReferenceKey = "Value" }], [],
            new ActivityNodeStructure(Elsa.Activities.Switch.Activities.Switch.StructureKind, Elsa.Activities.Switch.Activities.Switch.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new Elsa.Activities.Switch.Models.SwitchAuthoredStructure(
                [
                    new Elsa.Activities.Switch.Models.SwitchAuthoredCase("Big")
                    {
                        CaseId = "big",
                        Condition = new ArgumentState("condition", new ArgumentValue("variables.missingCondition > 1", "JavaScript"), null, null, null, null)
                    },
                    new Elsa.Activities.Switch.Models.SwitchAuthoredCase("Small")
                ]), new JsonSerializerOptions(JsonSerializerDefaults.Web))));

        var findings = await ValidateAsync(switchNode);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal(ScriptVariableReadValidator.UnresolvedReadType, finding.Type));
        var condition = Assert.Single(findings, finding => finding.Message.Contains("'missingCondition'", StringComparison.Ordinal));
        Assert.Equal($"sw/structure/{Elsa.Activities.Switch.Activities.Switch.ConditionInputKey("big")}", condition.Path);
        var value = Assert.Single(findings, finding => finding.Message.Contains("'missingValue'", StringComparison.Ordinal));
        Assert.Equal("sw/structure/Value", value.Path);
    }

    [Fact]
    public async Task A_body_that_writes_an_undeclared_variable_by_name_is_an_error_naming_the_position()
    {
        // Spec 1231 FR-020: refused once, as a write (not again as an unresolved read).
        var findings = await ValidateAsync(new ActivityNode("s", ScriptVersionId, [Body("const a = 1;\nsetVariable('nope', a);")], []));

        var error = Assert.Single(findings);
        Assert.Equal(ScriptVariableReadValidator.RefusedWriteType, error.Type);
        Assert.Equal(ValidationSeverities.Error, error.Severity);
        Assert.StartsWith(ScriptVariableReadDiagnostics.UndeclaredLiteralWrite, error.Message, StringComparison.Ordinal);
        Assert.Contains("variable 'nope' (variables.nope) at line 2, column 1", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("variables.currentItem = 1;")]
    [InlineData("variables.currentItem.total = 1;")]
    [InlineData("setVariable('currentIndex', 2);")]
    public async Task A_body_that_assigns_a_loop_item_or_index_is_an_error(string script)
    {
        // Spec 1231 FR-006, FR-020, T029: a loop's item and index are read-only.
        var loop = new ActivityNode("loop", "av-loop", [], []) { Structure = new ActivityNodeStructure(
            Elsa.Activities.ForEach.Activities.ForEach.StructureKind, Elsa.Activities.ForEach.Activities.ForEach.StructureSchemaVersion, JsonSerializer.SerializeToElement(new { })) };
        var handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), item => item.Kind == Elsa.Activities.ForEach.Activities.ForEach.StructureKind);
        loop = handler.ReplaceChildren(loop, [new ActivityChildProjection(Elsa.Activities.ForEach.Activities.ForEach.BodySlotName,
            [new ActivityNode("s", ScriptVersionId, [Body(script)], [])])]);

        var error = Assert.Single(await ValidateAsync(loop), finding => finding.Type == ScriptVariableReadValidator.RefusedWriteType);

        Assert.StartsWith(ScriptVariableReadDiagnostics.ReadOnlyLiteralWrite, error.Message, StringComparison.Ordinal);
        Assert.Contains("loop item or index", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("variables[k] = 1;")]
    [InlineData("setVariable(k, 1);")]
    [InlineData("const v = variables; v.order = 1;")]
    public async Task A_body_with_a_computed_or_opaque_write_is_a_warning(string script)
    {
        // Spec 1231 FR-020a: it publishes; the node may write every visible variable.
        var warning = Assert.Single(await ValidateAsync(new ActivityNode("s", ScriptVersionId, [Body("const k = 'order';\n" + script)], [])),
            finding => finding.Type == ScriptVariableReadValidator.ComputedWriteType);

        Assert.Equal(ValidationSeverities.Warning, warning.Severity);
        Assert.StartsWith(ScriptVariableReadDiagnostics.ComputedWriteReported, warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_that_writes_a_declared_variable_raises_nothing()
    {
        Assert.Empty(await ValidateAsync(new ActivityNode("s", ScriptVersionId, [Body("variables.order = { total: 1 }; setVariable('order', 2); variables.order.total++;")], [])));
    }

    [Fact]
    public async Task Resolvable_literal_reads_raise_nothing()
    {
        Assert.Empty(await ValidateAsync(new ActivityNode("w", "av-write", [Js("variables.order.total + getVariable('order').tax")], [])));
    }

    private async Task<IReadOnlyList<ValidationError>> ValidateAsync(ActivityNode root)
    {
        var validator = new ScriptVariableReadValidator(CatalogResolver(_catalog), Options(), new ActivityTreeWalker(_structures), _structures, _resolver);
        var state = new WorkflowDefinitionState([new VariableDefinition("order-key", "order", new TypeReference("Any"), null, null)], root, [], [], null);
        return [.. await validator.Validate(new Draft(state), CancellationToken.None)];
    }

    private static ArgumentState Body(string source) => new("script", new ArgumentValue(source, "Literal"), null, null, null, null);

    private static ArgumentState Js(string source) => new("Text", new ArgumentValue(source, "JavaScript"), null, null, null, null);

    private sealed class Draft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
