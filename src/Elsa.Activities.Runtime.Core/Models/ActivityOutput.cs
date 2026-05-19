using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;

namespace Elsa.Activities.Runtime.Core.Models
{
    public class ActivityOutput : Argument
    {
        public ActivityOutput(IMemoryBlockReference memoryBlockReference) : base(memoryBlockReference)
        {
        }

        public ActivityOutput(Func<IMemoryBlockReference> memoryBlockReference) : base(memoryBlockReference)
        {
        }
    }

    public class ActivityOutput<T> : ActivityOutput
    {
        public ActivityOutput(IMemoryBlockReference memoryBlockReference) : base(memoryBlockReference)
        {
        }

        public ActivityOutput(Func<IMemoryBlockReference> memoryBlockReference) : base(memoryBlockReference)
        {
        }
    }
}
