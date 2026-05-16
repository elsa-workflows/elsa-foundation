using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Activities.Core.Contracts
{
    public interface IActivityCompletionHandler
    {
        ValueTask CompleteActivityAsync(IActivityExecutionContext context);

        ValueTask CompleteActivityAsync(IActivityExecutionContext context, object result);

        ValueTask CompleteActivityAsync(IActivityExecutionContext context, IEnumerable<string> outcomes);

        ValueTask CompleteActivityAsync(IActivityExecutionContext context, IEnumerable<string> outcomes, object result);
    }
}
