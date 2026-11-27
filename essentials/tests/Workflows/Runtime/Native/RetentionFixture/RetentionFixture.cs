using Kernel.Foundation.Contracts;
using Kernel.WorkflowActor;

namespace Elsa.Workflows.Runtime.Native.RetentionFixture;

/// <summary>Runs the actual workflow retention pass from provider qualification fixtures.</summary>
public sealed class RetentionFixture(IActorReads reads, Func<ActorHeader, TimeSpan?> window,
    Func<BoundScope, ActorRef, string, CancellationToken, ValueTask<Admission>> admitDelete)
{
    private readonly RetentionPass _pass = new(reads, WorkflowActor.ActorType, window, admitDelete);

    public async ValueTask<(PageToken? Next, int Examined, int Accepted, int Duplicates)> RunAsync(
        BoundScope scope, PageToken? from, CancellationToken cancellationToken)
    {
        var page = await _pass.RunAsync(scope, from, cancellationToken);
        return (page.Next, page.Examined, page.Accepted, page.Duplicates);
    }
}
