using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Models;
using Elsa.Expressions.Options;
using Elsa.Expressions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class VariableExpressionEvaluatorTests
{
    [Fact]
    public async Task EvaluateAsync_ResolvesVariableExpressionThroughDefaultDescriptor()
    {
        var registry = new ExpressionDescriptorRegistry([new DefaultExpressionDescriptorProvider()]);
        var evaluator = new ExpressionEvaluator(registry, new ServiceCollection().BuildServiceProvider(), Options.Create(ExpressionEvaluatorOptions.Empty));
        var variable = new Variable("Counter", 42);
        var context = new TestExpressionContext(variable);

        var result = await evaluator.EvaluateAsync<int>(new TestExpression(WellKnownExpressionDescriptorTypes.Variable, "Counter"), context);

        Assert.Equal(42, result);
    }

    private sealed class TestExpressionContext(params IVariable[] variables) : IExpressionExecutionContext
    {
        private readonly Dictionary<string, IVariable> variablesByName = variables.ToDictionary(x => x.Name);

        public IMemoryRegister Memory { get; } = new TestMemoryRegister();

        public IExpressionExecutionContext? ParentContext { get; set; }

        public CancellationToken CancellationToken => CancellationToken.None;

        public bool IsContainedWithinCompositeActivity() => false;

        public bool TryGetActivityInput(string key, out object? value)
        {
            value = null;
            return false;
        }

        public bool TryGetWorkflowInput(string key, out object? value)
        {
            value = null;
            return false;
        }

        public object? GetVariableValueOrDefault(string variableName) => GetVariable(variableName)?.Get(this);

        public string GetCorrelationId() => string.Empty;

        public string GetWorkfowDefinitionId() => string.Empty;

        public string GetWorkfowDefinitionVersionId() => string.Empty;

        public int GetWorkfowDefinitionVersion() => 0;

        public string GetWorkfowInstanceId() => string.Empty;

        public object? GetRequiredService(Type type) => throw new InvalidOperationException($"No service registered for '{type}'.");

        public IMemoryBlock GetBlock(IMemoryBlockReference blockReference) => Memory.Declare(blockReference);

        public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block) => Memory.TryGetBlock(blockReference.Id, out block);

        public T? Get<T>(IMemoryBlockReference blockReference) => (T?)GetBlock(blockReference).Value;

        public void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null)
        {
            var block = Memory.Declare(blockReference);
            block.Value = value;
            configure?.Invoke(block);
        }

        public IVariable? GetVariable(string name, bool localScopeOnly = false) => variablesByName.GetValueOrDefault(name);

        public IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null)
        {
            var variable = new Variable<T>(name, value!);
            variablesByName[name] = variable;
            Set(variable, value, configure);
            return variable;
        }

        public IEnumerable<IVariable> EnumerateVariablesInScope() => variablesByName.Values;
    }

    private sealed class TestMemoryRegister : IMemoryRegister
    {
        public IDictionary<string, IMemoryBlock> Blocks { get; } = new Dictionary<string, IMemoryBlock>();
    }

    private sealed class TestExpression(string type, object? value) : IExpression
    {
        public string Type { get; set; } = type;

        public object? Value { get; set; } = value;

        public TValue GetValue<TValue>() => (TValue)Value!;
    }
}
