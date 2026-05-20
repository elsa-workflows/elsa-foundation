using Elsa.Activities.Runtime.Core.Contracts;

namespace Elsa.Expressions.JavaScript.Activities.RunJavaScript.TestClasses
{
    internal sealed class ActivityCompletionHandler : IActivityCompletionHandler
    {
        public ValueTask CompleteActivityAsync(IActivityExecutionContext context)
        {
            return new();
        }

        public ValueTask CompleteActivityAsync(IActivityExecutionContext context, object result)
        {
            return new();
        }

        public ValueTask CompleteActivityAsync(IActivityExecutionContext context, IEnumerable<string> outcomes)
        {
            return new();
        }

        public ValueTask CompleteActivityAsync(IActivityExecutionContext context, IEnumerable<string> outcomes, object result)
        {
            return new();
        }
    }
}
