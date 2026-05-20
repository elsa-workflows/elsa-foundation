using Elsa.Primitives.Extensions;

namespace Elsa.Expressions.Core.Contracts
{
    public interface IExpressionExecutionContext
    {
        /// <summary>
        /// A shared register of computer memory. 
        /// </summary>
        IMemoryRegister Memory { get; }

        bool IsContainedWithinCompositeActivity();

        bool TryGetActivityInput(string key, out object? value);

        bool TryGetWorkflowInput(string key, out object? value);

        object? GetVariableValueOrDefault(string variableName);

        string GetCorrelationId();

        string GetWorkfowDefinitionId();

        string GetWorkfowDefinitionVersionId();

        int GetWorkfowDefinitionVersion();

        string GetWorkfowInstanceId();

        object? GetRequiredService(Type type);

        TService GetRequiredService<TService>() where TService : notnull
            => (TService)GetRequiredService(typeof(TService))!;

        /// <summary>
        /// Provides access to the parent <see cref="IExpressionExecutionContext"/>, if there is any.
        /// </summary>
        IExpressionExecutionContext? ParentContext { get; set; }

        /// <summary>
        /// A cancellation token.
        /// </summary>
        CancellationToken CancellationToken { get; }

        /// <summary>
        /// Returns the <see cref="IMemoryBlock"/> pointed to by the specified memory block reference.
        /// </summary>
        IMemoryBlock GetBlock(Func<IMemoryBlockReference> blockReference) => GetBlock(blockReference());

        /// <summary>
        /// Returns the <see cref="IMemoryBlock"/> pointed to by the specified memory block reference.
        /// </summary>
        IMemoryBlock GetBlock(IMemoryBlockReference blockReference);

        /// <summary>
        /// Returns the <see cref="IMemoryBlock"/> pointed to by the specified memory block reference.
        /// </summary>
        bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block);

        /// <summary>
        /// Returns the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        object? Get(Func<IMemoryBlockReference> blockReference) => Get(blockReference());

        /// <summary>
        /// Returns the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        object? Get(IMemoryBlockReference blockReference) => GetBlock(blockReference).Value;

        /// <summary>
        /// Returns the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        bool TryGet(IMemoryBlockReference blockReference, out object? value)
        {
            if (TryGetBlock(blockReference, out var block))
            {
                value = block.Value;
                return true;
            }

            value = null;
            return false;
        }

        /// <summary>
        /// Returns the value of the memory block pointed to by the specified memory block reference. 
        /// </summary>
        T? Get<T>(Func<IMemoryBlockReference> blockReference) => Get<T>(blockReference());

        /// <summary>
        /// Returns the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        T? Get<T>(IMemoryBlockReference blockReference);

        /// <summary>
        /// Sets the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        void Set(Func<IMemoryBlockReference> blockReference, object? value, Action<IMemoryBlock>? configure = null) => Set(blockReference(), value, configure);

        /// <summary>
        /// Sets the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null);

        IVariable? GetVariable(string name, bool localScopeOnly = false);

        IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null);

        IEnumerable<IVariable> EnumerateVariablesInScope();

        /// <summary>
        /// Returns the value of the specified variable.
        /// </summary>
        object GetVariableInScope(string variableName)
        {
            var variable = GetVariable(variableName);
            var value = variable?.Get(this);

            return value.ConvertIEnumerableToArray();
        }

        /// <summary>
        /// Gets all variables names in scope.
        /// </summary>
        IEnumerable<string> GetVariableNamesInScope() =>
            EnumerateVariablesInScope()
                .Select(x => x.Name)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct();
    }
}
