namespace Elsa.Workflows.Runtime.Core.Contracts
{
    public interface IActivityCompletionHandler
    {
        ValueTask CompleteActivityAsync(IActivityExecutionContext context);

        ValueTask CompleteActivityAsync(IActivityExecutionContext context, object result);

        ValueTask CompleteActivityAsync(IActivityExecutionContext context, IEnumerable<string> outcomes);

        ValueTask CompleteActivityAsync(IActivityExecutionContext context, IEnumerable<string> outcomes, object result);
    }
}
