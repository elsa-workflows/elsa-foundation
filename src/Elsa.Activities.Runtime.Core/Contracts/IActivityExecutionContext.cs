using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using System.Runtime.CompilerServices;

namespace Elsa.Activities.Runtime.Core.Contracts
{
    public interface IActivityExecutionContext
    {        
        TService GetRequiredService<TService>()
            where TService : notnull;

        IExpressionExecutionContext ExpressionExecutionContext { get; }

        IActivity Activity { get; }

        IActivityExecutionContext ParentActivityExecutionContext { get; }

        T? Get<T>(ActivityInput<T>? input);

        void Set<T>(ActivityOutput<T>? output, T? value, [CallerArgumentExpression(nameof(output))] string? outputName = null);

        IAsyncEnumerable<ActivityOutputs> GetActivityOutputs();

        CancellationToken CancellationToken { get; }

        void SetOutcomes(string[] outcomes);

        IEnumerable<string> GetOutcomes();
    }
}
