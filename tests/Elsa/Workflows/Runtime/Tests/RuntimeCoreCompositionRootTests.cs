using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// RT-4 guard: the runtime execution spine is composed by the host-agnostic <see cref="RuntimeCoreServiceCollectionExtensions.AddWorkflowRuntime"/>
/// composition root, so a non-HTTP host (worker, test harness, another module) can resolve and drive the runtime without
/// the FastEndpoints <c>WorkflowsRuntimeApiFeature</c>. Mirrors the failure class the review flagged: the runtime must not
/// be reachable only through the API feature.
/// </summary>
public sealed class RuntimeCoreCompositionRootTests : RuntimePipelineTestSupport
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddWorkflowRuntime_keeps_expression_consumers_inside_the_per_work_scope(bool registerExpressionsFirst)
    {
        var services = new ServiceCollection();
        if (registerExpressionsFirst)
            AddExpressionServices(services);
        services.AddWorkflowRuntime();
        if (!registerExpressionsFirst)
            AddExpressionServices(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        Assert.NotNull(provider.GetRequiredService<IWorkflowSchedulerDrainer>());
        Assert.Contains(
            provider.GetServices<IWorkflowSchedulerWorkHandler>(),
            handler => handler is WorkflowStartActivitySchedulerWorkHandler);
        using var scope = provider.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRuntimeActivityInputMaterializer>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<WorkflowIntrinsicExecutor>());
    }

    [Fact]
    public void AddWorkflowRuntime_ResolvesTheExecutionSpine_WithoutTheApiFeature()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();

        // The whole dispatch graph must resolve from the Core composition root alone.
        Assert.NotNull(provider.GetService<IWorkflowSchedulerDrainer>());
        Assert.NotNull(provider.GetService<IWorkflowExecutionCommandExecutor>());
        Assert.NotNull(provider.GetService<IWorkflowDrainOrchestrator>());
        Assert.NotNull(provider.GetService<IRuntimeExecutionPipelineDispatcher>());
        Assert.NotNull(provider.GetService<IRuntimeWorkflowExecutionPipeline>());
        Assert.NotNull(provider.GetService<IRuntimeActivityExecutionPipeline>());
        Assert.NotNull(provider.GetService<IWorkflowExecutionActorProvider>());
        Assert.NotNull(provider.GetService<IWorkflowStartDispatcher>());
        Assert.NotEmpty(provider.GetServices<IWorkflowSchedulerWorkHandler>());
    }

    [Fact]
    public async Task AddWorkflowRuntime_DrivesADrainEndToEnd_WithoutTheApiFeature()
    {
        using var provider = new ServiceCollection().AddWorkflowRuntime().BuildServiceProvider();
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewExecutable());
        await provider.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(NewWorkflowState(WorkflowExecutionStatus.Running));
        await provider.GetRequiredService<IActivityExecutionStateStore>().SaveAsync(NewActivityStateForStatus(ActivityExecutionStatus.Running));
        await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(NewCancelWorkItem());

        var result = await provider.GetRequiredService<IWorkflowSchedulerDrainer>()
            .DrainAsync(new RuntimeSchedulerDrainRequest("wf-1"));

        // The Cancel work item ran through the composed workflow pipeline (Invoke slot -> Checkpoint slot -> committer).
        var item = Assert.Single(result.Items);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Completed, item.Status);
        var committed = Assert.Single(provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>().ListCommits());
        Assert.Equal(RuntimeCheckpointNames.ActivityCancelled, committed.Commit.Checkpoint.Name);
        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wf-1");
        Assert.Equal(WorkflowExecutionStatus.Cancelled, workflowState!.Status);
    }

    private static void AddExpressionServices(IServiceCollection services)
    {
        services.AddScoped<IPortableExpressionEvaluator, StubPortableExpressionEvaluator>();
        services.AddSingleton<IWellKnownTypeRegistry, StubWellKnownTypeRegistry>();
    }

    private sealed class StubPortableExpressionEvaluator : IPortableExpressionEvaluator
    {
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) =>
            ValueTask.FromResult(JsonSerializer.SerializeToElement<object?>(null));
    }

    private sealed class StubWellKnownTypeRegistry : IWellKnownTypeRegistry
    {
        public void RegisterType(Type type, string alias) => throw new NotSupportedException();
        public bool TryGetAlias(Type type, out string alias)
        {
            alias = "String";
            return type == typeof(string);
        }

        public bool TryGetType(string alias, out Type type) => TryGetTypeOrDefault(alias, out type);
        public IEnumerable<Type> ListTypes() => [typeof(string)];
        public string GetAliasOrDefault(Type type) => type == typeof(string) ? "String" : type.FullName!;
        public Type GetTypeOrDefault(string alias) => TryGetTypeOrDefault(alias, out var type) ? type : typeof(object);
        public bool TryGetTypeOrDefault(string alias, out Type type)
        {
            type = typeof(string);
            return StringComparer.Ordinal.Equals(alias, "String");
        }
    }
}
