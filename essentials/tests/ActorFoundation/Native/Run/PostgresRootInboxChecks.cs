using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootInboxChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch, bool operators)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use an empty dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => operators ? KernelDb.ForPostgresRootOperatorReplies(options) : new(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(1));
        var stop = budget.Token;
        await using (var db = Open())
            Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the inbox fixture starts with a fresh database");
        var store = Store();
        await store.CreateAsync(1);
        await new PostgresRootAdmission(Open, TimeSpan.FromHours(1)).InstallAsync(stop);
        var blue = EmbeddedTenant.Bind("inbox-blue");
        var red = ContractScopes.Bind("inbox-red");
        var probe = new Probe();
        var read = await store.OpenPreviewRootReadsAsync(1, stop);
        var ingress = operators
            ? await store.OpenSelectedRootIngressAsync(probe.Type, TimeSpan.FromHours(1), (scope, _) => scope == blue, stop,
                (scope, _) => scope == blue)
            : await store.OpenPreviewRootIngressAsync(probe.Type, TimeSpan.FromHours(1), stop);
        var actor = await Birth(blue, "subject");
        var sibling = await Birth(blue, "sibling");
        var foreign = await Birth(red, "foreign");
        Check((await read.InboxAsync(blue, actor, cancellationToken: stop)).Items.Count == 0,
            "a reference without a committed checkpoint has no operational inbox");

        long firstEpoch;
        Contract.Page<Contract.DeliveryView> first;
        const string kind = "escaped\0kind";
        await using (var owner = await Own("inbox-original"))
        {
            await Drive(owner, async () => await read.GetAsync(blue, actor, stop) is not null &&
                await read.GetAsync(blue, sibling, stop) is not null && await read.GetAsync(red, foreign, stop) is not null);
            firstEpoch = owner.Epoch;
            var databaseTime = (await read.InboxAsync(blue, actor, cancellationToken: stop)).AsOf;
            var future = databaseTime + 120_000;
            Check(await Send(blue, actor, "first", kind, "body", 0) is { Outcome: Contract.AdmissionOutcome.Accepted } &&
                await Send(blue, actor, "later", "timer", new string('p', 500_000) + "\0🌐", future) is { Outcome: Contract.AdmissionOutcome.Accepted } &&
                await Send(blue, sibling, "sibling-input", "work", null, future) is { Outcome: Contract.AdmissionOutcome.Accepted } &&
                await Send(red, foreign, "foreign-input", "work", null, future) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "three roots admit inputs while only the subject has two occurrences");
            first = await read.InboxAsync(blue, actor, cancellationToken: stop);
            Check(first is { Items: [{ Id: "first", Kind: kind, Due: 0, Sender: null, State: Contract.DeliveryState.Pending, Attempts: 0 }],
                    Next: not null } && first.Items[0].Handle.Length == 65 && first.Items[0].Size > 4 && first.AsOf >= databaseTime,
                "a pending page reports the public id, escaped kind, explicit zero due and database time");
            Check((await read.InboxAsync(red, actor, cancellationToken: stop)).Items.Count == 0 &&
                (await read.InboxAsync(blue, foreign, cancellationToken: stop)).Items.Count == 0 &&
                (await read.InboxAsync(blue, sibling, cancellationToken: stop)).Items is [{ Id: "sibling-input" }] &&
                (await read.InboxAsync(blue, new("unissued"), cancellationToken: stop)) is { Items.Count: 0, AsOf: > 0 },
                "unissued and foreign references return empty pages and sibling queues stay separate");
            var saved = first.Next ?? throw new InvalidOperationException("The first inbox page did not issue a continuation.");
            var altered = (saved.Value[0] == 'A' ? 'B' : 'A') + saved.Value[1..];
            Check(await RejectToken(red, actor, saved) && await RejectToken(blue, sibling, saved) &&
                await RejectToken(blue, actor, new(altered)),
                "a cursor cannot continue a different tenant, actor or altered page");
            var later = await read.InboxAsync(blue, actor, first.Next, stop);
            Check(later is { Items: [{ Id: "later", Kind: "timer", State: Contract.DeliveryState.Pending }], Next: null } &&
                later.Items[0].Due == future && later.Items[0].Size >= 500_000 && later.Items[0].Handle != first.Items[0].Handle,
                "the next page returns future-message metadata and the size of its stored envelope");
            await using var db = Open();
            var physical = (await store.ReadFirstRootAsync(blue.Tenant, actor.Id, stop))!.Actor.Id;
            var rows = await db.Messages.AsNoTracking().Where(row => row.Target == physical && !row.Done)
                .OrderBy(row => row.Seq).ToArrayAsync(stop);
            if (rows.Length != 2) throw new InvalidOperationException("The tied-page fixture requires two stored occurrences.");
            var moved = db.Messages.Where(row => row.Target == physical && row.Id == rows[1].Id);
            Check(await moved.ExecuteUpdateAsync(set => set.SetProperty(row => row.Seq, rows[0].Seq), stop) == 1,
                "the pagination fixture gives both occurrences the same delivery order");
            try
            {
                var tied = await read.InboxAsync(blue, actor, cancellationToken: stop);
                var next = await read.InboxAsync(blue, actor, tied.Next, stop);
                var expected = StringComparer.Ordinal.Compare(rows[0].Id, rows[1].Id) < 0 ? new[] { "first", "later" } : ["later", "first"];
                Check(tied.Next is not null && next.Next is null && tied.Items.Concat(next.Items).Select(row => row.Id).SequenceEqual(expected),
                    "the secondary occurrence key keeps tied positions deterministic and lossless");
            }
            finally { await moved.ExecuteUpdateAsync(set => set.SetProperty(row => row.Seq, rows[1].Seq), stop); }
        }

        var reopened = await Store().OpenPreviewRootReadsAsync(1, stop);
        Check((await reopened.InboxAsync(blue, actor, cancellationToken: stop)).Items[0].Handle == first.Items[0].Handle &&
            (await reopened.InboxAsync(blue, actor, first.Next, stop)).Items is [{ Id: "later" }],
            "a new reader opens the saved page and stable handle from the stored issuer");
        await using (var successor = await Own("inbox-successor"))
        {
            Check(successor.Epoch > firstEpoch, "the real Worker takes a new epoch");
            await Drive(successor, async () => (await reopened.InboxAsync(blue, actor, cancellationToken: stop)).Items is [{ Id: "later" }]);
            await using var db = Open();
            var image = (await store.ReadFirstRootAsync(blue.Tenant, actor.Id, stop))!;
            Check(image.Revision == 2 && await db.Messages.AsNoTracking().AnyAsync(row => row.Target == image.Actor.Id && row.Done, stop) &&
                (await reopened.InboxAsync(blue, actor, first.Next, stop)).Items is [{ Id: "later" }],
                "a committed input is absent from inspection while its deduplication row remains");

            if (operators)
            {
                var selected = (await reopened.GetAsync(blue, actor, stop))!;
                var operation = new Contract.OperatorCommand(selected.Selection, ThenDelete: true, ExpectedTarget: actor);
                var applied = await ingress.SendAsync(blue, new Contract.Target.Ref(actor), "retire", Contract.Kinds.Terminate,
                    JsonSerializer.Serialize(operation), cancellationToken: stop);
                var current = await reopened.InboxAsync(blue, actor, cancellationToken: stop);
                Check(applied is { Outcome: Contract.AdmissionOutcome.Accepted } && current.Next is not null &&
                    (await reopened.InboxAsync(blue, actor, current.Next, stop)).Items is [{ Id: "retire", Kind: Contract.Kinds.Terminate }],
                    "the inbox exposes the admitted operator id and kind without exposing its internal bound selection");
                await Drive(successor, async () => await reopened.GetAsync(blue, actor, stop) is null);
                Check((await reopened.InboxAsync(blue, actor, cancellationToken: stop)).Items.Count == 0 &&
                    (await reopened.InboxAsync(blue, actor, current.Next, stop)).Items.Count == 0 &&
                    (await reopened.InboxAsync(blue, sibling, cancellationToken: stop)).Items is [{ Id: "sibling-input" }],
                    "retirement hides the exact root and all its pending occurrences without touching a sibling");
            }
        }

        await using (var db = Open())
        {
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" RENAME COLUMN "State" TO "UnavailableState" """, stop);
            try
            {
                Check((await reopened.InboxAsync(blue, sibling, cancellationToken: stop)).Items is [{ Id: "sibling-input" }],
                    "a cold inbox read uses bounded metadata with the actor state column unavailable");
            }
            finally { await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Instances" RENAME COLUMN "UnavailableState" TO "State" """, stop); }
        }

        await using (var db = Open())
            await db.Database.ExecuteSqlRawAsync("""UPDATE "KernelStoreVersions" SET "MessageProtocol" = "MessageProtocol" + 100""", stop);
        try
        {
            await reopened.InboxAsync(blue, actor, first.Next, stop);
            throw new InvalidOperationException("The inbox accepted a different marked protocol.");
        }
        catch (StoreFormatException)
        {
            Console.WriteLine("ok the existing reader refuses a changed format in the page statement");
        }
        return 0;

        async Task<Contract.ActorRef> Birth(Contract.BoundScope scope, string id)
        {
            var accepted = await ingress.SendAsync(scope, new Contract.Target.Start(new(probe.Type, "{}")), id,
                Contract.Kinds.Start, cancellationToken: stop);
            return accepted.Actor ?? throw new InvalidOperationException("The fixture birth was refused.");
        }

        ValueTask<Contract.Admission> Send(Contract.BoundScope scope, Contract.ActorRef target, string id, string messageKind, string? payload, long due) =>
            ingress.SendAsync(scope, new Contract.Target.Ref(target), id, messageKind, payload, new(DueAt: due), stop);

        async Task<Worker> Own(string node)
        {
            var ownerStore = Store();
            var roots = (await ownerStore.BindWorkerPartitionAsync(0, stop)).Scope;
            var worker = new Worker(node, 0, ownerStore, new ActorAdapter(new EfStore.RootHeaders(roots), probe));
            var host = new DurableHost([worker]);
            await host.HeartbeatAsync(Defaults.Lease, stop);
            return (Worker?)await host.ActivateAsync(0, stop) ?? throw new InvalidOperationException("The fixture could not take ownership.");
        }

        async Task Drive(Worker worker, Func<Task<bool>> finished)
        {
            while (!await finished())
            {
                await worker.WokenAsync(null, stop);
                await worker.RunAsync(stop);
                stop.ThrowIfCancellationRequested();
            }
        }

        async Task<bool> RejectToken(Contract.BoundScope scope, Contract.ActorRef target, Contract.PageToken token)
        {
            try { await read.InboxAsync(scope, target, token, stop); }
            catch (ArgumentException error) when (error.ParamName == "from") { return true; }
            return false;
        }
    }

    private sealed class Probe : Contract.IActorType
    {
        public string Type => "postgres-inbox-probe";
        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            turn.State = "{}";
            return default;
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => new((object?)null);
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }

    private static void Check(bool passed, string description)
    {
        if (!passed) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
