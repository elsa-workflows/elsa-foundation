using Elsa.Expressions.Core.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Activities.RunJavaScript.TestClasses
{
    internal sealed class BlockReference(object? value) : IMemoryBlockReference
    {
        private readonly Block block = new(value);
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public IMemoryBlock Declare()
        {
            return block;
        }

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context)
        {
            return (T)Convert.ChangeType($"{block.Value}", typeof(T));
        }

        public T? Get<T>(IExpressionExecutionContext context)
        {
            return (T)Convert.ChangeType($"{block.Value}", typeof(T));
        }
    }

    internal sealed class Block(object? value) : IMemoryBlock
    {
        public object? Value { get; set; } = value;
        public object? Metadata { get; set; }
    }
}
