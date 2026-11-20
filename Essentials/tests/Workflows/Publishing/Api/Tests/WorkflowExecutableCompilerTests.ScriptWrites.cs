using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Publishing.Api.Tests;

/// <summary>
/// Spec 1231 at publish (T024, T029a): a script body's literal write to an undeclared variable is refused naming its
/// position and target; the variables it may write are pinned on the node; a computed write marks the node as possibly
/// writing every visible variable.
/// </summary>
public sealed partial class WorkflowExecutableCompilerTests
{
    [Fact]
    public async Task A_body_that_writes_an_undeclared_variable_refuses_publication_naming_the_position()
    {
        var exception = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() =>
            CompileBodyAsync("const a = 1;\nvariables.nope = a; return 1;").AsTask());

        Assert.Contains(ScriptVariableReadDiagnostics.UndeclaredLiteralWrite, exception.Message, StringComparison.Ordinal);
        Assert.Contains("writes variable 'nope' (variables.nope) at line 2, column 1", exception.Message, StringComparison.Ordinal);
        Assert.Contains("'script'", exception.Message, StringComparison.Ordinal);
        Assert.Contains("never creates a variable", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_pins_the_variables_it_may_write()
    {
        var executable = await CompileBodyAsync("variables.greeting = 'hi'; return 1;");

        var metadata = executable.RootActivity.Metadata;
        using var targets = JsonDocument.Parse(metadata[RuntimeMetadataKeys.MayWriteVariables]);
        var target = Assert.Single(targets.RootElement.EnumerateArray());
        Assert.Equal(VariableReference.WorkflowScopeId, target.GetProperty("scopeId").GetString());
        Assert.Equal("greeting-key", target.GetProperty("variableKey").GetString());
        Assert.Equal("greeting", target.GetProperty("name").GetString());
        Assert.False(metadata.ContainsKey(RuntimeMetadataKeys.MayWriteEveryVisibleVariable));
    }

    [Fact]
    public async Task An_engine_script_activitys_body_decides_its_may_write_set_over_its_type_marker()
    {
        // The probe is an engine script activity, pinned as accessing any visible variable by name (the runtime lets its
        // computed names resolve, and C9 refuses it in the background); a body with only literal writes still pins only those.
        var executable = await CompileBodyAsync("variables.greeting = 'hi'; return 1;");

        Assert.True(executable.RootActivity.ActivityContract!.AccessesUndeclaredVariables);
        Assert.False(executable.RootActivity.Metadata.ContainsKey(RuntimeMetadataKeys.MayWriteEveryVisibleVariable));
    }

    [Fact]
    public async Task A_body_that_only_reads_pins_no_write()
    {
        var executable = await CompileBodyAsync("return variables.greeting + '!';");

        Assert.False(executable.RootActivity.Metadata.ContainsKey(RuntimeMetadataKeys.MayWriteVariables));
        Assert.False(executable.RootActivity.Metadata.ContainsKey(RuntimeMetadataKeys.MayWriteEveryVisibleVariable));
    }

    [Fact]
    public async Task A_body_with_a_computed_write_publishes_and_may_write_every_visible_variable()
    {
        var executable = await CompileBodyAsync("variables[k] = 1; return 1;");

        Assert.Equal("true", executable.RootActivity.Metadata[RuntimeMetadataKeys.MayWriteEveryVisibleVariable]);
    }

    private ValueTask<WorkflowExecutable> CompileBodyAsync(string script)
    {
        var registry = TestWellKnownTypeRegistry.Create();
        registry.RegisterType(typeof(ScriptProbe), TypeAliasConvention.CanonicalAlias(typeof(ScriptProbe)));
        var probe = ClrActivityVersion("activity-script-probe", "Test.ScriptProbe", TypeAliasConvention.CanonicalAlias(typeof(ScriptProbe)));
        probe.Inputs = [new InputDefinition("script", "Script", new TypeReference("String"), null, "Script", null, IsNullable: true)];
        var node = new Elsa.Workflows.Design.Core.Models.ActivityNode(
            "script",
            "activity-script-probe",
            [new WorkflowArgumentState("script", new ArgumentValue(script, "Literal"), null, null, null, null)],
            Outputs: []);
        return TestCompiler.Create(
                new FakeVersionStore(WorkflowVersion(node, variables: [Greeting])),
                new FakeActivityVersionStore([probe]),
                _activityStructureService,
                registry,
                scriptVariableReadResolver: ReadResolver)
            .CompileAsync(NewRequest(DateTimeOffset.UtcNow));
    }
}
