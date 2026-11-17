using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Flowchart.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Internal;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests.ValidatorTestHelpers;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Workflows.Design.Tests.Unit.BaselineValidatorTests;

/// <summary>
/// Spec 1208 draft checks on scripts that select with <c>return complete(result, outcomes)</c>: a literal outcome name
/// outside the node's set is an error (<c>VF-AUTH-010</c>, FR-015, US3 scenarios 1–5), a returned string equal to a
/// connected outcome is a warning (<c>VF-AUTH-012</c>, FR-008a, US1 scenario 7), and declaring <c>complete</c> is an
/// error (FR-007a). Computed names, aliased <c>complete</c> and template literals are left to the run-time check.
/// </summary>
public sealed class ScriptOutcomeValidatorTests : IDisposable
{
    private const string ScriptVersionId = "av-script";
    private const string ScriptInput = "script";
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private readonly ServiceProvider _provider;
    private readonly IActivityStructureService _structures;
    private readonly IJavaScriptScriptOutcomeAnalyzer _analyzer;
    private readonly StubActivityCatalog _catalog = new();

    public ScriptOutcomeValidatorTests()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new Elsa.Activities.Flowchart.ActivitiesFlowchartFeature().ConfigureServices(services);
        // The script analysis is the JavaScript feature's; the validator consumes it through its contract.
        new JavaScriptFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _structures = _provider.CreateScope().ServiceProvider.GetRequiredService<IActivityStructureService>();
        _analyzer = _provider.GetRequiredService<IJavaScriptScriptOutcomeAnalyzer>();
        // Mirrors the elsa.outcomes facet the CLR scanner projects for RunJavaScript.
        _catalog.Add(ScriptVersionId, designFacets: [new ActivityDesignFacet("elsa.outcomes", "1", JsonSerializer.SerializeToElement(new
        {
            ports = Array.Empty<object>(),
            dynamicOutcomes = new { kind = "valuePerItem", source = "possibleOutcomes", selectsOnEveryCompletion = false },
            scriptInput = ScriptInput
        }))]);
        _catalog.Add("av-step");
        _catalog.Add("av-flowchart");
    }

    public void Dispose() => _provider.Dispose();

    [Fact]
    public async Task A_literal_unknown_name_is_an_error_naming_the_node_the_name_and_the_set()
    {
        var error = Assert.Single(await Validate(Script("js", "return complete(1, 'Aproved');", "Approved", "Rejected")));

        Assert.Equal(ScriptOutcomeValidator.UnknownOutcomeType, error.Type);
        Assert.Equal(ValidationSeverities.Error, error.Severity);
        Assert.Equal("js/inputs/script", error.Path);
        Assert.StartsWith("VF-AUTH-010:", error.Message, StringComparison.Ordinal);
        Assert.Contains("'js'", error.Message, StringComparison.Ordinal);
        Assert.Contains("'Aproved'", error.Message, StringComparison.Ordinal);
        Assert.Contains("['Approved', 'Done', 'Rejected']", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_known_literal_name_and_Done_are_accepted()
    {
        Assert.Empty(await Validate(Script("js", "if (x) return complete(1, 'Approved'); return complete(2, ['Done', 'Rejected']);", "Approved", "Rejected")));
    }

    [Fact]
    public async Task Each_unknown_name_in_an_array_literal_is_reported_once()
    {
        var errors = await Validate(Script("js", "return complete(1, ['Approved', 'Audit', 'Notify', 'Audit']);", "Approved"));

        Assert.Equal(2, errors.Count);
        Assert.All(errors, error => Assert.Equal(ScriptOutcomeValidator.UnknownOutcomeType, error.Type));
        Assert.Contains(errors, error => error.Message.Contains("'Audit'", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Message.Contains("'Notify'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_call_in_a_nested_function_is_checked()
    {
        var error = Assert.Single(await Validate(Script("js", "function route() { return complete(1, 'Elsewhere'); } return route();", "Approved")));

        Assert.Contains("'Elsewhere'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_possible_outcomes_the_set_is_Done_only()
    {
        // US3 scenario 4: several names on a node that declares none beyond Done.
        var errors = await Validate(Script("js", "return complete(1, ['Done', 'A']);"));

        var error = Assert.Single(errors);
        Assert.Contains("'A'", error.Message, StringComparison.Ordinal);
        Assert.Contains("['Done']", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("const name = 'Elsewhere'; return complete(1, name);")]
    [InlineData("return complete(1, ['Approved', pick()]);")]
    [InlineData("const finish = complete; return finish(1, 'Elsewhere');")]
    [InlineData("return complete(1, `Elsewhere`);")]
    [InlineData("return complete(1, [`Elsewhere`]);")]
    public async Task Computed_names_aliased_complete_and_template_literals_are_left_to_the_run_time_check(string script)
    {
        Assert.Empty(await Validate(Script("js", script, "Approved")));
    }

    [Fact]
    public async Task An_outcome_set_decided_by_an_expression_is_not_checked()
    {
        var node = new ActivityNode("js", ScriptVersionId,
        [
            LiteralInput(ScriptInput, "return complete(1, 'Anything');"),
            new ArgumentState("possibleOutcomes", new ArgumentValue("['A']", "JavaScript"), null, null, null, null)
        ], []);

        Assert.Empty(await Validate(node));
    }

    [Fact]
    public async Task A_returned_string_equal_to_a_connected_outcome_is_a_warning()
    {
        var root = FlowchartWith(Script("js", "return 'Approved';", "Approved", "Rejected"), "Approved");

        var warning = Assert.Single(await Validate(root));

        Assert.Equal(ScriptOutcomeValidator.ReturnedOutcomeNameType, warning.Type);
        Assert.Equal(ValidationSeverities.Warning, warning.Severity);
        Assert.Equal("js/inputs/script", warning.Path);
        Assert.StartsWith("VF-AUTH-012:", warning.Message, StringComparison.Ordinal);
        Assert.Contains("complete(result, 'Approved')", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_returned_string_that_names_no_connected_outcome_is_data()
    {
        // Rejected is declared but not connected; a nested function's return is not the script's result.
        var root = FlowchartWith(Script("js", "function f() { return 'Approved'; } f(); return 'Rejected';", "Approved", "Rejected"), "Approved");

        Assert.Empty(await Validate(root));
    }

    [Fact]
    public async Task A_script_that_declares_complete_is_an_error()
    {
        var error = Assert.Single(await Validate(Script("js", "const complete = (r, o) => r; return complete(1);")));

        Assert.Equal(ScriptOutcomeValidator.ReservedNameType, error.Type);
        Assert.Equal(ValidationSeverities.Error, error.Severity);
        Assert.Contains("reserved", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("function complete() { } return 1;")]
    [InlineData("const f = function (complete) { return complete; }; return f(1);")]
    public async Task A_function_or_parameter_named_complete_is_an_error(string script)
    {
        var error = Assert.Single(await Validate(Script("js", script)));

        Assert.Equal(ScriptOutcomeValidator.ReservedNameType, error.Type);
    }

    // Review round 1: every binding form declares the name, not only a plain variable, function, class or parameter.
    [Theory]
    [InlineData("const { complete } = { complete: 1 }; return 1;")]
    [InlineData("const { a: complete } = { a: 1 }; return 1;")]
    [InlineData("const [complete] = [1]; return 1;")]
    [InlineData("try { throw 1; } catch (complete) { } return 1;")]
    [InlineData("const f = function complete() { }; return 1;")]
    [InlineData("const C = class complete { }; return 1;")]
    [InlineData("function f(complete = 1) { } return 1;")]
    [InlineData("function f(a, ...complete) { } return 1;")]
    [InlineData("const f = ({ complete }) => complete; return 1;")]
    public async Task Every_binding_form_that_declares_complete_is_an_error(string script)
    {
        var error = Assert.Single(await Validate(Script("js", script)));

        Assert.Equal(ScriptOutcomeValidator.ReservedNameType, error.Type);
        Assert.Equal("js/inputs/script", error.Path);
    }

    [Theory]
    [InlineData("const { complete: done } = { complete: 1 }; return done;")]
    [InlineData("const o = { complete: 1 }; return o.complete;")]
    public async Task A_property_named_complete_declares_nothing(string script)
    {
        Assert.Empty(await Validate(Script("js", script)));
    }

    // The analyzer is the JavaScript feature's and optional: a host without it gets no script checks.
    [Fact]
    public async Task Without_a_script_outcome_analyzer_scripts_are_not_checked()
    {
        var validator = new ScriptOutcomeValidator(CatalogResolver(_catalog), Options(), new ActivityTreeWalker(_structures), _structures);

        Assert.Empty(await validator.Validate(new Draft(StateWithRoot(Script("js", "function complete() { } return complete(1, 'Aproved');", "Approved"))), CancellationToken.None));
    }

    [Fact]
    public async Task A_script_with_a_syntax_error_is_left_to_the_expression_tooling()
    {
        Assert.Empty(await Validate(Script("js", "return complete(1, 'X'")));
    }

    private ScriptOutcomeValidator Validator() =>
        new(CatalogResolver(_catalog), Options(), new ActivityTreeWalker(_structures), _structures, _analyzer);

    private async Task<IReadOnlyList<ValidationError>> Validate(ActivityNode root) =>
        [.. await Validator().Validate(new Draft(StateWithRoot(root)), CancellationToken.None)];

    private static ActivityNode Script(string nodeId, string script, params string[] possibleOutcomes)
    {
        var inputs = new List<ArgumentState> { LiteralInput(ScriptInput, script) };
        if (possibleOutcomes.Length > 0)
            inputs.Add(new ArgumentState("possibleOutcomes", new ArgumentValue(JsonSerializer.SerializeToElement(possibleOutcomes), "Literal"), null, null, null, null));
        return new ActivityNode(nodeId, ScriptVersionId, inputs, []);
    }

    private static ActivityNode FlowchartWith(ActivityNode script, string connectedPort)
    {
        var step = new ActivityNode("step", "av-step", [], []);
        return new ActivityNode("flowchart", "av-flowchart", [], [],
            new ActivityNodeStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure([script, step],
                    [new FlowchartConnection(new FlowchartEndpoint(script.NodeId, connectedPort), new FlowchartEndpoint("step"))], script.NodeId), Web)));
    }

    private sealed class Draft(WorkflowDefinitionState state) : IWorkflowDefinitionDraft
    {
        public string Id => "draft-1";
        public string WorkflowDefinitionId => "wf-1";
        public WorkflowDefinitionState State { get; } = state;
        public DateTimeOffset CreatedAt => DateTimeOffset.UnixEpoch;
        public DateTimeOffset LastModifiedAt => DateTimeOffset.UnixEpoch;
    }
}
