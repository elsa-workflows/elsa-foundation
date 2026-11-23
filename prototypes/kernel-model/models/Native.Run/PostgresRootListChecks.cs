using System.Globalization;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Contracts;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Actor = Kernel.WorkflowActor.WorkflowActor;
using PublicKinds = Kernel.Foundation.Contracts.Kinds;

namespace Kernel.Native.Run;

internal static class PostgresRootListChecks
{
    internal static ActorQuery ApprovalQuery()
    {
        var version = new NativeExecutionModel(new WorkflowRegistry()).StateVersion.ToString(CultureInfo.InvariantCulture);
        return new(Actor.ActorType, ActorStatus.Running,
            new Dictionary<string, string> { ["definition"] = DirectApprovalWorkflow.Definition, ["version"] = version }, Parked: false);
    }

    internal static async Task<int> RunAsync(bool owned, bool inBatch, PageToken? saved)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a fresh dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootOperators(options);
        var store = new EfStore(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var blue = FixedTenantScope.Bind("list-blue");
        var query = ApprovalQuery();
        await using var db = Open();
        if (saved is null)
        {
            Check(await db.Database.SqlQueryRaw<bool>("""
                SELECT NOT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = current_schema()) AS "Value"
                """).SingleAsync(ct), "the projection fixture starts with an empty database");
            await store.CreateAsync(1);
            await admission.InstallAsync(ct);
        }
        var reads = new PostgresRootReads(store, await admission.OpenJournalCursorAsync(ct), 2);
        var foundation = new PostgresPreviewFoundation(admission, store, reads, Actor.ActorType, (scope, _) => scope == blue);
        IActorReads api = foundation;

        if (saved is not null)
        {
            // The cold process is denied access to the owner and journal tables before opening its list.
            var first = await api.ListAsync(blue, query, cancellationToken: ct);
            var next = await api.ListAsync(blue, query with
            {
                Labels = new Dictionary<string, string> { ["version"] = query.Labels!["version"], ["definition"] = DirectApprovalWorkflow.Definition }
            }, saved, ct);
            var final = await api.ListAsync(blue, query, next.Next, ct);
            var actual = first.Items.Concat(next.Items).Concat(final.Items).Select(v => v.Header.Ref.Id).ToArray();
            Check(first.Items.Count == 2 && next is { Items.Count: 2, Next: not null } && final is { Items.Count: 1, Next: null } &&
                actual.Distinct().Count() == 5 && actual.SequenceEqual(actual.Order(StringComparer.Ordinal)),
                "a saved token crosses a fresh process and three bounded pages without reading owner state or the journal");
            Check(await Refused(blue, query with { Status = ActorStatus.Finished }) &&
                await Refused(blue, query with { Labels = null }) && await Refused(blue, query with { EndedBefore = 10 }) &&
                await Refused(blue, query with { Parked = null }) && await Refused(blue, query with { Type = null }),
                "continuations are bound to every query filter");
            var tampered = new PageToken((saved.Value.Value[0] == 'A' ? 'B' : 'A') + saved.Value.Value[1..]);
            try { await api.ListAsync(blue, query, tampered, ct); throw new InvalidOperationException("Accepted modified token."); }
            catch (ArgumentException) { }
            try { await api.TailAsync(blue, first.Items[0].Header.Ref, saved, ct); throw new InvalidOperationException("Accepted list token on tail."); }
            catch (ArgumentException) { }
            Check(first.Items.All(item => item.AsOf == first.AsOf) && first.AsOf <= next.AsOf && next.AsOf <= final.AsOf &&
                next.Items.All(item => item.AsOf == next.AsOf) && final.Items.All(item => item.AsOf == final.AsOf), "each returned page has its own database clock");
            Console.WriteLine($"passed projection restart {Environment.ProcessId}");
            return 0;
        }

        var workflow = new WorkflowRuntime(foundation, api);
        var approvals = new List<ActorRef>();
        for (var i = 0; i < 5; i++) approvals.Add(await Start(blue, "approval-" + i));
        var neighbor = new ActorRef(await PostgresMultiRootClient.CallSecondaryAsync(ct, "operator-start"));
        var unusual = await Start(blue, "odd-labels");
        var completed = await Start(blue, "completed", "terminal");
        Check((await api.ListAsync(blue, query, cancellationToken: ct)).Items.Count == 0,
            "admitted creators do not project uncommitted actors");
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build())
            .Add(Wf.Workflow("terminal", Wf.Node<PostgresFlowchartPreview.Approval>("approval")));
        var prepared = await PostgresNativeRootOwner.PreparePartitionAsync(store, "root-list", 0, registry, 16, ct);
        await using (var owner = await prepared.TryActivateAsync(ct) ?? throw new InvalidOperationException("Expected a fresh partition owner."))
        {
            while (await api.GetAsync(blue, completed, ct) is null || await store.ReadFirstRootAsync(PostgresMultiRootClient.Secondary, neighbor.Id, ct) is null) await Pump(owner);
            var end = (await store.ReadFirstRootAsync(blue.Tenant, completed.Id, ct))!;
            var frame = Json.Read<NativeState>(end.State!).Frames.Values.Single().Id;
            var approval = Json.Write(new CompositeSignal(frame, "approve", "done"));
            await Send(blue, completed, "finish", CompositeSignal.Kind, approval);
            var journal = await api.TailAsync(blue, approvals[0], cancellationToken: ct);
            await Send(blue, approvals[0], "suspend", PublicKinds.Suspend);
            while ((await store.ReadFirstRootAsync(blue.Tenant, completed.Id, ct))?.Status != InstanceStatus.Finished ||
                (await store.ReadFirstRootAsync(blue.Tenant, approvals[0].Id, ct))?.Suspended != true) await Pump(owner);
            var all = await Collect(blue, query);
            Check(all.Select(row => row.Header.Ref).ToHashSet().IsSupersetOf(approvals), "public list projects all committed approvals");
            using var header = JsonDocument.Parse(all.Single(row => row.Header.Ref == approvals[0]).Header.TypeData);
            Check(header.RootElement.GetProperty("Suspended").GetBoolean() &&
                (await api.TailAsync(blue, approvals[0], journal.Next, ct)).Items.Count == 0,
                "a journal-silent suspension updates the persisted public metadata");
            Check(all.All(row => row.Park is null), "suspension leaves the parked filter false");
        }

