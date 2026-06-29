using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// A bare <see cref="IActivityExecutionContext"/> that is deliberately NOT an
/// <see cref="Elsa.Workflows.Runtime.Core.Contracts.IRuntimeActivityExecutionContext"/>. Used to cover the
/// guard in control-leaf activities that require the Elsa runtime activity execution context.
/// </summary>
internal sealed class NonRuntimeActivityExecutionContext : IActivityExecutionContext
{
    public IExpressionExecutionContext ExpressionExecutionContext => throw new NotSupportedException();
    public IActivity Activity => throw new NotSupportedException();
    public IActivityExecutionContext ParentActivityExecutionContext => throw new NotSupportedException();
    public CancellationToken CancellationToken => CancellationToken.None;

    public TService GetRequiredService<TService>() where TService : notnull => throw new NotSupportedException();
    public T? Get<T>(InputArgument<T>? input) => default;
    public void Set<T>(OutputArgument<T>? output, T? value, string? outputName = null) => throw new NotSupportedException();
    public IAsyncEnumerable<ActivityOutputs> GetActivityOutputs() => throw new NotSupportedException();
    public void SetOutcomes(string[] outcomes) => throw new NotSupportedException();
    public IEnumerable<string> GetOutcomes() => throw new NotSupportedException();
    public void CreateBookmark(ActivityBookmarkRequest request) => throw new NotSupportedException();
    public IReadOnlyCollection<ActivityBookmarkRequest> GetBookmarkRequests() => throw new NotSupportedException();
}
