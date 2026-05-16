using Elsa.Activities.Core.Models;
using Elsa.Expressions.Core.Contracts;
using System.Runtime.CompilerServices;

namespace Elsa.Activities.Core.Contracts
{
    public interface IActivityExecutionContext
    {
        /// <summary>
        /// The key used to store the <see cref="ActivityExecutionContext"/> in the <see cref="ExpressionExecutionContext.TransientProperties"/> dictionary.
        /// </summary>
        public static readonly object ContextKey = new();

        TService GetRequiredService<TService>();

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
