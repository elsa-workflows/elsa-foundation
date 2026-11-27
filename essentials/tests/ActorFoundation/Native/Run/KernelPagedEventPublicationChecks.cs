using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    internal static async Task<int> RunPagedEventPublicationAsync(string provider, bool inBatch, bool physicalPages = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        var scope = ContractScopes.Bind("paged-events");
        var model = new PagedEventActor();
        var directory = Directory.CreateTempSubdirectory("paged-event-publication");
        try
        {
            var (open, ingress, reads, owner, _, restart) = await OpenAsync(model, provider,
                Path.Combine(directory.FullName, "store.db"), inBatch, token, publicStarts: true, pagedPublicEvents: !physicalPages);
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
            if (physicalPages)
            {
                await VerifyPhysicalEventPagesAsync(open, scope.Tenant, publicationId, token);
                await using var restarted = await restart(token);
                await UntilAsync(DeliveredAsync);
                Check(true, "the fresh hosted owner consumed exactly the physical page deliveries");
                return 0;
            }

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
                if (resumed is SqliteEmbeddedHostedService { LastError: { } fault })
                    throw new InvalidOperationException("The SQLite publication owner reported a fault.", fault);
                return await DeliveredAsync();
            });
            var settled = await ingress.SendAsync(scope, target, publicationId, "ignored", null, options, token);
            Check(settled is { Outcome: Contract.AdmissionOutcome.Duplicate, Publication.Status: Contract.PublicationStatus.Matched } &&
                settled.Publication.Incarnation == accepted.Publication!.Incarnation,
                "every large delivery lands and the durable receipt settles with its original incarnation");
            return 0;

            async Task<bool> DeliveredAsync()
            {
                foreach (var actor in actors)
                {
                    var view = await reads.GetAsync(scope, actor, token);
                    if (view?.Park is not null) throw new InvalidOperationException(view.Header.Fault);
                    if (view?.Header.Status != Contract.ActorStatus.Finished) return false;
                    if (view.Header.TypeData != publicationId) throw new InvalidOperationException("The recipient saw a different publication.");
                }
                return true;
            }
        }
        finally { directory.Delete(recursive: true); }

        async Task UntilAsync(Func<Task<bool>> predicate)
        {
            while (!await predicate()) await Task.Delay(20, token);
        }
    }

    private static async Task VerifyPhysicalEventPagesAsync(Func<KernelDb> open, string tenant, string eventId, CancellationToken token)
    {
        var qualified = tenant + "_" + PagedEventActor.Key;
        var occurrence = FoundationRootCreator.OperationDigest(eventId);
        var incarnation = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var keepUntil = now + (long)TimeSpan.FromMinutes(5).TotalMilliseconds;
        var ticket = RootPublicEventTicket.Outside(new(qualified, eventId, null, keepUntil), incarnation, occurrence, 1, keepUntil);
        const long trialEpoch = 11;
        await using (var db = open())
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            // Use the forthcoming profile's state shape for this isolated storage witness. The normal product provisioner
            // and the owned ticket use this same definition once wired to the new protocol.
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE TABLE "{RootPublicEventDispatch.Table}" ({RootPublicEventDispatch.Columns(db.Database.IsSqlite())})
                """, token);
            var bind = await BindPageSourceAsync(db);
            await RootPublicEventPublications.InsertAsync(db, tenant, qualified, eventId, occurrence, incarnation, occurrence,
                PagedEventActor.Payload, now, keepUntil, keepUntil, sql => RootPublicEventStarts.Bind(sql, bind), token);
            await RootPublicEventDispatch.AddAsync(db, occurrence, ticket.Id, ticket.Target.Id, ticket.Target.Partition, bind, token);
            await ticket.EnqueueAsync(db, bind, token);
            await tx.CommitAsync(token);
        }
        long firstSequence;
        await using (var db = open())
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var bind = await BindPageSourceAsync(db);
            var source = await ReadTicketAsync(db);
            var decoded = RootPublicEventTicket.Decode(source);
            Check(decoded == ticket && decoded.ToMessage().HasFencedEventStep, "the physical public ticket decodes as exact fenced work");
            firstSequence = source.Seq;
            Check(await ticket.ChangeAsync(db, firstSequence, 0, trialEpoch, RootPublicEventTicket.Change.Reserve, bind, token) &&
                await ticket.ChangeAsync(db, firstSequence, 0, trialEpoch, RootPublicEventTicket.Change.RecordFailure, bind, token) &&
                !await ticket.ChangeAsync(db, firstSequence, 0, trialEpoch, RootPublicEventTicket.Change.RecordFailure, bind, token),
                "charging a definitively failed trial clears its reservation exactly once");
            await tx.CommitAsync(token);
        }
        var rolledBack = await PageAsync(commit: false);
        Check(await CountDeliveriesAsync() == 0, "rolling back a physical page leaves no split delivery");
        var first = await PageAsync();
        Check(first == rolledBack && first is { Complete: false, Through: not null } && await CountDeliveriesAsync() == 1,
            "rollback preserved the initial cursor and the first commit wrote one oversized delivery");
        await using (var db = open())
        {
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var bind = await BindPageSourceAsync(db);
            var successor = await ReadTicketAsync(db);
            Check(successor.Seq > firstSequence && successor.Attempts == 0 && successor.Seen == 0 && successor.Trial == 0 &&
                !await ticket.ChangeAsync(db, firstSequence, 0, trialEpoch, RootPublicEventTicket.Change.Reserve, bind, token),
                "a same-key successor has a new sequence and an old reservation cannot claim it");
        }
        Check(await PageAsync(replay: true) == first && await CountDeliveriesAsync() == 1,
            "an obsolete cursor cannot advance committed state or enqueue an occurrence twice");
        var second = await PageAsync();
        Check(second is { Complete: false, Through: not null } && second.Through != first.Through && await CountDeliveriesAsync() == 2,
            "the physical keyset seek resumes at the next waiter and stops at the byte allowance");
        var third = await PageAsync();
        Check(third is { Complete: true, Through: not null } && third.Through != second.Through && await CountDeliveriesAsync() == 3,
            "the short final physical page lands one oversized delivery and proves exhaustion");
        await using (var db = open())
        {
            var status = await db.Database.SqlQuery<int>($"""
                SELECT "Status" AS "Value" FROM "FoundationPublicEventDispatch" WHERE "Occurrence" = {occurrence}
                """).SingleAsync(token);
            Check(status == (int)Contract.PublicationStatus.Matched && (await ReadTicketAsync(db)).Done,
                "the publication records Matched and settles its ticket with the final delivery");
            // Restore this private fixture's installed schema before opening its existing host again.
            await db.Database.ExecuteSqlRawAsync("DROP TABLE \"FoundationPublicEventDispatch\"", token);
        }

        async Task<RootEventAdmission.LiveMatchPage> PageAsync(bool commit = true, bool replay = false)
        {
            await using var db = open();
            await using var tx = await db.Database.BeginTransactionAsync(token);
            var bind = await BindPageSourceAsync(db);
            var source = await ReadTicketAsync(db);
            if (!replay && !await ticket.ChangeAsync(db, source.Seq, source.Attempts, trialEpoch, RootPublicEventTicket.Change.Reserve, bind, token))
                throw new InvalidOperationException("The pending ticket could not reserve the page.");
            var publication = await RootPublicEventDispatch.ReadPendingAsync(db, occurrence, ticket.Id, ticket.Target.Id, ticket.Target.Partition,
                bind, token) ?? throw new InvalidOperationException("The exact publication stopped before its last page.");
            if (replay) publication = publication with { Position = RootPublicEventDispatch.Cursor.Initial };
            var page = await RootEventAdmission.MatchLivePageAsync(db, publication.Tenant, publication.Key, publication.EventId,
                publication.Occurrence, publication.Payload, publication.KeepUntil, publication.Position.LiveThrough, bind, token);
            var changed = await RootPublicEventDispatch.ChangeAsync(db, publication, new(page.Through, page.Complete, null),
                page.Complete ? Contract.PublicationStatus.Matched : Contract.PublicationStatus.Pending, bind, token);
            if (changed == replay) throw new InvalidOperationException("The durable cursor update disagreed with the exact source position.");
            var mutation = page.Complete ? RootPublicEventTicket.Change.Settle : RootPublicEventTicket.Change.Advance;
            if (!replay && !await ticket.ChangeAsync(db, source.Seq, source.Attempts, trialEpoch, mutation, bind, token))
                throw new InvalidOperationException("The reserved ticket could not move with the page.");
            if (commit) await tx.CommitAsync(token);
            return page;
        }

        Task<MessageEntity> ReadTicketAsync(KernelDb db) => db.Messages.AsNoTracking()
            .SingleAsync(message => message.Id == ticket.Id && message.InboxScope == RootPublicEventTicket.Scope, token);

        async Task<Func<FormattableString, FormattableString>?> BindPageSourceAsync(KernelDb db)
        {
            if (!db.Database.IsNpgsql()) return null;
            var marker = await PostgresRootAdmission.PinRootCommitNamespaceAsync(db, writesJournal: false, token);
            var bind = await PostgresRootAdmission.PinAuthoredEventSourceAsync(db, marker, token);
            await RootEventAdmission.LockPostgresKeysAsync(db, [qualified], token);
            return bind;
        }

        async Task<int> CountDeliveriesAsync()
        {
            await using var db = open();
            return await db.Messages.CountAsync(message => message.Kind == Kinds.Event, token);
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
