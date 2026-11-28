using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class UserTaskWaitFormatChecks
{
    private static readonly RootPublicEventHost EventHost = new("workflow", false);
    private static readonly StoreFormat Current = StoreFormat.RootShipped;
    private static readonly StoreFormat Previous = StoreFormat.RootShippedBeforeUserTasks;

    internal static async Task<int> RunAsync(string provider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var token = timeout.Token;
        Require(Current == (Previous with { MessageProtocol = Previous.MessageProtocol + 1 }), "The shipped task wait profile needs the next message protocol.");
        var capabilities = RootProfiles.CapabilitiesOf(Current);
        Require(capabilities == (RootProfiles.ShippedBeforeUserTasks | RootCapabilities.UserTaskWaits)
            && RootProfiles.MissingPrerequisites(capabilities) == RootCapabilities.None, "The task wait profile must preserve the shipped capability closure.");

        if (provider == "sqlite")
            await SqliteAsync(token);
        else
            await PostgresAsync(token);

        Console.WriteLine($"{provider}: mismatched owners were refused and the stopped shipped store advanced with its physical definitions unchanged.");
        return 0;
    }

    private static async Task SqliteAsync(CancellationToken token)
    {
        var folder = Directory.CreateTempSubdirectory("actor-user-task-format");
        try
        {
            foreach (var selected in new StoreFormat?[] { Previous, null })
            {
                var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(new SqliteConnectionStringBuilder
                {
                    DataSource = Path.Combine(folder.FullName, selected is null ? "shipped.db" : "previous.db"),
                    ForeignKeys = true,
                    Pooling = false
                }.ToString()).Options;
                var definitions = new SqliteAdmissionDefinitions();
                await using var current = KernelDb.ForSqliteRootShipped(options, selected, EventHost);
                await using var other = KernelDb.ForSqliteRootShipped(options, selected is null ? Previous : null, EventHost);

                await EfStore.InstallSqliteShippedAsync(current, 1, token);
                await VerifyMarkersAsync(current, other, token);
                await RefusesOtherFormatAsync(() => EfStore.InstallSqliteShippedAsync(other, 1, token));
                await RefusesOtherFormatAsync(() => EfStore.RequireSqliteRootAdmissionAsync(other, definitions, token));
                await EfStore.RequireSqliteRootAdmissionAsync(current, definitions, token);
                await VerifyMarkersAsync(current, other, token);
                if (selected == Previous)
                    await UpgradeSqliteAsync(options, definitions, token);
            }
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    private static async Task UpgradeSqliteAsync(DbContextOptions<KernelDb> options, SqliteAdmissionDefinitions definitions,
        CancellationToken token)
    {
        KernelDb Open(bool candidate) => KernelDb.ForSqliteRootRuntime(options, definitions, candidate ? null : Previous, EventHost);
        var previous = new EfStore(() => Open(candidate: false), postgres: false);
        var next = new EfStore(() => Open(candidate: true), postgres: false);
        await using var initial = Open(candidate: false);
        var path = new SqliteConnectionStringBuilder(initial.Database.GetConnectionString()!).DataSource;
        var actorType = WorkflowActor.WorkflowActor.ActorType;
        var prior = SqliteEmbeddedFoundation.ForShippedBeforeUserTasks(path, "previous-task-format", actorType);
        var candidate = new SqliteEmbeddedFoundation(path, "next-task-format", actorType);
        await prior.InstallShippedAsync(token);
        var registry = new WorkflowRegistry().Add(Wf.Workflow("old-event", Wf.Event("await-event", "upgrade-unblocks")));
        var model = new NativeExecutionModel(registry);
        var actor = new WorkflowActor.WorkflowActor([model]);
        var scope = EmbeddedTenant.Bind("scope-format-upgrade");
        await using var owner = prior.CreateHostedService(actor, reason => throw new InvalidOperationException(reason));
        await owner.StartAsync(token);
        var ingress = await prior.OpenIngressAsync(token);
        var started = await ingress.SendAsync(scope, new Contract.Target.Start(WorkflowActor.WorkflowActor.Start(
            NativeExecutionModel.Start("old-event"))), "prior-wait", Contract.Kinds.Start, cancellationToken: token);
        Require(started is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "The previous owner must admit a real Native workflow.");
        var id = started.Actor!.Value.Id;
        EfStore.RootImage? before;
        do
        {
            before = await previous.ReadFirstRootAsync(scope.Tenant, id, token);
            if (before?.State is not null) break;
            await Task.Delay(20, token);
        } while (true);
        Require(before is { StateVersion: > 0, State: not null, Status: InstanceStatus.Running } &&
            before.State.Contains("upgrade-unblocks", StringComparison.Ordinal), "The old owner must commit the ordinary Native wait.");
        try
        {
            await candidate.UpgradeShippedFormatAsync(token);
            throw new InvalidOperationException("The in-use store was allowed to advance.");
        }
        catch (StoreFormatException error) when (error.Message.Contains("Live node", StringComparison.Ordinal)) { }
        catch (IOException) { }

        await owner.StopAsync(token);
        // A clean stop relinquishes the file. The prior node's persisted advertisement remains fenced through its lease.
        while (true)
        {
            var now = await prior.ReadClockAsync(token);
            if (!await initial.Nodes.AnyAsync(node => node.LeaseUntil >= now, token)) break;
            await Task.Delay(100, token);
        }
        await RejectWrongAuthorityAsync(async wrong =>
        {
            var selected = new EfStore(() => KernelDb.ForSqliteRootRuntime(options, definitions, Previous, wrong), postgres: false);
            await selected.UpgradeShippedRootFormatAsync(token);
        });
        await using (var wrongMarker = Open(candidate: true))
            await RefusesOtherFormatAsync(() => EfStore.RequireSqliteRootAdmissionAsync(wrongMarker, definitions, token));
        Require(await candidate.UpgradeShippedFormatAsync(token), "A stopped shipped SQLite file must advance exactly once.");
        Require(!await candidate.UpgradeShippedFormatAsync(token), "A resumed SQLite upgrade must be idempotent.");
        var wrongScope = SqliteEmbeddedFoundation.WithRootStartProtection(path, "incorrect-actor-owner", actorType,
            (_, _) => ValueTask.FromResult(true), EmbeddedTenant.Bind("other-publisher"));
        await RefusesOtherFormatAsync(async () => await wrongScope.UpgradeShippedFormatAsync(token));
        await using var current = Open(candidate: true);
        await using var old = Open(candidate: false);
        await VerifyMarkersAsync(current, old, token);
        await RefusesOtherFormatAsync(() => EfStore.RequireSqliteRootAdmissionAsync(old, definitions, token));
        var after = await next.ReadFirstRootAsync(scope.Tenant, id, token);
        Require(after is not null && after.State == before!.State && after.Revision == before.Revision && after.StateVersion == before.StateVersion,
            "Advancing the marker must preserve the old ordinary workflow checkpoint and its revision.");
    }

    private static async Task PostgresAsync(CancellationToken token)
    {
        var shippedConnection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply an empty owned PostgreSQL database for the shipped profile.");
        var candidateConnection = Environment.GetEnvironmentVariable("KERNEL_USER_TASK_WAIT_POSTGRES")
            ?? throw new InvalidOperationException("Supply a second empty owned PostgreSQL database for the task wait profile.");

        foreach (var (connection, useCandidate) in new[] { (shippedConnection, false), (candidateConnection, true) })
        {
            var staged = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(new NpgsqlConnectionStringBuilder(connection)
            {
                Pooling = false
            }.ConnectionString).Options;
            KernelDb Open(bool candidate) => candidate
                ? KernelDb.ForPostgresRootShipped(staged, EventHost)
                : KernelDb.ForPostgresRootShippedBeforeUserTasks(staged, EventHost);
            await using var initial = Open(useCandidate);
            Require(await PostgresFixtureDatabase.IsEmptyAsync(initial, token), "Each PostgreSQL qualification database must be empty.");
            var store = new EfStore(() => Open(useCandidate), postgres: true);
            await store.CreateAsync(1);
            await store.InstallPreviewRootSchemaAsync(token);
            await using var current = Open(useCandidate);
            await using var other = Open(!useCandidate);

            await VerifyMarkersAsync(current, other, token);
            await RefusesOtherFormatAsync(() => EfStore.RequireStoreVersionsAsync(other, token));
            await EfStore.RequireStoreVersionsAsync(current, token);
            await VerifyMarkersAsync(current, other, token);
            if (!useCandidate)
            {
                await store.NodeHeartbeatAsync("previous-task-format", TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), token);
                try
                {
                    await store.UpgradeShippedRootFormatAsync(token);
                    throw new InvalidOperationException("The live prior PostgreSQL owner was allowed to advance.");
                }
                catch (StoreFormatException error) when (error.Message.Contains("Live node", StringComparison.Ordinal)) { }
                await VerifyMarkersAsync(current, other, token);
                var released = await current.Nodes.Where(node => node.Id == "previous-task-format")
                    .ExecuteUpdateAsync(set => set.SetProperty(node => node.LeaseUntil, 0), token);
                Require(released == 1, "The fixture must release only the selected prior owner.");
                await RejectWrongAuthorityAsync(async wrong =>
                {
                    var selected = new EfStore(() => KernelDb.ForPostgresRootShippedBeforeUserTasks(staged, wrong), postgres: true);
                    await selected.UpgradeShippedRootFormatAsync(token);
                });
                await VerifyMarkersAsync(current, other, token);
                // The canonical installer treats caller-key receipts as authoritative. The migration must refuse before
                // advancing any marker when that relation is absent.
                await current.Database.ExecuteSqlRawAsync("""ALTER TABLE "FoundationKeyReceipts" RENAME TO "WithheldTaskUpgradeKeyReceipts" """, token);
                try
                {
                    await RefusesOtherFormatAsync(async () => await UpgradeAsync());
                    await VerifyMarkersAsync(current, other, token);
                }
                finally
                {
                    await current.Database.ExecuteSqlRawAsync("""ALTER TABLE "WithheldTaskUpgradeKeyReceipts" RENAME TO "FoundationKeyReceipts" """, token);
                }
                Require(await UpgradeAsync(), "A stopped prior PostgreSQL store must advance and reopen exactly once.");
                Require(!await UpgradeAsync(), "A resumed PostgreSQL upgrade must verify current authority and be idempotent.");
                Task<bool> UpgradeAsync() => PostgresPreviewHosting.UpgradeShippedFormatAsync(staged, EventHost.ActorType,
                    EventHost.ProtectsStarts, publisherScope: null, token);
                await VerifyMarkersAsync(other, current, token);
                await RefusesOtherFormatAsync(() => EfStore.RequireStoreVersionsAsync(current, token));
                await EfStore.RequireStoreVersionsAsync(other, token);
            }
        }
    }

    private static async Task RejectWrongAuthorityAsync(Func<RootPublicEventHost, Task> upgrade)
    {
        await RefusesOtherFormatAsync(() => upgrade(EventHost with { AuthoredStandaloneTenant = "different-publisher" }));
        await RefusesOtherFormatAsync(() => upgrade(EventHost with { ProtectsStarts = !EventHost.ProtectsStarts }));
    }

    private static async Task VerifyMarkersAsync(KernelDb current, KernelDb other, CancellationToken token)
    {
        var markers = await current.StoreVersions.AsNoTracking()
            .Where(marker => marker.Id == StoreVersionEntity.MarkerId || marker.Id == StoreDefinitionsRevision.MarkerId)
            .ToDictionaryAsync(marker => marker.Id, token);
        Require(markers[StoreVersionEntity.MarkerId].Format == current.RequiredFormat,
            "The installed marker must retain its selected message protocol.");
        var expected = current.RequiredFormat == Current ? RootProfiles.Shipped : RootProfiles.ShippedBeforeUserTasks;
        Require(current.Capabilities == expected, "The physical profile must select its expected capability set.");
        var definitions = StoreDefinitionsRevision.Seed(current) ?? throw new InvalidOperationException("The physical definitions marker is required.");
        var otherDefinitions = StoreDefinitionsRevision.Seed(other) ?? throw new InvalidOperationException("Both profiles require physical definitions markers.");
        Require(markers[StoreDefinitionsRevision.MarkerId].Format == definitions.Format && definitions.Format == otherDefinitions.Format,
            "Only the wire protocol changes between these profiles.");
    }

    private static async Task RefusesOtherFormatAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            throw new InvalidOperationException("The physical provider accepted a different message protocol.");
        }
        catch (StoreFormatException)
        {
        }
    }

    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException(reason);
    }
}
