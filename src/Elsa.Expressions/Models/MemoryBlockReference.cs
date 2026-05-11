using Elsa.Expressions.Core;
using Elsa.Serialization.Core;

namespace Elsa.Expressions.Models
{
    /// <summary>
    /// A base class for types that represent a reference to a block of memory. 
    /// </summary>
    public class MemoryBlockReference : IMemoryBlockReference
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryBlockReference"/> class.
        /// </summary>
        public MemoryBlockReference()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryBlockReference"/> class.
        /// </summary>
        public MemoryBlockReference(string id) => Id = id;

        /// <summary>
        /// The ID of the memory block.
        /// </summary>
        public string Id { get; set; } = null!;

        /// <summary>
        /// Declares the memory block.
        /// </summary>
        public virtual IMemoryBlock Declare() => new MemoryBlock();

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
        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context)
        {
            var result = Get(memoryRegister);
            var objectConverter = context.GetRequiredService<IObjectConverter>();
            return objectConverter.ConvertTo<T>(result);
        }

        /// <summary>
        /// Returns the value of the memory block.
        /// </summary>
        public object? Get(IExpressionExecutionContext context) => context.Get(this);

        /// <summary>
        /// Returns the value of the memory block.
        /// </summary>
        public T? Get<T>(IExpressionExecutionContext context)
        {
            var result = Get(context);
            var objectConverter = context.GetRequiredService<IObjectConverter>();
            return objectConverter.ConvertTo<T>(result);
        }

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
        public void Set(IExpressionExecutionContext context, object? value, Action<IMemoryBlock>? configure = null) => context.Set(this, value, configure);

        /// <summary>
        /// Returns the <see cref="MemoryBlock"/> pointed to by the specified memory block reference.
        /// </summary>
        public IMemoryBlock GetBlock(IMemoryRegister memoryRegister) => memoryRegister.TryGetBlock(Id, out var location)
            ? location
            : memoryRegister.Declare(this);
    }

    /// <summary>
    /// A base class for types that represent a reference to a block of memory.
    /// </summary>
    /// <typeparam name="T">The type of the memory block.</typeparam>
    public class MemoryBlockReference<T> : MemoryBlockReference
    {
    }
}
