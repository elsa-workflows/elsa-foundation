namespace Elsa.Expressions.Core
{
    public interface IExpressionExecutionContext
    {
        /// <summary>
        /// A shared register of computer memory. 
        /// </summary>
        IMemoryRegister Memory { get; }

        /// <summary>
        /// A dictionary of transient properties.
        /// </summary>
        IDictionary<object, object> TransientProperties { get; set; }

        bool TryGetActivityInput(string key, out object? value);

        bool TryGetWorkflowInput(string key, out object? value);

        object? GetVariableValueOrDefault(string variableName);

        string GetCorrelationId();

        string GetWorkfowDefinitionId();

        string GetWorkfowDefinitionVersionId();

        int GetWorkfowDefinitionVersion();

        string GetWorkfowInstanceId();

        object? GetRequiredService(Type type);

        public TService GetRequiredService<TService>() where TService : notnull
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
        public IMemoryBlock GetBlock(Func<IMemoryBlockReference> blockReference) => GetBlock(blockReference());

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
        public object? Get(Func<IMemoryBlockReference> blockReference) => Get(blockReference());

        /// <summary>
        /// Returns the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        public object? Get(IMemoryBlockReference blockReference) => GetBlock(blockReference).Value;

        /// <summary>
        /// Returns the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        public bool TryGet(IMemoryBlockReference blockReference, out object? value)
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
        public T? Get<T>(Func<IMemoryBlockReference> blockReference) => Get<T>(blockReference());

        /// <summary>
        /// Returns the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        T? Get<T>(IMemoryBlockReference blockReference);

        /// <summary>
        /// Sets the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        public void Set(Func<IMemoryBlockReference> blockReference, object? value, Action<IMemoryBlock>? configure = null) => Set(blockReference(), value, configure);

        /// <summary>
        /// Sets the value of the memory block pointed to by the specified memory block reference.
        /// </summary>
        void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null);
    }
}
