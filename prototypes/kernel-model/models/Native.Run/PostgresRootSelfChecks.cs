using System.Text;
using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class PostgresRootSelfChecks
{
    internal static async Task<int> RunAsync(bool owned, bool inBatch)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a dedicated PostgreSQL preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => new(options);
        EfStore Client() => new(Open, postgres: true, fenceInBatch: inBatch)
            { CommitSql = owned ? CommitSql.Owned : CommitSql.EntityFramework };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var stop = timeout.Token;
        var bare = Client();
        var scoped = Client();
        var ingress = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        const string tenant = "issued-content";
        await bare.CreateAsync(1);
        await ingress.InstallAsync(stop);
        var reference = (await ingress.AdmitAsync(tenant, "birth", new("native", "wait"), cancellationToken: stop)).Reference;
        var creator = (await ingress.ReadPendingCreatorAsync(tenant, reference, stop))!;
        var actor = creator.Target.Id;
        var window = TimeSpan.FromMinutes(1);
        var lease = (await bare.TakeAsync(0, "check", window, stop))!;
        Check((await bare.CommitFirstRootAsync(tenant, reference, creator, lease.For(0), window, null, 1,
            "state", new Dictionary<string, string>(), """["start"]""", stop)).Landed, "creator");
        await scoped.EnableRootSelfSendsAsync(stop);
        await scoped.BindWorkerRootsAsync([(tenant, reference)], stop);
        var row = (await scoped.LoadAsync([actor], stop)).Single();
        await ingress.AdmitDirectAsync(tenant, reference, "next", "step", cancellationToken: stop);
        var direct = (await ingress.ReadPendingDirectAsync(tenant, reference, stop)).Single();
        const string suffix = "\0\ud800\u007f\udfff\U0001f642\n";
        var payload = new string('"', FoundationMetadataLimits.MessageContentBytes - Encoding.UTF8.GetByteCount(suffix)) + suffix;
        var logical = scoped.RootSelfIssuer!(FoundationSelfSend.Seed(CreationProtocol.Origin(actor, 1), 2, 0));
        var sent = FoundationSelfSend.Create(actor, 1, logical, "ordinary\0step", payload);
        CommitBatch Batch(params TurnCommit[] turns) => new(0, lease.Epoch, 1, window, false, turns, [], []);
        var emit = new TurnCommit(actor, row with { Revision = 2 }, 1, [], [direct.InboxKey], [sent], [], [], null);
        Check((await scoped.CommitAsync(Batch(emit), stop)).Landed, "silent checkpoint and full-size emitted input");
        var key = sent.InboxKey;
        var stored = key with { Scope = EfStore.RootSelfScope };
        var empty = new TurnCommit(actor, null, null, [], [], [], [], [], null);
        foreach (var change in new[]
        {
            Batch() with { Seen = [stored] }, Batch() with { Trial = [stored] }, Batch(empty with { Held = [stored] }),
            Batch(empty with { Done = [stored] }), Batch(empty with { Consumed = [stored] })
        })
        {
            bool landed;
            try { landed = (await bare.CommitAsync(change, stop)).Landed; }
            catch (StoreException error) when (error.Failure == Failure.Definite) { landed = false; }
            Check(!landed, "unqualified disposition refused");
        }

        var ordinary = new Message(key.Id, creator.Target, "!foundation-self/1", "application body").AsIssued();
        Check((await bare.CommitAsync(Batch(empty with { Sent = [ordinary] }), stop)).Landed,
            "ordinary issued input with the same ID and stored kind can coexist");

        var cold = Client();
        await cold.EnableRootSelfSendsAsync(stop);
        await cold.BindWorkerRootsAsync([(tenant, reference)], stop);
        var pending = (await cold.PollAsync(0, lease.Epoch, TimeSpan.Zero, 1, stop)).Messages.Single();
        Check(pending is { Seen: 0, Trial: 0, Attempts: 0 } && pending.Message == sent,
            "cold poll returns the original full-size input and unchanged delivery state");
        Check((await bare.CommitAsync(Batch(empty with { Consumed = [ordinary.InboxKey] }), stop)).Landed,
            "ordinary issued input retains its generic store contract");
        await using (var db = Open())
        {
            var bytes = await db.Database.SqlQuery<int>($"""
                SELECT octet_length("FoundationSelfContent") AS "Value" FROM "Messages" WHERE "Id" = {key.Id} AND "InboxScope" = {EfStore.RootSelfScope}
                """).SingleAsync(stop);
            Check(bytes == FoundationMetadataLimits.MessageContentBytes && await db.Journal.CountAsync(stop) == 1,
                "physical content occupies its exact application-byte budget, with one journal position");
        }
        row = (await cold.LoadAsync([actor], stop)).Single();
        Check(row.Revision == 2 && (await cold.CommitAsync(Batch() with { Seen = [key] }, stop)).Landed,
            "qualified owner can mark the unchanged input");
        Check((await cold.CommitAsync(Batch(new TurnCommit(actor, row with { Revision = 3 }, 2, [key], [], [], [], [], null)), stop)).Landed,
            "qualified owner consumes the exact input with the next silent checkpoint");

        return 0;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("ok " + name);
    }
}
