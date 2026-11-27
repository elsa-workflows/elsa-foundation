using Elsa.ActorFoundation.TestSupport;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    internal static async Task<int> RunPagedEventPublicationAsync(string provider, bool inBatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var scope = ContractScopes.Bind("paged-events");
        var model = new PagedEventActor();
        var directory = Directory.CreateTempSubdirectory("paged-event-publication");
        try
        {
            var (open, ingress, reads, owner, _, restart) = await OpenAsync(model, provider,
                Path.Combine(directory.FullName, "store.db"), inBatch, token, publicStarts: true);
            await using var initialOwner = owner;
            var actors = new List<Contract.ActorRef>();
            for (var index = 0; index < 3; index++)
            {
                var id = $"waiter-{index}";
                var start = await ingress.SendAsync(scope, new Contract.Target.Start(new(model.Type, "waiter", id)),
                    id, Contract.Kinds.Start, cancellationToken: token);
                if (start is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } actor })
                    throw new InvalidOperationException($"The recipient did not start ({start.Outcome}, {start.Reason}).");
                actors.Add(actor);
            }
            await UntilAsync(async () =>
            {
                await using var db = open();
                return await db.Subscriptions.CountAsync(row => row.Key == scope.Tenant + "_" + PagedEventActor.Key, token) == actors.Count;
            });
            await initialOwner.DisposeAsync();

            var target = new Contract.Target.Event(PagedEventActor.Key);
            const string publicationId = "large-publication";
            var options = new Contract.AddOptions(KeepFor: TimeSpan.FromMinutes(5));
            var accepted = await ingress.SendAsync(scope, target, publicationId, "ignored", PagedEventActor.Payload, options, token);
            Check(accepted is { Outcome: Contract.AdmissionOutcome.Accepted, Publication.Status: Contract.PublicationStatus.Pending },
                $"a one-way event with a payload above the page budget is durably accepted while its owner is stopped ({accepted.Outcome}, {accepted.Reason})");
            var replay = await ingress.SendAsync(scope, target, publicationId, "also ignored", "replacement", options, token);
            Check(replay.Outcome == Contract.AdmissionOutcome.Duplicate && replay.Publication == accepted.Publication,
                "a duplicate before matching sees the original pending publication");

            await using var resumed = await restart(token);
            await UntilAsync(async () =>
            {
                foreach (var actor in actors)
                {
                    var view = await reads.GetAsync(scope, actor, token);
                    if (view?.Park is not null) throw new InvalidOperationException(view.Header.Fault);
                    if (view?.Header.Status != Contract.ActorStatus.Finished) return false;
                    if (view.Header.TypeData != publicationId) throw new InvalidOperationException("The recipient saw a different publication.");
                }
                return true;
            });
            var settled = await ingress.SendAsync(scope, target, publicationId, "ignored", null, options, token);
            Check(settled is { Outcome: Contract.AdmissionOutcome.Duplicate, Publication.Status: Contract.PublicationStatus.Matched } &&
                settled.Publication.Incarnation == accepted.Publication!.Incarnation,
                "every large delivery lands and the durable receipt settles with its original incarnation");
            return 0;
        }
        finally { directory.Delete(recursive: true); }

        async Task UntilAsync(Func<Task<bool>> predicate)
        {
            while (!await predicate()) await Task.Delay(20, token);
        }
    }

    private sealed class PagedEventActor : Contract.IActorType
    {
        internal const string Key = "wide";
        internal static string Payload { get; } = new('"', 9 * 1024 * 1024);
        public string Type => "paged-event-probe";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start) turn.Subscribe(Key, "wide-wait");
                if (message.Event is not { } published) continue;
                if (published.Key != Key || published.Wait != "wide-wait" || message.Payload != Payload)
                    throw new InvalidOperationException("The event did not preserve the registered wait and payload.");
                turn.SetHeader(published.Id, turn.Header.Labels);
                turn.End(Contract.ActorStatus.Finished);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
