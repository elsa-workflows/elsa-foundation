using Elsa.Events;
using Elsa.Expressions;
using Elsa.Expressions.JavaScript;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.JavaScript;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// D4 guardrail for ADR 0030. Before this unit, enabling <see cref="JavaScriptWorkflowsRuntimeFeature"/> was an
/// unguarded landmine: five pre/post-processors took an <c>IWorkflowExecutionContext</c> dependency registered
/// nowhere, so resolving the whole <see cref="IEnumerable{IScriptPreProcessor}"/> threw on first evaluation — and
/// no test enabled the feature. These tests enable the feature, resolve every registered processor, and evaluate
/// a script end-to-end, so a future change cannot silently re-orphan a processor behind a missing dependency.
/// </summary>
public sealed class JavaScriptRuntimeFeatureEvaluationTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;
    private readonly IJavaScriptEvaluator _evaluator;

    public JavaScriptRuntimeFeatureEvaluationTests()
    {
        _provider = BuildServiceProvider();
        _scope = _provider.CreateScope();
        _evaluator = _scope.ServiceProvider.GetRequiredService<IJavaScriptEvaluator>();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Fact]
    public void EnablingFeature_ResolvesEveryRegisteredScriptProcessor_WithoutMissingDependency()
    {
        // All implementations construct (MS DI eagerly constructs each when the enumerable is materialized).
        var preProcessors = _scope.ServiceProvider.GetServices<IScriptPreProcessor>().ToList();
        var postProcessors = _scope.ServiceProvider.GetServices<IScriptPostProcessor>().ToList();

        Assert.NotEmpty(preProcessors);
        Assert.NotEmpty(postProcessors);
    }

    [Fact]
    public async Task EnablingFeature_EvaluatesScriptEndToEnd_AcrossAllProcessors()
    {
        var result = await _evaluator.EvaluateAsync("1 + 1", typeof(object), NewExecutionContext(_scope.ServiceProvider));

        Assert.Equal(2d, Convert.ToDouble(result));
    }

    [Fact]
    public async Task EnablingFeature_ExecutionTimeIdentityAndStateAccessorsAreLive()
    {
        var context = NewExecutionContext(
            _scope.ServiceProvider,
            correlationId: "order-123",
            workflowInputs: new Dictionary<string, object?> { ["name"] = "World" },
            activityOutputValues: new Dictionary<string, object?> { ["Result"] = "prior" });

        Assert.Equal("wfexec-1", await _evaluator.EvaluateAsync("getWorkflowInstanceId()", typeof(object), context));
        Assert.Equal("order-123", await _evaluator.EvaluateAsync("getCorrelationId()", typeof(object), context));
        Assert.Equal("definition-1", await _evaluator.EvaluateAsync("getWorkflowDefinitionId()", typeof(object), context));
        Assert.Equal("World", await _evaluator.EvaluateAsync("getInput('name')", typeof(object), context));
        Assert.Equal("prior", await _evaluator.EvaluateAsync("getOutput('Result')", typeof(object), context));
    }

    [Fact]
    public async Task SetCorrelationIdAndName_AreVisibleOnReadBackWithinTheSameEvaluation()
    {
        // Read-after-write: a script that sets correlation id / instance name then reads it back observes the new
        // value, even though the durable change folds at the checkpoint (spec 064 scenario 3, carried forward).
        var context = NewExecutionContext(_scope.ServiceProvider, correlationId: "order-1");

        Assert.Equal("order-42", await _evaluator.EvaluateAsync("setCorrelationId('order-42'); getCorrelationId()", typeof(object), context));
        Assert.Equal("Order 42", await _evaluator.EvaluateAsync("setWorkflowInstanceName('Order 42'); getWorkflowInstanceName()", typeof(object), context));
    }

    private static SimpleActivityExecutionContext NewExecutionContext(
        IServiceProvider serviceProvider,
        string? correlationId = null,
        IReadOnlyDictionary<string, object?>? workflowInputs = null,
        IReadOnlyDictionary<string, object?>? workflowVariables = null,
        IReadOnlyDictionary<string, object?>? activityOutputValues = null)
    {
        var empty = new Dictionary<string, object?>();
        var carrier = new RuntimeExecutionExpressionCarrierState(
            CorrelationId: correlationId,
            WorkflowName: null,
            WorkflowDefinitionVersion: 0,
            WorkflowInputs: workflowInputs ?? empty,
            WorkflowVariables: workflowVariables ?? empty,
            ActivityOutputValues: activityOutputValues ?? empty);

        return SimpleActivityExecutionContext.ForExecution(
            serviceProvider,
            new TestActivity(),
            CancellationToken.None,
            workflowExecutionId: "wfexec-1",
            pinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-7", "7.0.0", "hash-1"),
            schedulerWorkItem: null,
            executableNode: null,
            activityExecutionState: null,
            variableScope: null,
            executionCarrier: carrier);
    }

    internal static ServiceProvider BuildServiceProvider()
    {
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        new EventsFeature().ConfigureServices(services);
        new SerializationFeature().ConfigureServices(services);
        new ExpressionsFeature().ConfigureServices(services);
        new JavaScriptFeature().ConfigureServices(services);
        new JintFeature().ConfigureServices(services);

        // Enable the whole feature (not just MaterializationAccessorsPreProcessor as other tests do) — this is
        // exactly what no test did before ADR 0030, and the reason the landmine went unnoticed.
        new JavaScriptWorkflowsRuntimeFeature().ConfigureServices(services);

        return services.BuildServiceProvider();
    }
}