        await Send(blue, unusual, "odd-labels", "unused");
        var image = (await store.ReadFirstRootAsync(blue.Tenant, unusual.Id, ct))!;
        var input = (await admission.ReadPendingDirectAsync(blue.Tenant, unusual.Id, ct)).Single();
        await store.HeartbeatAsync("list-metadata", Defaults.Lease, ct);
        var ownerFence = await store.TakeAsync(0, "list-metadata", Defaults.Lease, cancellationToken: ct)
            ?? throw new InvalidOperationException("Expected the released test partition.");
        var fence = new PartitionFence(0, ownerFence.Epoch, ownerFence.Position);
        var odd = new Dictionary<string, string> { ["\0"] = "\uD800", ["\\u0000"] = "\uFFFD", ["with/slash"] = "\\\0" };
        Check((await store.CommitRootTurnAsync(blue.Tenant, unusual.Id, image, [input], fence, Defaults.Lease, InstanceStatus.Running,
            image.State, image.StateVersion, image.TypeData, odd, null, ct)).Landed, "the fenced writer accepts ordinal labels outside PostgreSQL text");
        foreach (var label in odd)
        {
            var asked = new ActorQuery(Labels: new Dictionary<string, string> { [label.Key] = label.Value });
            var match = await api.ListAsync(blue, asked, cancellationToken: ct);
            Check(match.Items is [var row] && row.Header.Ref == unusual && row.Header.Labels[label.Key] == label.Value,
                "projection filters and returns the original UTF-16 label code units");
        }
        Check((await api.ListAsync(blue, new(Labels: new Dictionary<string, string> { ["\0"] = "\uFFFD" }), cancellationToken: ct)).Items.Count == 0,
            "escaped label values cannot match a different value or a different key");

