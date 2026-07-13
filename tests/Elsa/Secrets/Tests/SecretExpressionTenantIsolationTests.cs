using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Expressions.Core.Contracts;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Expressions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretExpressionTenantIsolationTests
{
    [Fact]
    public async Task ExecutionTimeContextsResolveSameSecretNameForConcurrentTenantsIndependently()
    {
        var stateStore = await StateStoreAsync(
            State("execution-a", "tenant-a"),
            State("execution-b", "tenant-b"));
        var resolver = new SynchronizingResolver(new Dictionary<string, string>
        {
            ["tenant-a"] = "value-a",
            ["tenant-b"] = "value-b"
        });
        await using var provider = Services(stateStore).BuildServiceProvider();
        var handler = new SecretExpressionHandler(resolver, null!);
        var expression = new TestExpression("Secret", "shared.name");

        var tenantA = handler.EvaluateAsync(expression, typeof(string), ExecutionContext(provider, "execution-a"), Options.Instance).AsTask();
        var tenantB = handler.EvaluateAsync(expression, typeof(string), ExecutionContext(provider, "execution-b"), Options.Instance).AsTask();
        var values = await Task.WhenAll(tenantA, tenantB);

        Assert.Equal(["value-a", "value-b"], values);
        Assert.Equal(["tenant-a", "tenant-b"], resolver.ObservedTenants.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task MaterializationTimeContextsResolveSameSecretNameForConcurrentTenantsIndependently()
    {
        var stateStore = await StateStoreAsync(
            State("execution-a", "tenant-a"),
            State("execution-b", "tenant-b"));
        var resolver = new SynchronizingResolver(new Dictionary<string, string>
        {
            ["tenant-a"] = "value-a",
            ["tenant-b"] = "value-b"
        });
        var handler = new SecretExpressionHandler(resolver, null!);
        var services = Services(stateStore);
        services.AddSingleton<IExpressionEvaluator>(new SecretEvaluator(handler));
        await using var provider = services.BuildServiceProvider();
        var materializer = new RuntimeActivityInputMaterializer();
        var node = SecretInputNode();

        var tenantA = materializer.MaterializeInputsAsync(node, MaterializationContext(provider, "execution-a", "activity-a")).AsTask();
        var tenantB = materializer.MaterializeInputsAsync(node, MaterializationContext(provider, "execution-b", "activity-b")).AsTask();
        var materialized = await Task.WhenAll(tenantA, tenantB);

        Assert.Equal("value-a", Assert.Single(materialized[0]).Value);
        Assert.Equal("value-b", Assert.Single(materialized[1]).Value);
        Assert.Equal(["tenant-a", "tenant-b"], resolver.ObservedTenants.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingAuthoritativeTenantFailsClosedWithoutCallingResolver(string? tenantId)
    {
        var stateStore = await StateStoreAsync(State("execution-a", tenantId));
        var resolver = new RecordingResolver();
        await using var provider = Services(stateStore).BuildServiceProvider();
        var handler = new SecretExpressionHandler(resolver, null!);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await handler.EvaluateAsync(
                new TestExpression("Secret", "shared.name"),
                typeof(string),
                ExecutionContext(provider, "execution-a"),
                Options.Instance));

        Assert.Contains("authoritative tenant", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, resolver.CallCount);
    }

    private static ServiceCollection Services(IWorkflowExecutionStateStore stateStore)
    {
        var services = new ServiceCollection();
        services.AddSingleton(stateStore);
        return services;
    }

    private static async Task<InMemoryWorkflowExecutionStateStore> StateStoreAsync(params WorkflowExecutionState[] states)
    {
        var store = new InMemoryWorkflowExecutionStateStore();
        foreach (var state in states)
            await store.SaveAsync(state);
        return store;
    }

    private static WorkflowExecutionState State(string executionId, string? tenantId) => new(
        executionId,
        new("artifact", "definition", "version", "1.0.0", "hash"),
        WorkflowExecutionStatus.Running,
        null,
        DateTimeOffset.Parse("2026-07-13T10:00:00Z"),
        DateTimeOffset.Parse("2026-07-13T10:00:00Z"),
        DateTimeOffset.Parse("2026-07-13T10:00:00Z"),
        null,
        null,
        null,
        tenantId,
        new Dictionary<string, string>(),
        WorkflowExecutionOrigin.Published);

    private static SimpleActivityExecutionContext ExecutionContext(IServiceProvider provider, string workflowExecutionId)
    {
        var empty = new Dictionary<string, object?>();
        var carrier = new RuntimeExecutionExpressionCarrierState(
            null,
            null,
            1,
            empty,
            empty,
            empty,
            null,
            null,
            null);
        return SimpleActivityExecutionContext.ForExecution(
            provider,
            new TestActivity(),
            CancellationToken.None,
            workflowExecutionId,
            new("artifact", "definition", "version", "1.0.0", "hash"),
            null,
            null,
            null,
            null,
            carrier);
    }

    private static RuntimeInputBindingResolutionContext MaterializationContext(
        IServiceProvider provider,
        string workflowExecutionId,
        string activityExecutionId) => new(
        workflowExecutionId,
        activityExecutionId,
        new Dictionary<string, DurableValueState>(),
        EmptyActivityOutputReader.Instance,
        provider);

    private static ExecutableNode SecretInputNode() => new(
        "node",
        "activity",
        "Test",
        "1.0.0",
        "test",
        JsonDocument.Parse("{}").RootElement,
        new Dictionary<string, RuntimeInputBinding>
        {
            ["Value"] = new(
                "Value",
                RuntimeInputBindingSource.Expression,
                expression: new("Secret", "shared.name"),
                metadata: new Dictionary<string, string>
                {
                    [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = typeof(string).AssemblyQualifiedName!
                })
        },
        new Dictionary<string, RuntimeOutputCapture>(),
        new Dictionary<string, string>());

    private sealed class SecretEvaluator(SecretExpressionHandler handler) : IExpressionEvaluator
    {
        public async ValueTask<T?> EvaluateAsync<T>(
            IExpression expression,
            IExpressionExecutionContext context,
            IExpressionEvaluatorOptions? options = null) =>
            (T?)await EvaluateAsync(expression, typeof(T), context, options);

        public ValueTask<object?> EvaluateAsync(
            IExpression expression,
            Type returnType,
            IExpressionExecutionContext context,
            IExpressionEvaluatorOptions? options = null) =>
            handler.EvaluateAsync(expression, returnType, context, options ?? Options.Instance);
    }

    private sealed class SynchronizingResolver(IReadOnlyDictionary<string, string> values) : ISecretValueResolver
    {
        private readonly TaskCompletionSource _bothEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _calls;
        public List<string> ObservedTenants { get; } = [];

        public async ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
        {
            lock (ObservedTenants)
                ObservedTenants.Add(tenantId);
            if (Interlocked.Increment(ref _calls) == 2)
                _bothEntered.TrySetResult();
            await _bothEntered.Task.WaitAsync(cancellationToken);
            return ResolvedSecret.Success(values[tenantId], new() { TenantId = tenantId, Name = reference.Name });
        }
    }

    private sealed class RecordingResolver : ISecretValueResolver
    {
        public int CallCount { get; private set; }

        public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return ValueTask.FromResult(ResolvedSecret.Success("unexpected", new()));
        }
    }

    private sealed class Options : IExpressionEvaluatorOptions
    {
        public static readonly Options Instance = new();
        public IDictionary<string, object> Arguments { get; } = new Dictionary<string, object>();
    }

    private sealed class TestExpression(string type, object? value) : IExpression
    {
        public string Type { get; set; } = type;
        public object? Value { get; set; } = value;
        public TValue GetValue<TValue>() => Value is TValue typed ? typed : default!;
    }

    private sealed class TestActivity : ActivityBase;

    private sealed class EmptyActivityOutputReader : IRuntimeActivityOutputReader
    {
        public static readonly EmptyActivityOutputReader Instance = new();

        public bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output)
        {
            output = null!;
            return false;
        }

        public IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId) => [];
    }
}
