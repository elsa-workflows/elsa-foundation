using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Design.Validations.Validators;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

/// <summary>
/// Spec 1241 User Story 3 (T008–T010, SC-004): the step-write analyzer a reusable activity's body is validated with finds
/// what workflow draft validation finds for the same body: reference refusals, the overlap warning, the step writers the
/// concurrent-write rule counts, and a script body's refused literal writes.
/// </summary>
public sealed partial class StepVariableWriteValidationTests
{
    [Fact]
    public void The_feature_registers_the_analyzer_scoped()
    {
        using var services = Services();
        using var scope = services.CreateScope();

        var analyzer = scope.ServiceProvider.GetRequiredService<IStepVariableWriteAnalyzer>();
        Assert.Equal("Elsa.Workflows.Design.Validations.Services.StepVariableWriteAnalyzer", analyzer.GetType().FullName);
    }

    public static TheoryData<string> Bodies => new()
    {
        // US3 AS4: unbound, undeclared, another expression, another type.
        "unbound",
        "undeclared",
        "literal",
        "wrong-type",
        // US3 AS3: a step write that overlaps the node's own capture.
        "overlap",
        "script-overlap",
        "clean"
    };

    [Theory]
    [MemberData(nameof(Bodies))]
    public async Task The_analyzer_reports_what_workflow_validation_reports(string body)
    {
        var root = Body(body);

        var workflow = (await Validate(root))
            .Where(item => item.Type is StepVariableWriteRules.VariableReference or StepVariableWriteRules.StepWriteOverlapsCapture)
            .Select(item => (item.Path, item.Type, item.Message, item.Severity));
        var analysis = await AnalyzeAsync(root);

        Assert.Equal(workflow.Order(), analysis.Findings.Select(item => (item.Path, item.Type, item.Message, item.Severity)).Order());
    }

    [Fact]
    public async Task The_analyzer_counts_reference_marked_and_script_writers()
    {
        // US3 AS1, AS2: the step writers the concurrent-write rule counts.
        var root = Sequence("root",
            Writer("writer", "balance"),
            new ActivityNode("generic", MarkedVersion, [], []),
            Script("script", "variables.total = variables.total + 1;"));

        var facts = (await AnalyzeAsync(root)).Facts;

        Assert.Contains(facts.ByNodeId["writer"].Targets, target => target is { ScopeId: "workflow", ReferenceKey: "balance" });
        Assert.True(facts.ByNodeId["generic"].MayWriteEveryVisibleVariable);
        Assert.Contains(facts.ByNodeId["script"].Targets, target => target is { ScopeId: "workflow", ReferenceKey: "total" });
    }

    [Fact]
    public async Task The_analyzer_refuses_a_script_write_to_an_undeclared_name_as_the_script_validator_does()
    {
        // US3 AS5: a body has no script read validator of its own, so the analyzer reports the same refusal.
        var root = Sequence("root", Script("script", "variables.missing = 1; return 1;"));

        var analysis = await AnalyzeAsync(root);

        var refused = Assert.Single(analysis.Findings, item => item.Type == StepVariableWriteRules.RefusedScriptWrite);
        Assert.Equal("script/inputs/script", refused.Path);
        Assert.NotEqual(ValidationSeverities.Warning, refused.Severity);
        using var services = Services();
        using var scope = services.CreateScope();
        var scriptValidator = scope.ServiceProvider.GetServices<IDraftValidator>().OfType<ScriptVariableReadValidator>().Single();
        var workflow = await scriptValidator.Validate(new StubDraft(new WorkflowDefinitionState(Variables, root, [], [], null)), CancellationToken.None);
        Assert.Contains(workflow, item => item.Type == StepVariableWriteRules.RefusedScriptWrite && item.Message == refused.Message);
    }

    [Fact]
    public async Task The_analyzer_warns_about_a_computed_script_write_as_the_script_validator_does()
    {
        // SC-004: a body script that writes by a computed name gets the workflow's warning.
        var root = Sequence("root", Script("script", "const name = 'total'; variables[name] = 1; return 1;"));

        var analysis = await AnalyzeAsync(root);

        var computed = Assert.Single(analysis.Findings, item => item.Type == StepVariableWriteRules.ComputedScriptWrite);
        Assert.Equal(ValidationSeverities.Warning, computed.Severity);
        using var services = Services();
        using var scope = services.CreateScope();
        var scriptValidator = scope.ServiceProvider.GetServices<IDraftValidator>().OfType<ScriptVariableReadValidator>().Single();
        var workflow = await scriptValidator.Validate(new StubDraft(new WorkflowDefinitionState(Variables, root, [], [], null)), CancellationToken.None);
        Assert.Contains(workflow, item => item.Type == StepVariableWriteRules.ComputedScriptWrite && item.Message == computed.Message);
    }

    private static ActivityNode Body(string body) => body switch
    {
        "unbound" => Sequence("root", new ActivityNode("writer", WriterVersion, [], [])),
        "undeclared" => Sequence("root", Writer("writer", "missing")),
        "literal" => Sequence("root", new ActivityNode("writer", WriterVersion, [new ArgumentState("Balance", new ArgumentValue(5, "Literal"), null, null, null, null)], [])),
        "wrong-type" => Sequence("root", Writer("writer", "total")),
        "overlap" => Sequence("root", Writer("writer", "balance") with { Outputs = [VariableOutput("Result", "balance")] }),
        "script-overlap" => Sequence("root", Script("script", "setVariable('total', 2); return 1;") with { Outputs = [VariableOutput("result", "total")] }),
        _ => Sequence("root", Writer("writer", "balance"))
    };

    private static async Task<StepVariableWriteAnalysis> AnalyzeAsync(ActivityNode root)
    {
        using var services = Services();
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IStepVariableWriteAnalyzer>().AnalyzeAsync(root, Variables);
    }
}
