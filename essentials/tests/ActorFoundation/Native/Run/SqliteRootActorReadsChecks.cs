using Elsa.ActorFoundation.Hosting;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
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
            await using var host = SqliteEmbeddedHostedService.ForFoundation(file, ActorReadsKind, new ActorReadsProbe(),
                reason => throw new InvalidOperationException(reason), Loops, fenceInBatch);
            await host.StartAsync(token);
            foreach (var root in new[] { first, escaped, parked })
                await WaitForRootAsync(reader, tenant.Tenant, root, 1, token);
            await WaitForRootAsync(reader, other.Tenant, hidden, 1, token);
            Require(await ingress.SendAsync(tenant, new Contract.Target.Ref(parked), "poison", "fail", cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the actor read fixture admits the Park-causing input");
            await WaitForRootAsync(reader, tenant.Tenant, parked, 2, token);

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
            Require(entries.Where(entry => entry.Entry.Contains("\"Step\"", StringComparison.Ordinal)).Select(entry => (entry.Revision, entry.Entry))
                    .SequenceEqual([(1L, "{\"Step\":\"start-first\"}"), (1L, "{\"Step\":\"start-second\"}")]) &&
                journal is { Items.Count: 0, Next: null, AsOf: > 0 } &&
                await reads.TailAsync(other, first, cancellationToken: token) is { Items.Count: 0, AsOf: > 0 },
                "journal paging resumes exactly within one revision and keeps other tenants' journals empty");

            Require(await ingress.SendAsync(tenant, new Contract.Target.Ref(first), "finish", "finish", cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the fixture admits its next journal checkpoint");
            await WaitForRootAsync(reader, tenant.Tenant, first, 2, token);
            var terminal = (await reader.ReadFirstRootAsync(tenant.Tenant, first.Id, token))!;
            Require(terminal is { Status: InstanceStatus.Finished, TerminalAt: > 0 } &&
                await reads.TailAsync(tenant, first, last, token) is { Items: [{ Revision: 2 }], Next: not null },
                "the previously caught-up journal cursor resumes at the new checkpoint");
            Require((await ListAsync(tenant, unrestricted with { EndedBefore = terminal.TerminalAt })).Count == 0 &&
                (await ListAsync(tenant, unrestricted with { EndedBefore = terminal.TerminalAt + 1 })).Single().Header.Ref == first,
                "the ended-before filter has a strict terminal-time boundary");
            var finished = (await reads.GetAsync(tenant, first, token))!;
            Require(await ingress.SendAsync(tenant, new Contract.Target.Ref(first), "delete", Contract.Kinds.Delete,
                Json.Write(new Contract.OperatorCommand(finished.Selection!, ExpectedTarget: first)), cancellationToken: token) is
                { Outcome: Contract.AdmissionOutcome.Accepted }, "the SQLite actor reader supplies the exact terminal selection for retirement");
            while (await reads.GetAsync(tenant, first, token) is not null) await Task.Delay(25, token);
            Require((await ListAsync(tenant, unrestricted)).All(row => row.Header.Ref != first) &&
                await reads.TailAsync(tenant, first, last, token) is { Items.Count: 0, AsOf: > 0 },
                "retirement removes the actor from lists and observations of its former journal");
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
