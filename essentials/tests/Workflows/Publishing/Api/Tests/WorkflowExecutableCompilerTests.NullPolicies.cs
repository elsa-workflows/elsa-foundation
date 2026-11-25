using Elsa.Activities.Scripting.Activities;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class WorkflowExecutableCompilerTests
{
    [Theory]
    [InlineData("1;", true, true)]
    [InlineData("function helper() { return 1; } helper();", true, true)]
    [InlineData("const helper = () => { return 1; }; helper();", true, true)]
    [InlineData("function helper() { return 1; } return helper();", true, false)]
    [InlineData("if (args.flag) return 1;", true, false)]
    [InlineData("1;", false, false)]
    public async Task Only_a_consumed_body_result_requires_a_return_in_the_activity_body(string body, bool bound, bool refused)
    {
        var services = new ServiceCollection(); new JavaScriptFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var root = new ActivityNode("script", typeof(RunJavaScript).FullName!,
            [new("script", ArgumentValue.FromClr(body, "Literal"), null, null, null, null)], bound
                ? [new("result", new ArgumentValue(JsonSerializer.SerializeToElement(new { referenceKey = "captured", declaringScopeId = "workflow" }), "Variable"), null, null, null, null)] : []);
        var compile = SecretBindingCompilerFixture.CompileAsync(root, [typeof(RunJavaScript)],
            variables: [new("captured", "captured", new("Any"), null, null)], includeDeclaredInputDefaults: true,
            scriptOutcomeAnalyzer: provider.GetRequiredService<IJavaScriptScriptOutcomeAnalyzer>());
        if (refused) Assert.Contains("VF-UNDEF-001", (await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compile)).Message);
        else await compile;
    }

    [Theory]
    [InlineData("String", false, false, "Literal")]
    [InlineData("Int32", false, true, "Literal")]
    [InlineData("Int32", true, false, "Literal")]
    [InlineData("String", false, false, "literal")]
    [InlineData("Int32", false, true, "literal")]
    [InlineData("Int32", true, false, "LITERAL")]
    public async Task SetOutput_uses_the_declared_output_type_instead_of_the_authored_fallback(string alias, bool policy, bool refused, string syntax)
    {
        var root = new ActivityNode("set", "", [
            new("name", ArgumentValue.FromClr("count", syntax), null, null, null, null),
            new("value", new ArgumentValue(JsonSerializer.SerializeToElement<object?>(null), "Literal"), null, null, null, null)], [])
            { Intrinsic = new(AuthoredWorkflowIntrinsicKind.SetOutput, new(alias == "String" ? "Int32" : "Any")) };
        var version = WorkflowVersion(root);
        version.State = version.State! with { Outputs = [new("count", "count", new(alias), null, "Count", null, false)],
            StrategyOptions = new() { NullHandling = new() { DefaultOnNull = policy } } };
        var compiler = TestCompiler.Create(new FakeVersionStore(version), new FakeActivityVersionStore([]), _activityStructureService, TestWellKnownTypeRegistry.Create());
        if (refused)
        {
            var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());
            Assert.Contains("VF-ACT-004", error.Message);
        }
        else
        {
            var executable = await compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow));
            Assert.Equal(alias, executable.RootActivity.InputBindings["value"].TargetType.Alias);
            Assert.Equal(ValuePresence.ExplicitNull, executable.RootActivity.InputBindings["value"].Literal!.Presence);
        }
    }

    [Fact]
    public async Task Required_workflow_input_with_a_default_is_refused_before_compilation()
    {
        var version = WorkflowVersion(Node("consumer", Text("first")));
        version.State = version.State! with { Inputs = [WorkflowInput("value", new("String"), true, JsonSerializer.SerializeToElement("default"), "Literal")] };
        var compiler = TestCompiler.Create(new FakeVersionStore(version), new FakeActivityVersionStore([_writeLineActivity]), _activityStructureService, TestWellKnownTypeRegistry.Create());
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => compiler.CompileAsync(NewRequest(DateTimeOffset.UtcNow)).AsTask());
        Assert.Contains("VF-ACT-014", error.Message);
    }
}
