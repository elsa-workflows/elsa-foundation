using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using System.Runtime.CompilerServices;

namespace Elsa.Workflows.Runtime.Core.Contracts
{
    public interface IActivityExecutionContext
    {
        TService GetRequiredService<TService>()
            where TService : notnull;

        IExpressionExecutionContext ExpressionExecutionContext { get; }

        IActivity Activity { get; }

        IActivityExecutionContext ParentActivityExecutionContext { get; }

        T? Get<T>(InputArgument<T>? input);

        void Set<T>(ActivityOutput<T>? output, T? value, [CallerArgumentExpression(nameof(output))] string? outputName = null);

        IAsyncEnumerable<ActivityOutputs> GetActivityOutputs();

        CancellationToken CancellationToken { get; }

        void SetOutcomes(string[] outcomes);

        IEnumerable<string> GetOutcomes();
    }
}
