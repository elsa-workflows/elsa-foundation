using Kernel.EntityFramework;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using PublicActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresRootHistoryChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("This check needs a fresh dedicated PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => new(options);
        EfStore Store() => new(Open, postgres: true, fenceInBatch: inBatch) { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = timeout.Token;
        var reader = Store();
        await reader.CreateAsync(1);
        var admissions = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        await admissions.InstallAsync(ct);
        var journal = new PostgresRootReads(reader, await admissions.OpenJournalCursorAsync(ct), 128);
        var api = new PostgresPreviewFoundation(admissions, reader, journal, PublicActor.ActorType);
        var scope = FixedTenantScope.Bind("preview-history");
        var registry = new WorkflowRegistry().Add(DirectApprovalWorkflow.Build());
        await using var db = Open();
        Task<long> Position() => db.Partitions.Select(p => p.Position).SingleAsync(ct);
        Task<long> Clock() => db.Database.SqlQueryRaw<long>("""SELECT floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Value" """).SingleAsync(ct);

        foreach (var history in new[] { Contract.History.Minimal, Contract.History.Full })
        {
            var delayed = history == Contract.History.Full;
            var originalPosition = await Position();
            var due = delayed ? await Clock() + 4_000 : (long?)null;
            var workflow = NativeExecutionModel.Start(DirectApprovalWorkflow.Definition) with { History = history };
            var started = await api.SendAsync(scope, new Contract.Target.Start(PublicActor.Start(workflow)), "start-" + history,
                Contract.Kinds.Start, options: new(DueAt: due), cancellationToken: ct);
            var actor = started.Actor!.Value;
            Check(started.Outcome == Contract.AdmissionOutcome.Accepted, $"{history} workflow start admitted");
            async Task Send(string id, string kind, string? payload = null)
            {
                var receipt = await api.SendAsync(scope, new Contract.Target.Ref(actor), id, kind, payload, cancellationToken: ct);
                Check(receipt.Outcome == Contract.AdmissionOutcome.Accepted && receipt.Actor == actor, $"{history} input {id} admitted for the original actor");
            }

            if (delayed) await Send("ordinary", "unmatched");
            var store = Store();
            await using var active = await PostgresNativeRootOwner.ActivateAsync(store, "history-owner", scope.Tenant, actor.Id, registry, ct);
            async Task Pass()
            {
                await active.WokenAsync(null, ct);
                await active.RunAsync(ct);
                if (!((Worker)active).Owns) throw new InvalidOperationException($"{history} workflow owner lost its partition.");
            }
            async Task<EfStore.RootImage> Drive(int revision)
            {
                while (true)
                {
                    var image = await reader.ReadFirstRootAsync(scope.Tenant, actor.Id, ct);
                    if (image?.Revision >= revision) return image;
                    await Pass();
                    await Task.Delay(10, ct);
                }
            }
            if (delayed)
            {
                var epoch = await db.Partitions.Select(p => p.Epoch).SingleAsync(ct);
                var now = await store.PollAsync(0, epoch, TimeSpan.Zero, 1, ct);
                var future = await store.PollAsync(0, epoch, TimeSpan.FromMinutes(1), 8, ct);
                Check(now.Now < due && now.Messages.Count == 0 &&
                    future.Messages is [var candidate] && candidate.Message.Kind == Kinds.Start,
                    "a ready direct input stays behind its pending creator even inside the poll horizon");
                await Pass();
                Check(await Clock() < due && await reader.ReadFirstRootAsync(scope.Tenant, actor.Id, ct) is null &&
                    (await admissions.ReadPendingDirectAsync(scope.Tenant, actor.Id, ct)).Count == 1 && await Position() == originalPosition,
                    "an early owner pass leaves the admitted direct input, actor and journal untouched");
                var delay = due!.Value - await Clock();
                if (delay >= 0) await Task.Delay(TimeSpan.FromMilliseconds(delay + 1), ct);
                var eligible = await store.PollAsync(0, epoch, TimeSpan.Zero, 8, ct);
                var creator = (await admissions.ReadPendingCreatorAsync(scope.Tenant, actor.Id, ct))!;
                var seen = await db.Messages.Where(m => m.Id == creator.InboxKey.Id && m.InboxScope == 0).Select(m => m.Seen).SingleAsync(ct);
                Check(eligible.Now >= due && (seen == epoch ? eligible.Messages.Count == 0 :
                    eligible.Messages is [var first] && first.Message.InboxKey == creator.InboxKey),
                    "at its deadline the exact creator is already seen or eligible while the direct input stays withheld");
            }
            var check = await Drive(delayed ? 2 : 1);
            var frame = Json.Read<NativeState>(check.State!).Frames.Single().Key;
            var startPage = await api.TailAsync(scope, actor, cancellationToken: ct);
            Check(startPage.Items.Count > 0 && startPage.Items.All(e => e.Revision == 1) && startPage.Next is not null &&
                await Position() == originalPosition + 1, $"{history} journal contains only the start revision");
            if (!delayed)
                await Send("approval-1", CompositeSignal.Kind, Json.Write(new CompositeSignal(frame, "approve", "Ada")));
            var silent = await Drive(2);
            var approvals = Json.Read<NativeState>(silent.State!).Frames[frame].State!.AsArray();
            Check(silent.Revision == 2 && silent.Status == InstanceStatus.Running && silent.History == (History)history &&
                approvals.Count == (delayed ? 0 : 1) && (delayed || approvals[0]!.GetValue<string>() == "Ada") &&
                (await admissions.ReadPendingDirectAsync(scope.Tenant, actor.Id, ct)).Count == 0 &&
                (await api.TailAsync(scope, actor, startPage.Next, ct)).Items.Count == 0 && await Position() == originalPosition + 1,
                $"{history} no-record turn consumes its input, advances the revision and preserves the journal position and tail");
            await Send("next", CompositeSignal.Kind, Json.Write(new CompositeSignal(frame, "approve", "Grace")));
            var next = await Drive(3);
            var physical = await reader.ReadRootJournalAsync(scope.Tenant, actor.Id, 0, 10, ct);
            var suffix = await api.TailAsync(scope, actor, startPage.Next, ct);
            Check(next.Revision == 3 && next.Status == (delayed ? InstanceStatus.Running : InstanceStatus.Finished) &&
                physical.Rows.Select(r => r.Revision).SequenceEqual([1L, 3L]) &&
                physical.Rows.Select(r => r.Position).SequenceEqual([originalPosition + 1, originalPosition + 2]) &&
                suffix.Items.Count > 0 && suffix.Items.All(e => e.Revision == 3) && await Position() == originalPosition + 2,
                $"{history} next recorded turn appends the contiguous position and resumes the saved public tail");
        }
        await CheckReaderOpenAsync(Open, admissions, ct);
        return 0;
    }

    private static async Task CheckReaderOpenAsync(Func<KernelDb> create, PostgresRootAdmission admissions, CancellationToken token)
    {
        await using var setup = create();
        var original = await setup.StoreVersions.AsNoTracking().SingleAsync(token);
        try
        {
            foreach (var (change, versions) in new (Action<StoreVersionEntity>, string)[]
            {
                (row => row.StoreSchema--, $"Stored schema={original.StoreSchema - 1},"),
                (row => row.MessageProtocol--, $"message protocol={original.MessageProtocol - 1},"),
                (row => row.JournalCodec++, $"journal codec={original.JournalCodec + 1}."),
            })
            {
                await using var db = create();
                var marker = await db.StoreVersions.SingleAsync(token);
                db.Entry(marker).CurrentValues.SetValues(original);
                change(marker);
                await db.SaveChangesAsync(token);
                await RequireReaderRefusal(versions);
            }

            await setup.StoreVersions.Where(row => row.Id == original.Id).ExecuteDeleteAsync(token);
            await RequireReaderRefusal("version marker is missing");
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await using var db = create();
            var marker = await db.StoreVersions.SingleOrDefaultAsync(row => row.Id == original.Id, cleanup.Token);
            if (marker is null) db.StoreVersions.Add(original);
            else db.Entry(marker).CurrentValues.SetValues(original);
            await db.SaveChangesAsync(cleanup.Token);
        }

        await admissions.OpenJournalCursorAsync(token);
        Check(true, "a public reader reopens its cursor after restoring the supported format");
        return;

        async Task RequireReaderRefusal(string details)
        {
            try { await admissions.OpenJournalCursorAsync(token); }
            catch (StoreFormatException error) when (error.Message.Contains(details, StringComparison.Ordinal))
            {
                Check(true, $"public reader startup refuses {details}");
                return;
            }
            throw new InvalidOperationException($"Public reader startup must refuse {details}.");
        }
    }

    private static void Check(bool valid, string description)
    {
        if (!valid) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
