using System.Diagnostics;
using System.Globalization;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    private const int Sb7Actors = 100;
    private const int Sb7WaitsPerActor = 1_000;
    private const int Sb7Waits = Sb7Actors * Sb7WaitsPerActor;
    private const int Sb7Page = 170;
    private const int Sb7FullPages = 588;
    private const int Sb7FinalPage = 40;
    private const string Sb7Key = "broadcast";
    private const string Sb7Outside = "outside-broadcast";
    private const string Sb7Trigger = "begin-broadcast";
    private const string Sb7Payload = "small broadcast";
    private static readonly TimeSpan Sb7Retention = TimeSpan.FromMinutes(30);
    private static readonly string[] Sb7Names = Enumerable.Range(0, Sb7WaitsPerActor)
        .Select(index => "wait-" + index.ToString("D4", CultureInfo.InvariantCulture)).ToArray();

    internal static async Task<int> RunSb7BroadcastAsync(string provider, bool inBatch, bool authored)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        var token = deadline.Token;
        var clock = Stopwatch.StartNew();
        var directory = Directory.CreateTempSubdirectory("sb7-hosted-broadcast");
        try
        {
            var scope = ContractScopes.Bind("sb7-broadcast");
            var qualified = scope.Tenant + "_" + Sb7Key;
            var model = new Sb7Actor(authored);
            var (open, ingress, reads, owner, _, restart) = await OpenAsync(model, provider,
                Path.Combine(directory.FullName, "store.db"), inBatch, token, pagedPublicEvents: true);
            await using var initialOwner = owner;

            var subscribers = new List<Contract.ActorRef>(Sb7Actors);
            // Stage a few ordinary creator turns at a time so SQLite does not bind all 100,000 wait inserts in one command.
            var waveSize = provider == "sqlite" ? 2 : Sb7Actors;
            while (subscribers.Count < Sb7Actors)
            {
                var next = Math.Min(Sb7Actors, subscribers.Count + waveSize);
                while (subscribers.Count < next)
                    subscribers.Add(await StartAsync("waiter", "waiter-" + subscribers.Count));

                await UntilAsync(initialOwner, async db =>
                    await db.Subscriptions.CountAsync(row => row.Key == qualified && row.Start == null, token) == next * Sb7WaitsPerActor,
                    TimeSpan.FromMilliseconds(35));
            }
            Contract.ActorRef? publisher = authored ? await StartAsync("publisher", "publisher") : null;
            if (publisher is { } source)
                await UntilAsync(initialOwner, async _ => (await reads.GetAsync(scope, source, token))?.Header.TypeData == "ready");
            await initialOwner.DisposeAsync();

            await using (var db = open())
            {
                var keeping = await db.Database.SqlQuery<int>($"""
                    SELECT CAST(count(*) AS integer) AS "Value" FROM "FoundationPublicEventLiveWaits" WHERE "Key" = {qualified}
                    """).SingleAsync(token);
                Sb7Require(keeping == 0, "the 100,000 committed subscriptions are all ordinary waits");
            }
            Console.WriteLine($"SB-7 registered {Sb7Waits:N0} ordinary waits in {clock.Elapsed.TotalSeconds:F2}s.");

            var eventTarget = new Contract.Target.Event(Sb7Key);
            var options = new Contract.AddOptions(KeepFor: Sb7Retention);
            Contract.PublicationReceipt? accepted = null;
            long initialSequence;
            if (publisher is { } author)
            {
                var trigger = await ingress.SendAsync(scope, new Contract.Target.Ref(author), Sb7Trigger, Sb7Trigger,
                    options: options, cancellationToken: token);
                Sb7Require(trigger.Outcome == Contract.AdmissionOutcome.Accepted, $"the authored trigger is admitted ({trigger.Reason})");
                await using var db = open();
                initialSequence = await db.Messages.Where(row => row.Kind == Sb7Trigger).Select(row => row.Seq).SingleAsync(token);
            }
            else
            {
                var admission = await ingress.SendAsync(scope, eventTarget, Sb7Outside, Contract.Kinds.Event, Sb7Payload, options, token);
                Sb7Require(admission is { Outcome: Contract.AdmissionOutcome.Accepted, Publication.Status: Contract.PublicationStatus.Pending },
                    $"the outside publication is accepted Pending while the owner is stopped ({admission.Outcome}, {admission.Reason})");
                accepted = admission.Publication!;
                var replay = await ingress.SendAsync(scope, eventTarget, Sb7Outside, Contract.Kinds.Event, "ignored replay payload", options, token);
                Sb7Require(replay.Outcome == Contract.AdmissionOutcome.Duplicate && replay.Publication == accepted,
                    "the pre-match replay returns the original pending receipt");
                await using var db = open();
                initialSequence = await db.Messages.Where(row => row.InboxScope == RootPublicEventTicket.Scope)
                    .Select(row => row.Seq).SingleAsync(token);
            }

            var broadcastStart = clock.Elapsed;
            await using var resumed = await restart(token);
            await UntilAsync(resumed, async db =>
                await db.Database.SqlQuery<int>($"""
                    SELECT CAST(count(*) AS integer) AS "Value" FROM "Instances"
                    WHERE "FoundationTenant" = {scope.Tenant} AND "Status" = {(int)InstanceStatus.Finished}
                    """).SingleAsync(token) == Sb7Actors);
            await resumed.DisposeAsync();

            var eventId = await VerifySb7PagesAsync(open, scope.Tenant, qualified, subscribers, initialSequence, authored, token);
            foreach (var actor in subscribers)
            {
                var view = await reads.GetAsync(scope, actor, token);
                Sb7Require(view is { Header.Status: Contract.ActorStatus.Finished, Park: null } && view.Header.TypeData == eventId,
                    $"recipient {actor} committed the complete set of exact event deliveries");
            }
            if (publisher is { } publishingActor)
            {
                var view = await reads.GetAsync(scope, publishingActor, token);
                Sb7Require(view is { Header.Status: Contract.ActorStatus.Running, Park: null } && view.Header.TypeData == eventId,
                    "the authored Event id belongs to the still-running original publisher");
            }
            else
            {
                var replay = await ingress.SendAsync(scope, eventTarget, Sb7Outside, Contract.Kinds.Event, null, options, token);
                Sb7Require(replay.Outcome == Contract.AdmissionOutcome.Duplicate &&
                    replay.Publication == accepted! with { Status = Contract.PublicationStatus.Matched },
                    "the post-match replay returns Matched with the original outside incarnation");
            }

            Console.WriteLine($"SB-7 {provider} {(inBatch ? "in-batch" : "checked")} {(authored ? "authored" : "outside")}: " +
                $"{Sb7Waits:N0} exact deliveries, {Sb7FullPages + 1} matching pages ({Sb7FullPages}×{Sb7Page} + {Sb7FinalPage})" +
                $"{(authored ? ", after one insert-only step" : "")}. Publication and consumption took {(clock.Elapsed - broadcastStart).TotalSeconds:F2}s.");
            return 0;

            async Task<Contract.ActorRef> StartAsync(string role, string id)
            {
                var target = new Contract.Target.Start(new(model.Type, role, History: Contract.History.Minimal));
                while (true)
                {
                    var result = await ingress.SendAsync(scope, target, id, Contract.Kinds.Start, cancellationToken: token);
                    if (result is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } actor }) return actor;
                    if (result.Reason is not (Contract.RefusalReason.RateLimited or Contract.RefusalReason.NodeBusy or Contract.RefusalReason.InboxFull))
                        throw new InvalidOperationException($"SB-7 {id} was refused ({result.Outcome}, {result.Reason}).");
                    await Task.Delay(result.RetryAfter ?? TimeSpan.FromMilliseconds(50), token);
                }
            }

            async Task UntilAsync(IAsyncDisposable hosted, Func<KernelDb, Task<bool>> done, TimeSpan? interval = null)
            {
                while (true)
                {
                    if (hosted is SqliteEmbeddedHostedService { LastError: { } fault })
                        throw new InvalidOperationException("The SB-7 SQLite owner faulted.", fault);

                    await using var db = open();
                    var broken = await db.Database.SqlQuery<string>($"""
                        SELECT "Id" || ': ' || coalesce("Fault", 'parked') AS "Value" FROM "Instances"
                        WHERE "FoundationTenant" = {scope.Tenant} AND ("Status" = {(int)InstanceStatus.Faulted} OR "FoundationParkAt" IS NOT NULL)
                        """).FirstOrDefaultAsync(token);
                    Sb7Require(broken is null, "the owner parked or faulted an SB-7 actor: " + broken);
                    if (await done(db)) return;
                    await Task.Delay(interval ?? TimeSpan.FromMilliseconds(250), token);
                }
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task<string> VerifySb7PagesAsync(Func<KernelDb> open, string tenant, string qualified,
        IReadOnlyList<Contract.ActorRef> subscribers, long initialSequence, bool authored, CancellationToken token)
    {
        await using var db = open();
        var publication = await db.Database.SqlQuery<Sb7Publication>($"""
            SELECT p."Occurrence", p."EventId", d."Ticket", d."Target", d."Partition", d."Status",
                d."LiveComplete", d."LiveThroughInstance", d."LiveThroughWait", d."StartThrough"
            FROM "FoundationPublicEventPublications" p JOIN "FoundationPublicEventDispatch" d ON d."Occurrence" = p."Occurrence"
            WHERE p."Tenant" = {tenant} AND p."Key" = {qualified}
            """).SingleAsync(token);
        var eventId = DurableKeyJson.Read<string>(publication.EventId);
        Sb7Require(authored ? eventId.StartsWith('~') : eventId == Sb7Outside, "the persisted publication has the exact source id");

        var recipients = await db.Database.SqlQuery<Sb7Recipient>($"""
            SELECT "Id", "FoundationReference" AS "Reference" FROM "Instances"
            WHERE "FoundationTenant" = {tenant} AND "Status" = {(int)InstanceStatus.Finished}
            """).ToListAsync(token);
        Sb7Require(recipients.Select(row => row.Reference).ToHashSet(StringComparer.Ordinal)
            .SetEquals(subscribers.Select(actor => actor.Id)), "the physical recipient lives equal the admitted subscriber set");
        recipients.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
        Sb7Require(publication.Status == (int)Contract.PublicationStatus.Matched && publication.LiveComplete &&
            publication.LiveThroughInstance == recipients[^1].Id && publication.LiveThroughWait == Sb7Names[^1] &&
            publication.StartThrough is null, "the publication is Matched at the exact final live wait");

        var receiptIds = await db.Database.SqlQuery<string>($"""
            SELECT "Delivery" AS "Value" FROM "FoundationPublicEventLiveReceipts" WHERE "Occurrence" = {publication.Occurrence}
            """).ToListAsync(token);
        var receipts = receiptIds.ToHashSet(StringComparer.Ordinal);
        Sb7Require(receipts.Count == Sb7Waits && receiptIds.Count == Sb7Waits, "every named wait has exactly one durable occurrence receipt");
        var ticket = await db.Messages.AsNoTracking().SingleAsync(row => row.Id == publication.Ticket && row.Target == publication.Target &&
            row.Partition == publication.Partition && row.InboxScope == RootPublicEventTicket.Scope && row.Kind == Kinds.Publish, token);

        // Within this quiet store, only the ticket and the deliveries allocate sequences. Each match commit writes one contiguous
        // run of deliveries. A single reserved sequence between runs is the same-key successor. A final settle keeps its sequence.
        // Authored first allocates its ticket on the producer turn, then advances it once without allocating a delivery.
        var sequence = initialSequence + (authored ? 2 : 0);
        var latestPageTicket = sequence;
        var count = 0;
        var deliveries = db.Messages.Where(row => row.Kind == Kinds.Event).OrderBy(row => row.Seq)
            .Select(row => new { row.Target, row.Id, row.InboxScope, row.Seq, row.Done }).AsAsyncEnumerable();
        await foreach (var delivery in deliveries.WithCancellation(token))
        {
            Sb7Require(count < Sb7Waits, "the broadcast produced more than 100,000 Event inbox rows");
            if (count != 0 && count % Sb7Page == 0) latestPageTicket = ++sequence;
            Sb7Require(delivery.Seq == ++sequence, $"delivery {count + 1} breaks the exact {Sb7Page}-delivery page order");
            var recipient = recipients[count / Sb7WaitsPerActor];
            var wait = Sb7Names[count % Sb7WaitsPerActor];
            var id = RootAdmissionWire.PublicEventOccurrence(recipient.Reference, publication.Occurrence, eventId, wait);
            Sb7Require(delivery.Done && delivery.InboxScope == InboxKey.FoundationOccurrenceScope && delivery.Target == recipient.Id &&
                delivery.Id == id && receipts.Remove(id), $"delivery {count + 1} must consume the receipt for {recipient.Reference}/{wait}");
            count++;
        }
        Sb7Require(count == Sb7FullPages * Sb7Page + Sb7FinalPage && receipts.Count == 0,
            "exactly 588 full pages plus a 40-delivery final page consumed every durable occurrence receipt");
        Sb7Require(ticket.Done && ticket.Attempts == 0 && ticket.Trial == 0 && ticket.Seq == latestPageTicket,
            "the terminal ticket settled at its final page sequence");
        return eventId;
    }

    private sealed record Sb7Publication(string Occurrence, string EventId, string Ticket, string Target, int Partition, int Status,
        bool LiveComplete, string? LiveThroughInstance, string? LiveThroughWait, string? StartThrough);
    private sealed record Sb7Recipient(string Id, string Reference);

    private static void Sb7Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("SB-7: " + message);
    }

    private sealed class Sb7Actor(bool authored) : Contract.IActorType
    {
        public string Type => "sb7-broadcast";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            var seen = turn.State?.ToCharArray();
            string? receivedEvent = null;
            foreach (var message in turn.Inbox)
            {
                switch (message.Kind)
                {
                    case Contract.Kinds.Start when turn.Header.TypeData == "waiter":
                        foreach (var wait in Sb7Names) turn.Subscribe(Sb7Key, wait);
                        seen = new string('0', Sb7WaitsPerActor).ToCharArray();
                        break;
                    case Contract.Kinds.Start:
                        turn.SetHeader("ready", turn.Header.Labels);
                        break;
                    case Sb7Trigger:
                        turn.SetHeader(turn.Publish(Sb7Key, Sb7Payload, Sb7Retention), turn.Header.Labels);
                        break;
                    case Contract.Kinds.Event:
                        if (message.Event is not { } delivery || delivery.Key != Sb7Key || message.Payload != Sb7Payload ||
                            (authored ? !delivery.Id.StartsWith('~') : delivery.Id != Sb7Outside) ||
                            !delivery.Wait.StartsWith("wait-", StringComparison.Ordinal) ||
                            !int.TryParse(delivery.Wait.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var index) ||
                            index >= Sb7Names.Length || delivery.Wait != Sb7Names[index] || seen is null || seen[index] != '0')
                            throw new InvalidOperationException($"Wrong or repeated Event delivery: {message.Event}.");
                        seen[index] = '1';
                        receivedEvent = delivery.Id;
                        break;
                }
            }

            if (seen is not null) turn.State = new string(seen);
            if (receivedEvent is not null && !seen!.Contains('0'))
            {
                turn.SetHeader(receivedEvent, turn.Header.Labels);
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
