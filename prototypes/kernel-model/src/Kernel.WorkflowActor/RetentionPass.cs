using Kernel.Foundation.Contracts;

namespace Kernel.WorkflowActor;

internal sealed record RetentionPage(PageToken? Next, int Examined, int Accepted, int Duplicates);

/// <summary>Proposes retention deletes from one configured read page. The owner decides eligibility when it delivers each command.</summary>
internal sealed class RetentionPass(IActorReads reads, string actorType, Func<ActorHeader, TimeSpan?> retentionWindow,
    Func<BoundScope, ActorRef, string, CancellationToken, ValueTask<Admission>> admitDelete)
{
    private readonly ActorQuery _query = new(Type: actorType, Parked: false);

    internal async ValueTask<RetentionPage> RunAsync(BoundScope scope, PageToken? from = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        var page = await reads.ListAsync(scope, _query, from, cancellationToken);
        if (page.Next is { } next && next == from)
            throw new InvalidOperationException("The retention list returned the same continuation.");

        var accepted = 0;
        var duplicates = 0;
        foreach (var view in page.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var header = view.Header;
            if (header.Tenant != scope.Tenant || header.Type != actorType || view.Park is not null ||
                header.Status is not (ActorStatus.Finished or ActorStatus.Cancelled or ActorStatus.Faulted) ||
                header.EndedAt is not { } endedAt || view.Selection is not { Length: > 0 } selection ||
                retentionWindow(header) is not { Ticks: >= 0 } window ||
                (decimal)page.AsOf - endedAt < (long)window.TotalMilliseconds)
                continue;

            var admission = await admitDelete(scope, header.Ref, selection, cancellationToken);
            switch (admission)
            {
                case { Outcome: AdmissionOutcome.Accepted, Actor: var exact } when exact == header.Ref:
                    accepted++;
                    break;
                case { Outcome: AdmissionOutcome.Duplicate, Actor: var exact } when exact == header.Ref:
                    duplicates++;
                    break;
                case { Outcome: AdmissionOutcome.Refused, Reason: RefusalReason.StaleSelection or RefusalReason.TargetMismatch }:
                    // A new sweep can reconsider a changed actor from its current projected header.
                    break;
                default:
                    throw new InvalidOperationException($"Retention admission did not settle the candidate ({admission.Outcome}, {admission.Reason}).");
            }
        }

        // An interrupted pass never returns a continuation. Its successful admissions can be replayed.
        cancellationToken.ThrowIfCancellationRequested();
        return new(page.Next, page.Items.Count, accepted, duplicates);
    }
}
