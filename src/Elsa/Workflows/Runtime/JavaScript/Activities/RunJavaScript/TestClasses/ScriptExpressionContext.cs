using Elsa.Expressions.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.JavaScript.Activities.RunJavaScript.TestClasses;

internal sealed class ScriptExpressionContext(IServiceProvider serviceProvider)
    : IExpressionExecutionContext
{
    public IMemoryRegister Memory => null!;

    public IExpressionExecutionContext? ParentContext { get => null; set { } }

    public CancellationToken CancellationToken => CancellationToken.None;

    public IEnumerable<IVariable> EnumerateVariablesInScope()
    {
        yield break;
    }

    public T? Get<T>(IMemoryBlockReference blockReference)
    {
        return (T)blockReference.Declare().Value!;
    }

    public IMemoryBlock GetBlock(IMemoryBlockReference blockReference)
    {
        return new Block(blockReference.Declare().Value);
    }

    public string GetCorrelationId()
    {
        return string.Empty;
    }

    public object? GetRequiredService(Type type)
    {
        return serviceProvider.GetRequiredService(type);
    }

    public IVariable? GetVariable(string name, bool localScopeOnly = false)
    {
        return null;
    }

    public object? GetVariableValueOrDefault(string variableName)
    {
        return null;
    }

    public string GetWorkflowDefinitionId()
    {
        return string.Empty;
    }

    public int GetWorkflowDefinitionVersion()
    {
        return 0;
    }

    public string GetWorkflowDefinitionVersionId()
    {
        return string.Empty;
    }

    public string GetWorkflowInstanceId()
    {
        return string.Empty;
    }

    public bool IsContainedWithinCompositeActivity()
    {
        return false;
    }

    public void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null)
    {
        blockReference.Set(this, value);
    }

    public IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null)
    {
        return null!;
    }

    public bool TryGetActivityInput(string key, out object? value)
    {
        value = null;
        return false;
    }

    public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block)
    {
        block = null!;
        return false;
    }

    public bool TryGetWorkflowInput(string key, out object? value)
    {
        value = null;
        return false;
    }
}