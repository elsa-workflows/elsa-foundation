using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Activities.Runtime.Core.Models
{
    public class OutputArgument : Argument
    {
        public OutputArgument(IMemoryBlockReference memoryBlockReference) : base(memoryBlockReference)
        {
        }

        public OutputArgument(Func<IMemoryBlockReference> memoryBlockReference) : base(memoryBlockReference)
        {
        }
    }

    public class OutputArgument<T> : OutputArgument
    {
        public OutputArgument(IMemoryBlockReference memoryBlockReference) : base(memoryBlockReference)
        {
        }

        public OutputArgument(Func<IMemoryBlockReference> memoryBlockReference) : base(memoryBlockReference)
        {
        }
    }
}
