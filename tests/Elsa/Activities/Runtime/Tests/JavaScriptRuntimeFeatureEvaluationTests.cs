using Elsa.Activities.Runtime.Core.Contracts;
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
public sealed class JavaScriptRuntimeFeatureEvaluationTests
{
    [Fact]
    public void EnablingFeature_ResolvesEveryRegisteredScriptProcessor_WithoutMissingDependency()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();

        var preProcessors = scope.ServiceProvider.GetServices<IScriptPreProcessor>().ToList();
        var postProcessors = scope.ServiceProvider.GetServices<IScriptPostProcessor>().ToList();

        // All implementations construct (MS DI eagerly constructs each when the enumerable is materialized).
        Assert.NotEmpty(preProcessors);
        Assert.NotEmpty(postProcessors);
    }

    [Fact]
    public async Task EnablingFeature_EvaluatesScriptEndToEnd_AcrossAllProcessors()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IJavaScriptEvaluator>();
        var context = NewExecutionContext(scope.ServiceProvider);

        var result = await evaluator.EvaluateAsync("1 + 1", typeof(object), context);

        Assert.Equal(2d, Convert.ToDouble(result));
    }

    [Fact]
    public async Task EnablingFeature_ExecutionTimeIdentityAndStateAccessorsAreLive()
    {
        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var evaluator = scope.ServiceProvider.GetRequiredService<IJavaScriptEvaluator>();
        var context = NewExecutionContext(
            scope.ServiceProvider,
            correlationId: "order-123",
            workflowInputs: new Dictionary<string, object?> { ["name"] = "World" },
            activityOutputValues: new Dictionary<string, object?> { ["Result"] = "prior" });

        Assert.Equal("wfexec-1", await evaluator.EvaluateAsync("getWorkflowInstanceId()", typeof(object), context));
        Assert.Equal("order-123", await evaluator.EvaluateAsync("getCorrelationId()", typeof(object), context));
        Assert.Equal("definition-1", await evaluator.EvaluateAsync("getWorkflowDefinitionId()", typeof(object), context));
        Assert.Equal("World", await evaluator.EvaluateAsync("getInput('name')", typeof(object), context));
        Assert.Equal("prior", await evaluator.EvaluateAsync("getOutput('Result')", typeof(object), context));
    }

    private static SimpleActivityExecutionContext NewExecutionContext(
        IServiceProvider serviceProvider,
        string? correlationId = null,
        IReadOnlyDictionary<string, object?>? workflowInputs = null,
        IReadOnlyDictionary<string, object?>? workflowVariables = null,
        IReadOnlyDictionary<string, object?>? activityOutputValues = null) =>
        new(
            serviceProvider,
            new TestActivity(),
            CancellationToken.None,
            workflowExecutionId: "wfexec-1",
            pinnedExecutable: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-7", "7.0.0", "hash-1"),
            correlationId: correlationId,
            workflowInputs: workflowInputs,
            workflowVariables: workflowVariables,
            activityOutputValues: activityOutputValues);

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

    private sealed class TestActivity : IActivity
    {
        public string Id { get; set; } = "activity-1";
        public string NodeId { get; set; } = "node-1";
        public string? Name { get; set; }
        public string Type { get; set; } = "Test.Activity";
        public string Version { get; set; } = "1.0.0";
        public Dictionary<string, object> CustomProperties { get; set; } = new();
        public Dictionary<string, object> SyntheticProperties { get; set; } = new();
        public Dictionary<string, object> Metadata { get; set; } = new();
        public ValueTask<bool> CanExecuteAsync(IActivityExecutionContext context) => new(true);
        public ValueTask ExecuteAsync(IActivityExecutionContext context) => ValueTask.CompletedTask;
    }
}
