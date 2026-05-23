using Elsa.Expressions.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.CompilerServices;

namespace Elsa.Workflows.Runtime.JavaScript.Activities.RunJavaScript.TestClasses
{
    internal sealed class ScriptExecutionContext(IServiceProvider serviceProvider)
        : IActivityExecutionContext
    {
        private ActivityOutput<object?>? activityOutput;
        private string[] outcomes = [];

        public IExpressionExecutionContext ExpressionExecutionContext { get; } = new ScriptExpressionContext(serviceProvider);

        public IActivity Activity => throw new NotImplementedException();

        public IActivityExecutionContext ParentActivityExecutionContext => throw new NotImplementedException();

        public CancellationToken CancellationToken => CancellationToken.None;

        public T? Get<T>(InputArgument<T>? input)
        {
            return (T)input!.MemoryBlockReference().Declare().Value!;
        }

        public T? Get<T>(ActivityOutput<T>? _)
        {
            return (T)activityOutput?.MemoryBlockReference().Declare().Value!;
        }

        public IAsyncEnumerable<ActivityOutputs> GetActivityOutputs()
        {
            throw new NotImplementedException();
        }

        public IEnumerable<string> GetOutcomes() => outcomes.AsEnumerable();

        public TService GetRequiredService<TService>()
            where TService : notnull
        {
            return serviceProvider.GetRequiredService<TService>();
        }

        public void Set<T>(ActivityOutput<T>? output, T? value, [CallerArgumentExpression("output")] string? outputName = null)
        {
            activityOutput = new ActivityOutput<object?>(
                new BlockReference(value)
            );
        }

        public void SetOutcomes(string[] outcomes)
        {
            this.outcomes = outcomes;
        }
    }
}
