using System.Data.Common;
using Elsa.ActorFoundation.Hosting;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    private const string ActorReadsKind = "sqlite-root-actor-reads";

    internal static async Task<int> RunActorReadsAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = budget.Token;
        var directory = Directory.CreateTempSubdirectory(ActorReadsKind);
        try
        {
            var file = Path.Combine(directory.FullName, "reads.db");
            await InstallFoundationAsync(file, token);
            var tenant = EmbeddedTenant.Bind(ActorReadsKind);
            var other = ContractScopes.Bind("reads-other");
            var reads = SqliteEmbeddedHosting.CreateFoundationReads(file, pageSize: 1);
            var ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(file, ActorReadsKind,
                (_, _, kind) => kind == Contract.Kinds.Delete, cancellationToken: token);
            var definitions = new SqliteAdmissionDefinitions();
            var reader = new EfStore(() => OpenFoundationDb(file, definitions), postgres: false, fenceInBatch);
            var first = await AdmitAsync(tenant, "first", "nul");
            var escaped = await AdmitAsync(tenant, "escaped", "escaped");
            var parked = await AdmitAsync(tenant, "parked", "park");
            var hidden = await AdmitAsync(other, "hidden", "nul");
            await reader.EnableRootSelfSendsAsync(token);
            await reader.EnableRootJobsAsync(token);
            await reader.EnableRootChildrenAsync(token);
            reader.BindSqliteWorkerNode();
            var pending = await CheckCreatorRollbackAsync(file, tenant.Tenant, first, fenceInBatch, token);
            Require((await reader.LoadAsync([pending, pending], token)).Count == 0,
                "the batched SQLite load recognizes an admitted birth before its first checkpoint");
            await ExpectFoundationFailureAsync<NotSupportedException>(
                () => reader.LoadAsync([pending, InstanceRef.In(0, "unadmitted").Id], token).AsTask(),
                "the batched SQLite load refuses a requested actor without an admitted birth");
            await using var host = SqliteEmbeddedHostedService.ForFoundation(file, ActorReadsKind, new ActorReadsProbe(),
                reason => throw new InvalidOperationException(reason), Loops, fenceInBatch);
            await host.StartAsync(token);
            foreach (var root in new[] { first, escaped, parked })
                await WaitForRootAsync(reader, tenant.Tenant, root, 1, token);
            await WaitForRootAsync(reader, other.Tenant, hidden, 1, token);
            Require(await ingress.SendAsync(tenant, new Contract.Target.Ref(parked), "poison", "fail", cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the actor read fixture admits the Park-causing input");
            await WaitForRootAsync(reader, tenant.Tenant, parked, 2, token);
            var parkedImage = (await reader.ReadFirstRootAsync(tenant.Tenant, parked.Id, token))!;
            var loaded = await reader.LoadAsync([parkedImage.Actor.Id, pending, parkedImage.Actor.Id], token);
            Require(loaded is [{ Status: InstanceStatus.Faulted, Revision: 2 } faulted, { Status: InstanceStatus.Running, Revision: 1 } running] &&
                faulted.Id == parkedImage.Actor.Id && faulted.FoundationDispatchHeader?.Reference == parked.Id &&
                running.Id == pending && running.FoundationDispatchHeader?.Reference == first.Id,
                "one batched SQLite load restores exact Parked and Running roots in distinct request order");

            var unrestricted = new Contract.ActorQuery(Type: ActorReadsKind);
            var page = await reads.ListAsync(tenant, unrestricted, cancellationToken: token);
            Require(page is { Items.Count: 1, Next: not null, AsOf: > 0 }, "the list returns its first bounded page with the SQLite snapshot clock");
            await ExpectFoundationFailureAsync<ArgumentException>(() => reads.ListAsync(other, unrestricted, page.Next, token).AsTask(),
                "the actor-list cursor cannot be reused by a different tenant");
            await ExpectFoundationFailureAsync<ArgumentException>(() => reads.ListAsync(tenant,
                unrestricted with { Parked = true }, page.Next, token).AsTask(), "the actor-list cursor binds the query filters");
            var all = await ListAsync(tenant, unrestricted);
            Require(all.Select(row => row.Header.Ref.Id).SequenceEqual(new[] { first.Id, escaped.Id, parked.Id }.Order(StringComparer.Ordinal)) &&
                (await ListAsync(other, unrestricted)).Select(row => row.Header.Ref).SequenceEqual([hidden]),
                "actor-list keyset pages use ordinal order and expose only the tenant's roots");
            Require((await ListAsync(tenant, unrestricted with { Parked = true, Status = Contract.ActorStatus.Faulted })).Single().Header.Ref == parked &&
                (await ListAsync(tenant, unrestricted with { Parked = false, Status = Contract.ActorStatus.Running })).Count == 2,
                "status and Park filters use committed projection metadata");
            Require((await ListAsync(tenant, unrestricted with { Labels = new Dictionary<string, string> { ["key\0"] = "a\0b", ["group"] = "example" } }))
                    .Select(row => row.Header.Ref).SequenceEqual([first]) &&
                (await ListAsync(tenant, unrestricted with { Labels = new Dictionary<string, string> { ["key\0"] = "a\\u0000b" } }))
                    .Select(row => row.Header.Ref).SequenceEqual([escaped]) &&
                (await ListAsync(tenant, unrestricted with { Labels = new Dictionary<string, string> { ["KEY\0"] = "a\0b" } })).Count == 0,
                "label conjunction and ordinal matching distinguish escaped NUL, literal backslash and case");
            Require((await ListAsync(tenant, new(Type: ActorReadsKind.ToUpperInvariant()))).Count == 0,
                "the actor type filter uses ordinal equality");

            var parkPlan = new ParkReadObservation("/* sqlite-root-actor-list */");
            parkPlan.Resume.TrySetResult();
            var parkProbe = new EfStore(() => OpenFoundationDb(file, definitions, parkPlan), postgres: false, fenceInBatch);
            await parkProbe.ReadRootActorListAsync(tenant.Tenant, "", ActorReadsKind, null, [], null, true, 1, token);
            Require(parkPlan.Plan.Any(row => row.Contains("IX_FoundationActorList_Parked", StringComparison.Ordinal)),
                "the parked-only list uses the tenant and reference seek on the partial index");

            var journalPlan = new ParkReadObservation("/* sqlite-root-journal */");
            journalPlan.Resume.TrySetResult();
            var journalProbe = new EfStore(() => OpenFoundationDb(file, definitions, journalPlan), postgres: false, fenceInBatch);
            await journalProbe.ReadRootJournalAsync(tenant.Tenant, first.Id, 5, 1, token);
            Require(journalPlan.Plan.Any(row => row.Contains("IX_Journal_Partition_Instance_Position (Partition=? AND Instance=? AND Position>?)",
                    StringComparison.Ordinal)) && journalPlan.Plan.All(row => !row.Contains("TEMP B-TREE", StringComparison.Ordinal)),
                "the journal seeks its actor and position without scanning or sorting sibling entries");

            var journal = await reads.TailAsync(tenant, first, cancellationToken: token);
            Require(journal is { Items.Count: 1, Next: not null, AsOf: > 0 }, "the actor journal returns one entry and a cursor within the checkpoint");
            await ExpectFoundationFailureAsync<ArgumentException>(() => reads.TailAsync(other, first, journal.Next, token).AsTask(),
                "the actor journal cursor is tenant-bound");
            await ExpectFoundationFailureAsync<ArgumentException>(() => reads.TailAsync(tenant, escaped, journal.Next, token).AsTask(),
                "the actor journal cursor is reference-bound");
            var entries = new List<Contract.JournalEntry>();
            Contract.PageToken? last = null;
            while (journal.Items is [var entry] && journal.Next is { } tokenAfter)
            {
                entries.Add(entry);
                last = tokenAfter;
                journal = await reads.TailAsync(tenant, first, last, token);
            }
            Require(entries.Where(entry => entry.Entry.Contains("\"Step\"", StringComparison.Ordinal))
                    .SequenceEqual([new(1, "{\"Step\":\"start-first\"}"), new(1, "{\"Step\":\"start-second\"}")]) &&
                entries.Select(entry => entry.CommitOrdinal).SequenceEqual(Enumerable.Range(0, entries.Count).Select(index => (int?)index)) &&
                journal is { Items.Count: 0, Next: null, AsOf: > 0 } &&
                await reads.TailAsync(other, first, cancellationToken: token) is { Items.Count: 0, AsOf: > 0 },
                "journal paging resumes exactly within one revision and keeps other tenants' journals empty");

            Require(await ingress.SendAsync(tenant, new Contract.Target.Ref(first), "finish", "finish", cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the fixture admits its next journal checkpoint");
            await WaitForRootAsync(reader, tenant.Tenant, first, 2, token);
            var terminal = (await reader.ReadFirstRootAsync(tenant.Tenant, first.Id, token))!;
            Require(terminal is { Status: InstanceStatus.Finished, TerminalAt: > 0 } &&
                await reads.TailAsync(tenant, first, last, token) is { Items: [{ Revision: 2, CommitOrdinal: 0 }], Next: not null },
                "the previously caught-up journal cursor resumes at the new checkpoint");
            Require((await ListAsync(tenant, unrestricted with { EndedBefore = terminal.TerminalAt })).Count == 0 &&
                (await ListAsync(tenant, unrestricted with { EndedBefore = terminal.TerminalAt + 1 })).Single().Header.Ref == first,
                "the ended-before filter has a strict terminal-time boundary");
            var retention = (Contract.IActorRetentionReads)reads;
            var keep = new Contract.ActorRetentionWindows();
            var expire = new Contract.ActorRetentionWindows(TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
            var quote = await retention.ReadRetentionQuoteAsync(tenant, ActorReadsKind, keep, expire, cancellationToken: token);
            Require(quote.NewlyEligible == 1 && quote.AsOf >= terminal.TerminalAt,
                "the retention quote counts the ended run and excludes the Running and unresolved Park projections");
            var endedAt = terminal.TerminalAt!.Value;
            var oneTick = expire with { Finished = TimeSpan.FromTicks(1) };
            Require(await retention.ReadRetentionQuoteAsync(tenant, ActorReadsKind, oneTick, expire, new("definition", "a\0b"), endedAt, token) ==
                new Contract.ActorRetentionQuote(1, endedAt) &&
                await retention.ReadRetentionQuoteAsync(tenant, ActorReadsKind, keep, expire, asOf: endedAt - 1, cancellationToken: token) ==
                new Contract.ActorRetentionQuote(0, endedAt - 1),
                "the same historical clock excludes younger runs and retains the fractional prior-window boundary");
            Require(await retention.ReadRetentionQuoteAsync(tenant, ActorReadsKind, keep, expire, new("definition", "a\\u0000b"), endedAt, token) is { NewlyEligible: 0 } &&
                await retention.ReadRetentionQuoteAsync(other, ActorReadsKind, keep, expire, cancellationToken: token) is { NewlyEligible: 0 },
                "retention quotes use exact definition labels and tenant scope");
            await ExpectFoundationFailureAsync<ArgumentOutOfRangeException>(() => retention.ReadRetentionQuoteAsync(tenant, ActorReadsKind,
                keep, expire, asOf: long.MaxValue, cancellationToken: token).AsTask(), "the retention quote refuses a future clock");
            var finished = (await reads.GetAsync(tenant, first, token))!;
            Require(await ingress.SendAsync(tenant, new Contract.Target.Ref(first), "delete", Contract.Kinds.Delete,
                Json.Write(new Contract.OperatorCommand(finished.Selection!, ExpectedTarget: first)), cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the SQLite actor reader supplies the exact terminal selection for retirement");
            while (await reads.GetAsync(tenant, first, token) is not null) await Task.Delay(25, token);
            Require((await ListAsync(tenant, unrestricted)).All(row => row.Header.Ref != first) &&
                await reads.TailAsync(tenant, first, last, token) is { Items.Count: 0, AsOf: > 0 } &&
                (await reader.LoadAsync([pending], token)).Count == 0,
                "retirement removes the actor from lists, journal observations and worker loads");
            await host.StopAsync(token);
            Require(host.LastError is null, "the SQLite read fixture owner stops cleanly");
            return 0;

            async Task<Contract.ActorRef> AdmitAsync(Contract.BoundScope scope, string id, string definition) => (await ingress.SendAsync(scope,
                new Contract.Target.Start(new(ActorReadsKind, definition)), id, Contract.Kinds.Start, cancellationToken: token)).Actor!.Value;

            async Task<List<Contract.ActorView>> ListAsync(Contract.BoundScope scope, Contract.ActorQuery query)
            {
                var items = new List<Contract.ActorView>();
                Contract.PageToken? after = null;
                do
                {
                    var next = await reads.ListAsync(scope, query, after, token);
                    items.AddRange(next.Items);
                    after = next.Next;
                } while (after is not null);
                return items;
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private static async Task<string> CheckCreatorRollbackAsync(string file, string tenant, Contract.ActorRef actor,
        bool fenceInBatch, CancellationToken token)
    {
        await using var db = OpenFoundationDb(file);
        var admitted = await db.Messages.FromSqlInterpolated($"""
            SELECT m.* FROM "Messages" m JOIN "FoundationRootBirths" b ON b."Creator" = m."Id"
                AND b."Actor" = m."Target" AND b."Partition" = m."Partition"
            WHERE b."Tenant" = {tenant} AND b."Reference" = {actor.Id} AND m."InboxScope" = 0 AND NOT m."Done"
            """).AsNoTracking().SingleAsync(token);
        var creator = PostgresRootAdmission.DecodeCreator(admitted);
        var start = creator.Start!;
        var drift = new CreatorRouteDrift(admitted.Target);
        var definitions = new SqliteAdmissionDefinitions();
        var store = new EfStore(() => OpenFoundationDb(file, definitions, drift), postgres: false, fenceInBatch);
        await store.EnableRootSelfSendsAsync(token);
        await store.EnableRootJobsAsync(token);
        await store.EnableRootChildrenAsync(token);
        store.BindSqliteWorkerNode();

        var leaseTime = TimeSpan.FromSeconds(10);
        var lease = await store.TakeAsync(admitted.Partition, "creator-bind-probe", leaseTime, token)
            ?? throw new InvalidOperationException("The creator-bind probe did not take its fence.");
        var header = new FoundationHeaderChange(start.Definition, new Dictionary<string, string>());
        var row = new InstanceRow(creator.Target.Id, start.Model, start.Definition, InstanceStatus.Running, "null", 1, null,
            History: start.History, StateVersion: 1, Creation: 1, StartIntent: CreationProtocol.Fingerprint(start, creator.Kind, creator.Payload))
        {
            NextJobGeneration = 0,
            FoundationDispatchHeader = new(tenant, actor.Id, header)
        };
        var turn = new TurnCommit(row.Id, row, null, [creator.InboxKey], [], [], [], [], "[]") { FoundationHeader = header };
        var batch = new CommitBatch(admitted.Partition, lease.Epoch, lease.Position, leaseTime, false, [turn], [], []);
        var refused = await store.CommitAsync(batch, token);
        Require(drift.Altered && !refused.Landed &&
            await db.Messages.AnyAsync(row => row.Id == admitted.Id && row.InboxScope == 0 && !row.Done && row.ReplyTo == admitted.ReplyTo, token) &&
            !await db.Instances.AnyAsync(row => row.Id == admitted.Target, token) &&
            !await db.Journal.AnyAsync(row => row.Instance == admitted.Target, token) &&
            await db.Partitions.Where(row => row.Id == admitted.Partition).Select(row => row.Position).SingleAsync(token) == lease.Position,
            "a changed creator envelope rolls back the fenced delete, checkpoint and journal and keeps the creator pending");
        await store.ReleaseAsync(admitted.Partition, lease.Epoch, token);
        return admitted.Target;
    }

    private sealed class CreatorRouteDrift(string actor) : DbCommandInterceptor
    {
        internal bool Altered { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!command.CommandText.Contains("""DELETE FROM "Messages" AS m""", StringComparison.Ordinal)) return result;

            // The fingerprint does not include the live route. Change it after prepare, inside the fenced transaction,
            // so the returned creator reaches the full envelope comparison.
            using var change = ((SqliteConnection)command.Connection!).CreateCommand();
            change.Transaction = (SqliteTransaction)command.Transaction!;
            change.CommandText = """UPDATE "Messages" SET "ReplyTo" = $route WHERE "Target" = $actor AND "InboxScope" = 0""";
            change.Parameters.AddWithValue("$actor", actor);
            change.Parameters.AddWithValue("$route", ReplyTo.Of("creator-bind-probe", "another-registration", long.MaxValue));
            Altered = await change.ExecuteNonQueryAsync(cancellationToken) == 1;
            return result;
        }
    }

    private sealed class ActorReadsProbe : Contract.IActorType
    {
        public string Type => ActorReadsKind;
        public bool Withholds(Contract.ActorHeader header) => false;
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            if (turn.StartSettings is not null)
            {
                turn.SetHeader(turn.Header.TypeData, new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["group"] = "example",
                    ["definition"] = turn.Header.TypeData switch { "nul" => "a\0b", "escaped" => "a\\u0000b", _ => turn.Header.TypeData },
                    ["key\0"] = turn.Header.TypeData switch { "nul" => "a\0b", "escaped" => "a\\u0000b", _ => "park" }
                });
                turn.Record(new { Step = "start-first" });
                turn.Record(new { Step = "start-second" });
                turn.State = "null";
            }
            else if (turn.Inbox.Any(input => input.Kind == "fail")) throw new InvalidOperationException("Park from actor read fixture.");
            else if (turn.Inbox.Any(input => input.Kind == "finish")) turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
    }
}
