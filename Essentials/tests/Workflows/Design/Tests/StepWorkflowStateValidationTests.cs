using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests;

public sealed partial class StepVariableWriteValidationTests
{
    [Theory]
    [InlineData("workflowState.setOutput('',1);")]
    [InlineData("workflowState.setOutput(null,1);")]
    [InlineData("workflowState.setOutput('x');")]
    [InlineData("workflowState.setCorrelationId(42);")]
    [InlineData("workflowState.setInstanceName(false);")]
    [InlineData("workflowState.getCorrelationId(1);")]
    public async Task StepWorkflowState_provably_invalid_literal_calls_fail_authoring_validation(string script)
    {
        Assert.Contains(await Validate(Sequence("root", Script("script", script))), error => error.Type == "VF-STATE-001");
    }

    [Theory]
    [InlineData("workflowState.setCorrelationId('new');")]
    [InlineData("workflowState.setInstanceName('Ready');")]
    [InlineData("workflowState.setOutput('Count', 1);")]
    [InlineData("const s = workflowState; s.setCorrelationId('new');")]
    [InlineData("const build = (() => {}).constructor; build(\"workflowState.setCorrelationId('new')\")();")]
    public async Task StepWorkflowState_identity_and_opaque_access_refuse_background_placement(string script)
    {
        var node = Script("script", script) with { ExecutionPlacement = ActivityExecutionPlacement.Background };
        Assert.Contains(await Validate(Sequence("root", node)), error => error.Type == "VF-STATE-002");
    }

    [Theory]
    [InlineData("return 1;")]
    [InlineData("return variables.total;")]
    [InlineData("return Object.keys({a: 1});")]
    public async Task StepWorkflowState_known_read_only_scripts_do_not_invent_state_effects(string script)
    {
        Assert.DoesNotContain(await Validate(Sequence("root", Script("script", script))), error => error.Type == "VF-STATE-002");
    }
}
