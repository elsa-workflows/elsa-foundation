using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Kernel.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class InlineRootPromotionChecks
{
    internal static async Task<int> RunAsync(bool postgres, bool fenceInBatch)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = timeout.Token;
        var scope = ContractScopes.Bind("inline-proof");
        var directory = postgres ? null : Directory.CreateTempSubdirectory("inline-promotion-");
        try
        {
            var options = new DbContextOptionsBuilder<KernelDb>();
            if (postgres)
                options.UseNpgsql(Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES") ??
                    throw new InvalidOperationException("Supply a fresh PostgreSQL fixture."));
            else
                options.UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(directory!.FullName, "inline-promotion.db"), ForeignKeys = true, Pooling = false
                }.ToString());
            var sqliteDefinitions = new SqliteAdmissionDefinitions();
            KernelDb Open() => postgres ? KernelDb.ForPostgresRootPreparedStartsQualification(options.Options)
                : KernelDb.ForSqliteRootRuntime(options.Options, sqliteDefinitions, StoreFormat.RootPreparedStartsQualification);

            var store = new EfStore(Open, postgres, fenceInBatch) { CommitSql = CommitSql.Owned };
            await using (var db = Open())
            {
                if (postgres)
                    Check(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the promotion fixture uses an empty PostgreSQL database");
                else await EfStore.InstallSqliteShippedAsync(db, 1, token);
            }
            if (postgres)
            {
                await store.CreateAsync(1);
                await store.InstallPreviewRootSchemaAsync(token);
            }
            var lease = TimeSpan.FromSeconds(10);
            var fence = await TakeAsync("inline-first");
            byte[] key;
            await using (var db = Open())
                key = postgres ? await PostgresRootAdmission.ReadRootIssuerKeyAsync(db, token)
                    : await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token);

            var (later, laterTurn) = Sealed("late-first-write", 3);
            try
            {
                await PromoteAsync(later, laterTurn, fence, (_, _) =>
                    throw new InvalidOperationException("An unsupported revision entered protection."));
                throw new InvalidOperationException("An unsupported initial revision was accepted.");
            }
            catch (NotSupportedException)
            {
                Check(await CountAsync(later, "FoundationPreparedRootStarts", "Actor", token) == 0,
                    "a revision that the installed history projector cannot seed is refused before preparation");
            }

            var (creator, turn) = Sealed("selected-operation", 1);
            var selected = creator.FoundationRootBinding!.Value.Reference;
            var result = await PromoteAsync(creator, turn, fence, async (offer, ct) =>
            {
                Check(offer.Tenant == scope.Tenant && offer.Actor.Id == selected &&
                    await CountAsync(creator, "FoundationPreparedRootStarts", "Actor", ct) == 1 &&
                    await CountAsync(creator, "FoundationRootBirths", "Actor", ct) == 0 &&
                    await store.ReadFirstRootAsync(scope.Tenant, selected, ct) is null,
                    "the callback sees committed preparation and no admitted run");
                return true;
            });
            Check(result is { Landed: true } &&
                await store.ReadFirstRootAsync(scope.Tenant, selected, token) is { Revision: 1, Status: InstanceStatus.Running } &&
                await store.ReadFirstRootAsync("other-tenant", selected, token) is null &&
                await CountAsync(creator, "Subscriptions", "Instance", token) == 1 &&
                await CountAsync(creator, "Journal", "Instance", token) == 1 &&
                await CountAsync(creator, "Messages", "Target", token) == 0 &&
                await CountAsync(creator, "FoundationPreparedRootStarts", "Actor", token) == 0,
                "the exact actor, revision, wait and journal commit without a replay creator");

            fence = fence with { Position = fence.Position + 1 };
            var (rejected, rejectedTurn) = Sealed("rejected-material", 1);
            Check(await PromoteAsync(rejected, rejectedTurn, fence, (_, _) => ValueTask.FromResult(false)) is { Landed: false } &&
                await CountAsync(rejected, "FoundationPreparedRootStarts", "Actor", token) == 0 &&
                await CountAsync(rejected, "FoundationRootBirths", "Actor", token) == 0 &&
                await CountAsync(rejected, "Instances", "Id", token) == 0,
                "a definitive protector refusal disarms the exact stage without admitting the actor");

            var (duplicate, duplicateTurn) = Sealed("selected-operation", 1);
            Check(await PromoteAsync(duplicate, duplicateTurn, fence, (_, _) => ValueTask.FromResult(true)) is { Landed: false } &&
                await CountAsync(duplicate, "FoundationRootBirths", "Actor", token) == 0 &&
                await CountAsync(duplicate, "Instances", "Id", token) == 0 &&
                await CountAsync(duplicate, "Subscriptions", "Instance", token) == 0 &&
                await CountAsync(duplicate, "Journal", "Instance", token) == 0,
                "a live competing receipt rolls back every promoted row");

            var (stale, staleTurn) = Sealed("stale-owner", 1);
            var staleFence = fence;
            Check(await PromoteAsync(stale, staleTurn, staleFence, async (_, ct) =>
            {
                await store.ReleaseAsync(staleFence.Partition, staleFence.Epoch, ct);
                fence = await TakeAsync("inline-successor");
                return true;
            }) is { Landed: false } &&
                await CountAsync(stale, "FoundationRootBirths", "Actor", token) == 0 &&
                await CountAsync(stale, "Instances", "Id", token) == 0 &&
                await CountAsync(stale, "Subscriptions", "Instance", token) == 0 &&
                await CountAsync(stale, "Journal", "Instance", token) == 0,
                "owner movement during executable protection leaves no durable continuation");
            await store.ReleaseAsync(fence.Partition, fence.Epoch, token);
            return 0;

            async Task<PartitionFence> TakeAsync(string node) =>
                (await store.TakeAsync(0, node, lease, token))?.For(0) ??
                throw new InvalidOperationException("The fixture could not take the owner fence.");

            ValueTask<Committed> PromoteAsync(Message message, TurnCommit sealedTurn, PartitionFence owned, ProtectRootStartAsync protect) =>
                store.PromoteInlineRootAsync(scope, message, sealedTurn, owned, lease, TimeSpan.FromHours(1), protect, token);

            (Message Creator, TurnCommit Turn) Sealed(string operation, long revision)
            {
                var address = InstanceRef.In(0, scope.Tenant + "_" + Guid.NewGuid().ToString("N"));
                var reference = FoundationRootCreator.ReferenceFor(key, scope.Tenant, new(address.Id, 1));
                var storedOperation = RootPreparedStarts.InlineOperation(operation);
                var creator = FoundationRootCreator.Create(address, storedOperation, FoundationRootCreator.OperationDigest(storedOperation),
                    new StartSpec("workflow", "sealed", Ephemeral: true) { FoundationInlineTenant = scope.Tenant }, null, null)
                    with { FoundationRootBinding = (address.Id, scope.Tenant, reference) };
                var header = new FoundationHeaderChange("selected-model", new Dictionary<string, string> { ["definition"] = "sealed" });
                var row = new InstanceRow(address.Id, "workflow", "sealed", InstanceStatus.Running, """{"wait":true}""", revision,
                    Parent: null, StateVersion: 1, Creation: 1,
                    StartIntent: CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload))
                {
                    FoundationInlineBirth = (scope.Tenant, creator.InboxKey),
                    FoundationDispatchHeader = new(scope.Tenant, reference, header)
                };
                var wait = new SubscriptionRow(scope.Tenant + "_event", address.Id, "resume", Creation: 1) { FoundationTenant = scope.Tenant };
                return (creator, new(address.Id, row, null, [], [], [], [wait], [], """["start","waiting"]""")
                    { FoundationHeader = header });
            }

            async Task<int> CountAsync(Message message, string table, string column, CancellationToken ct)
            {
                await using var db = Open();
                return await db.Database.SqlQueryRaw<int>($$"""
                    SELECT CAST(count(*) AS integer) AS "Value" FROM "{{table}}" WHERE "{{column}}" = {0}
                    """, message.Target.Id).SingleAsync(ct);
            }
        }
        finally { directory?.Delete(recursive: true); }
    }

    private static void Check(bool result, string description)
    {
        if (!result) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