        var rollbackFilter = new ActorQuery(Labels: new Dictionary<string, string> { ["transient"] = "rollback" });
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            var uncommitted = DurableKeyJson.Write(new { image.Model, image.Definition, image.TypeData,
                Labels = rollbackFilter.Labels!.ToArray(), EscapedState = false });
            Check(await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Instances" SET "FoundationHeader" = {uncommitted}, "Revision" = "Revision" + 1,
                    "FoundationWriteRevision" = "Revision" + 1
                WHERE "FoundationTenant" = {blue.Tenant} AND "FoundationReference" = {unusual.Id}
                """, ct) == 1, "the database stages a projection change before commit");
            Check((await api.ListAsync(blue, rollbackFilter, cancellationToken: ct)).Items.Count == 0,
                "a separate public reader cannot see uncommitted projection metadata");
            await transaction.RollbackAsync(ct);
        }
        Check((await api.ListAsync(blue, rollbackFilter, cancellationToken: ct)).Items.Count == 0 &&
            (await api.ListAsync(blue, new(Labels: odd), cancellationToken: ct)).Items.Single().Header.Ref == unusual,
            "rollback preserves the prior persisted metadata");

        var terminal = (await api.ListAsync(blue, new(Status: ActorStatus.Finished), cancellationToken: ct)).Items.Single();
        var ended = terminal.Header.EndedAt!.Value;
        Check(terminal.Header.Ref == completed && (await api.ListAsync(blue, new(EndedBefore: ended), cancellationToken: ct)).Items.Count == 0 &&
            (await api.ListAsync(blue, new(EndedBefore: ended + 1), cancellationToken: ct)).Items is [var finished] && finished.Header.Ref == completed,
            "the persisted end time accepts only a strict end bound");
        Check((await api.ListAsync(blue, query with { Parked = true }, cancellationToken: ct)).Items.Count == 0 &&
            (await api.ListAsync(blue, query with { Type = "wrong" }, cancellationToken: ct)).Items.Count == 0,
            "actor type and park state each restrict the list");

        // Simulate upgrading an older database, then make owner and tail tables inaccessible to the next process.
        await db.Database.ExecuteSqlRawAsync("""
            DROP TRIGGER kernel_project_root_actor_list ON "Instances";
            DROP TABLE "FoundationActorList";
            """, ct);
        await admission.InstallAsync(ct);
        await admission.InstallAsync(ct);
        var page = await api.ListAsync(blue, query, cancellationToken: ct);
        Check(page.Items.Count == 2 && page.Next is not null && (await Collect(blue, query)).Count == 5,
            "idempotent installation backfills existing public metadata");
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" RENAME TO "FixtureOwnerRows";
            ALTER TABLE "Journal" RENAME TO "FixtureJournalRows";
            """, ct);
        await PostgresMultiRootClient.CallSecondaryAsync(ct, "operator-list", neighbor.Id, page.Next!.Value.Value);
        Console.WriteLine($"projection-bookmark {page.Next!.Value.Value}");
        Console.WriteLine($"passed projection birth {Environment.ProcessId}");
        return 0;

        async Task<ActorRef> Start(BoundScope scope, string id, string definition = DirectApprovalWorkflow.Definition) =>
            (await workflow.StartAsync(scope, id, NativeExecutionModel.Start(definition), cancellationToken: ct)).Actor!.Value;
        async Task Send(BoundScope scope, ActorRef actor, string id, string kind, string? payload = null) =>
            Check((await foundation.SendAsync(scope, new Target.Ref(actor), id, kind, payload, cancellationToken: ct)).Outcome == AdmissionOutcome.Accepted,
                "input admitted through the public surface");
        async Task Pump(IScopeActivation owner)
        {
            await owner.WokenAsync(null, ct);
            await owner.RunAsync(ct);
        }
        async Task<List<ActorView>> Collect(BoundScope scope, ActorQuery filter)
        {
            var items = new List<ActorView>();
            PageToken? from = null;
            do
            {
                var read = await api.ListAsync(scope, filter, from, ct);
                items.AddRange(read.Items);
                from = read.Next;
            } while (from is not null);
            return items;
        }
        async Task<bool> Refused(BoundScope scope, ActorQuery filter)
        {
            try { await api.ListAsync(scope, filter, saved, ct); return false; }
            catch (ArgumentException) { return true; }
        }
    }

    private static void Check(bool passed, string message)
    {
        if (!passed) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
