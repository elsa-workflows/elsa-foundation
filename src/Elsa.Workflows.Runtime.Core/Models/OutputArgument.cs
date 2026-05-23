using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models
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

    public class ActivityOutput<T> : OutputArgument
    {
        public ActivityOutput(IMemoryBlockReference memoryBlockReference) : base(memoryBlockReference)
        {
        }

        public ActivityOutput(Func<IMemoryBlockReference> memoryBlockReference) : base(memoryBlockReference)
        {
        }
    }
}
