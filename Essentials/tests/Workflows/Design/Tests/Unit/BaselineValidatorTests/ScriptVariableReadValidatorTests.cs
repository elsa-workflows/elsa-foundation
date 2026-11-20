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
