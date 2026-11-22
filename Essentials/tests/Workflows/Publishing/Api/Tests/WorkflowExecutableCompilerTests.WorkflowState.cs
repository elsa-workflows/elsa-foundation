using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Design.Validations.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class WorkflowExecutableCompilerTests
{
    private static ScriptVariableReadResolver StateReadResolver()
    {
        var services = new ServiceCollection();
        new Elsa.Expressions.JavaScript.JavaScriptFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        return new ScriptVariableReadResolver(provider.GetServices<IScriptVariableReadAnalyzer>());
    }
    [Theory]
    [InlineData("workflowState.setOutput('',1);")]
    [InlineData("workflowState.setOutput(42,1);")]
    [InlineData("function local(workflowState) {} workflowState.setCorrelationId(42);")]
    [InlineData("workflowState.getOutput(null);")]
    [InlineData("workflowState.setOutput('x');")]
    [InlineData("workflowState.setCorrelationId(42);")]
    [InlineData("workflowState.setInstanceName(false);")]
    [InlineData("workflowState.getInstanceName(1);")]
    public async Task Provably_invalid_workflow_state_calls_refuse_publication_without_values(string script)
    {
        var error = await Assert.ThrowsAsync<WorkflowExecutableCompilationException>(() => CompileBodyAsync(script, readResolver: StateReadResolver()).AsTask());
        Assert.Contains("VF-STATE-001", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(script, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("const key='x'; workflowState.setOutput(key,42);")]
    [InlineData("const name='new'; workflowState.setCorrelationId(name);")]
    [InlineData("function f(workflowState) { workflowState.setOutput('',1); } f({setOutput(){}});")]
    public async Task Computed_calls_and_local_lookalikes_keep_runtime_validation(string script) => await CompileBodyAsync(script, readResolver: StateReadResolver());
}
