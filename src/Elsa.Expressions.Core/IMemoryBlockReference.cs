namespace Elsa.Expressions.Core
{
    public interface IMemoryBlockReference
    {
        /// <summary>
        /// The ID of the memory block.
        /// </summary>
        string Id { get; set; }

        /// <summary>
        /// Declares the memory block.
        /// </summary>
        IMemoryBlock Declare();

        /// <summary>
        /// Returns true if the memory block is defined in the specified memory register.
        /// </summary>
        public bool IsDefined(IMemoryRegister register) => register.HasBlock(Id);

        /// <summary>
        /// Returns the value of the memory block.
        /// </summary>
        public object? Get(IMemoryRegister memoryRegister) => GetBlock(memoryRegister).Value;

        /// <summary>
        /// Returns the value of the memory block.
        /// </summary>
        T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context);

        /// <summary>
        /// Returns the value of the memory block.
        /// </summary>
        public object? Get(IExpressionExecutionContext context) => context.Get(this);

        /// <summary>
        /// Returns the value of the memory block.
        /// </summary>
        T? Get<T>(IExpressionExecutionContext context);

        /// <summary>
        /// Returns the value of the memory block.
        /// </summary>
        public bool TryGet(IExpressionExecutionContext context, out object? value) => context.TryGet(this, out value);

        /// <summary>
        /// Sets the value of the memory block.
        /// </summary>
        public void Set(IMemoryRegister memoryRegister, object? value, Action<IMemoryBlock>? configure = null)
        {
            var block = GetBlock(memoryRegister);
            block.Value = value;
            configure?.Invoke(block);
        }

        /// <summary>
        /// Sets the value of the memory block.
        /// </summary>
        public void Set(IExpressionExecutionContext context, object? value, Action<IMemoryBlock>? configure = null) 
            => context.Set(this, value, configure);

        /// <summary>
        /// Returns the <see cref="MemoryBlock"/> pointed to by the specified memory block reference.
        /// </summary>
        public IMemoryBlock GetBlock(IMemoryRegister memoryRegister) => memoryRegister.TryGetBlock(Id, out var location) 
            ? location 
            : memoryRegister.Declare(this);
    }
}
