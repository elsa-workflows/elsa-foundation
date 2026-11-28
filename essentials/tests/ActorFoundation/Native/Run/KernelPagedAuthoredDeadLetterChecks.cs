using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    internal static async Task<int> RunPagedAuthoredDeadLettersAsync(string provider, bool inBatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var token = timeout.Token;
        var scope = ContractScopes.Bind("paged-event-dead-letter");
        var model = new DeadLetterAuthor();
        var directory = Directory.CreateTempSubdirectory("paged-event-dead-letter");
        try
        {
            var (open, ingress, reads, owner, _, restart) = await OpenAsync(model, provider,
                Path.Combine(directory.FullName, "store.db"), inBatch, token, publicStarts: true, pagedPublicEvents: true,
                authorizeOperator: (_, _, _) => true);
            await using var firstOwner = owner;
            var ended = await StartAsync("ended", Contract.ActorStatus.Finished);
            var running = await StartAsync("running", Contract.ActorStatus.Running);
            await firstOwner.DisposeAsync();

            // An already elapsed Event window must not erase the only initial payload if the ticket exhausts.
            var endedTicket = await SeedAsync(ended, "ended-ticket");
            var parkedTicket = await SeedAsync(running, "parked-ticket");
            await using var successor = await restart(token);
            await RequireDeadLetterAsync(endedTicket, successor);

            Contract.ActorView? parked = null;
            await UntilAsync(successor, async () =>
            {
                parked = await reads.GetAsync(scope, running, token);
                return parked?.Park is not null;
            });
            await using (var db = open())
            {
                var retained = await ReadTicketAsync(db, parkedTicket);
                Check(retained.Done && await ReadKeptReasonAsync(db, parkedTicket) == (short)ParkKeepReason.Consumed &&
                    RootPublicEventTicket.Decode(retained).Payload == parkedTicket.Payload,
                    "the exhausted initial publication holds its exact payload while the author is parked");
            }

            var command = JsonSerializer.Serialize(new Contract.OperatorCommand(parked!.Selection, ExpectedTarget: running));
            var discarded = await ingress.SendAsync(scope, new Contract.Target.Ref(running), "discard-initial-publication",
                Contract.Kinds.Delete, command, cancellationToken: token);
            Check(discarded.Outcome == Contract.AdmissionOutcome.Accepted, "Delete accepts the exact Park selection");
            await UntilAsync(successor, async () => await reads.GetAsync(scope, running, token) is null);
            await RequireDeadLetterAsync(parkedTicket, successor);
            return 0;

            async Task<Contract.ActorRef> StartAsync(string id, Contract.ActorStatus expected)
            {
                var accepted = await ingress.SendAsync(scope, new Contract.Target.Start(new(model.Type, id)),
                    id, Contract.Kinds.Start, cancellationToken: token);
                if (accepted.Actor is not { } actor) throw new InvalidOperationException($"Test author was refused ({accepted.Reason}).");
                await UntilAsync(firstOwner, async () => (await reads.GetAsync(scope, actor, token))?.Header.Status == expected);
                return actor;
            }

            async Task<RootPublicEventTicket> SeedAsync(Contract.ActorRef author, string id)
            {
                await using var db = open();
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                var address = await db.Database.SqlQuery<string>($"""
                    SELECT "Actor" AS "Value" FROM "FoundationRootBirths"
                    WHERE "Tenant" = {scope.Tenant} AND "Reference" = {author.Id} AND "Creation" = 1
                    """).SingleAsync(token);
                var key = scope.Tenant + "_dead-letter";
                var now = db.Database.IsNpgsql() ? await RootEventAdmission.ReadPostgresClockAsync(db, token) :
                    await SqliteRootAdmission.ReadClockAsync(db, token);
                var publication = new EventRow(key, "~" + id, "an exact \"Event\" body\0after NUL", 0) { FoundationTenant = scope.Tenant };
                var ticket = RootPublicEventTicket.Authored(publication, new(id, new(address), Kinds.Publish), now - 1000);
                Func<FormattableString, FormattableString>? bind = null;
                if (db.Database.IsNpgsql())
                {
                    var marker = await PostgresRootAdmission.PinRootCommitNamespaceAsync(db, writesJournal: false, token);
                    bind = await PostgresRootAdmission.PinAuthoredEventSourceAsync(db, marker, token);
                    await RootEventAdmission.LockPostgresKeysAsync(db, [key], token);
                }
                await ticket.EnqueueAsync(db, bind, token);
                if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Attempts" = 3 WHERE "Id" = {id} AND "InboxScope" = {RootPublicEventTicket.Scope}
                    """, token) != 1) throw new InvalidOperationException("Could not exhaust the initial publication ticket.");
                await transaction.CommitAsync(token);
                return ticket;
            }

            async Task RequireDeadLetterAsync(RootPublicEventTicket ticket, IAsyncDisposable activeOwner)
            {
                await UntilAsync(activeOwner, async () =>
                {
                    await using var db = open();
                    return await ReadTicketAsync(db, ticket) is { Done: true, KeepUntil: > 0 } &&
                        await ReadKeptReasonAsync(db, ticket) is null;
                });
                await using var db = open();
                var stored = await ReadTicketAsync(db, ticket);
                var deadLetter = RootPublicEventTicket.Decode(stored);
                Check(deadLetter == (ticket with { ReceiptUntil = stored.KeepUntil, DeadLettered = true }) &&
                    stored.Target != ticket.Target.Id && stored.Trial == 0 && stored.Attempts == 3 &&
                    stored.KeepUntil > ticket.Author!.AcceptedAt + (long)TimeSpan.FromMinutes(59).TotalMilliseconds,
                    "the original ticket identity and exact body survive as a bounded dead letter off the author mailbox");
                var materialized = await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "FoundationPublicEventDispatch" WHERE "Occurrence" = {ticket.Occurrence}
                        UNION ALL SELECT 1 FROM "FoundationPublicEventPublications" WHERE "Occurrence" = {ticket.Occurrence}) AS "Value"
                    """).SingleAsync(token);
                Check(!materialized, "no Event or dispatch row appears when the initial publication never committed");
            }

            Task<MessageEntity> ReadTicketAsync(KernelDb db, RootPublicEventTicket ticket) => db.Messages.AsNoTracking()
                .SingleAsync(message => message.Id == ticket.Id && message.InboxScope == RootPublicEventTicket.Scope, token);

            Task<short?> ReadKeptReasonAsync(KernelDb db, RootPublicEventTicket ticket) => db.Database.SqlQuery<short?>($"""
                SELECT "FoundationKeptBy" AS "Value" FROM "Messages" WHERE "Id" = {ticket.Id} AND "InboxScope" = {RootPublicEventTicket.Scope}
                """).SingleAsync(token);

            async Task UntilAsync(IAsyncDisposable activeOwner, Func<Task<bool>> condition)
            {
                while (!await condition())
                {
                    if (activeOwner is SqliteEmbeddedHostedService { LastError: { } fault })
                        throw new InvalidOperationException("The SQLite publication owner reported a fault.", fault);
                    await Task.Delay(25, token);
                }
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class DeadLetterAuthor : Contract.IActorType
    {
        public string Type => "dead-letter-author";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.Header.TypeData == "ended") turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
