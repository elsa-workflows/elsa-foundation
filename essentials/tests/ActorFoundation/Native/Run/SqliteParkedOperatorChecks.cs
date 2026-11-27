using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    internal static async Task<int> RunParkedOperatorsAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var token = budget.Token;
        var directory = Directory.CreateTempSubdirectory("sqlite-parked-operators");
        try
        {
            var file = Path.Combine(directory.FullName, "parked-operators.db");
            await InstallFoundationAsync(file, token);
            var scope = EmbeddedTenant.Bind("sqlite-parked-operators");
            var reads = SqliteEmbeddedHosting.CreateFoundationReads(file);
            var requests = SqliteEmbeddedHosting.CreateRequests(file, "park-control-node", ParkKind);
            var ingress = await SqliteEmbeddedHosting.OpenFoundationWithOperatorsAsync(file, ParkKind,
                (_, _, kind) => kind is Contract.Kinds.Terminate or Contract.Kinds.Delete, requests: requests, cancellationToken: token);
            var definitions = new SqliteAdmissionDefinitions();
            var store = new EfStore(() => OpenFoundationDb(file, definitions), postgres: false, fenceInBatch);
            await using var db = OpenFoundationDb(file);
            var plain = await AdmitAsync("plain", "throw-at-start");
            var delete = await AdmitAsync("delete", "plain");
            var pair = await AdmitAsync("pair", "plain");
            var protectedActor = await AdmitAsync("protected", "protected");
            var large = await AdmitAsync("large", "plain");
            var largeDelete = await AdmitAsync("large-delete", "large-delete");
            var compoundRetire = await AdmitAsync("compound-retire", "compound-retire");
            var compoundKeep = await AdmitAsync("compound-keep", "compound-keep");
            var exhausted = await AdmitAsync("exhausted", "exhausted");
            var withJob = await AdmitAsync("outstanding", "running");
            var protectionReads = 0;
            var executor = new ParkOpeningActor(protection: header =>
            {
                if (header.TypeData == "large-delete" && Interlocked.Increment(ref protectionReads) > 1)
                    throw new InvalidOperationException("The fixture temporarily cannot read deletion protection.");
                return header.TypeData is "protected" or "compound-keep";
            });
            executor.ReleaseFault.TrySetResult();

            await using (var first = Host())
            {
                await first.StartAsync(token);
                await WaitAsync(first, plain, 1);
                foreach (var root in new[] { delete, pair, protectedActor, large, largeDelete, compoundRetire, compoundKeep, exhausted, withJob })
                {
                    await WaitAsync(first, root, 1);
                    Require(await ingress.SendAsync(scope, new Contract.Target.Ref(root), "poison", "fail", cancellationToken: token) is
                        { Outcome: Contract.AdmissionOutcome.Accepted }, "the parked-operator fixture admits a fault input");
                    await WaitAsync(first, root, 2);
                }
                await first.StopAsync(token);
                Require(first.LastError is null, "the opening owner stops with its Parks committed");
            }

            var largeActor = (await store.ReadFirstRootAsync(scope.Tenant, large.Id, token))!.Actor.Id;
            var largeDeleteActor = (await store.ReadFirstRootAsync(scope.Tenant, largeDelete.Id, token))!.Actor.Id;
            await SeedKeptPageAsync(largeActor, InMemoryStore.ParkPageLimit, 8);
            await SeedKeptPageAsync(largeDeleteActor, 2, ParkRecoveryCredit.LimitBytes / 2 + 1_024);
            var compoundRetireActor = (await store.ReadFirstRootAsync(scope.Tenant, compoundRetire.Id, token))!.Actor.Id;
            var compoundKeepActor = (await store.ReadFirstRootAsync(scope.Tenant, compoundKeep.Id, token))!.Actor.Id;
            await SeedKeptPageAsync(compoundRetireActor, InMemoryStore.ParkPageLimit, 8);
            await SeedKeptPageAsync(compoundKeepActor, InMemoryStore.ParkPageLimit, 8);
            var exhaustedActor = (await store.ReadFirstRootAsync(scope.Tenant, exhausted.Id, token))!.Actor.Id;
            await SeedKeptPageAsync(exhaustedActor, InMemoryStore.ParkPageLimit, 8);
            var park = await ReadAsync(plain);
            var otherPark = await ReadAsync(delete);
            Require(await ingress.SendAsync(scope, new Contract.Target.Ref(plain), "wrong", Contract.Kinds.Terminate, Command(plain, otherPark.Selection!),
                cancellationToken: token) is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection },
                "the SQLite admission refuses a Park selection from a different root");
            var terminateReply = CallAsync(plain, "terminate-creator", Contract.Kinds.Terminate, park.Selection!);
            var deleteReply = CallAsync(delete, "delete", Contract.Kinds.Delete, otherPark.Selection!);
            var compound = CallAsync(pair, "compound", Contract.Kinds.Terminate, (await ReadAsync(pair)).Selection!, thenDelete: true);
            var protectedReply = CallAsync(protectedActor, "protected", Contract.Kinds.Delete, (await ReadAsync(protectedActor)).Selection!);
            var largeReply = CallAsync(large, "large", Contract.Kinds.Terminate, (await ReadAsync(large)).Selection!);
            var largeDeleteReply = CallAsync(largeDelete, "large-delete", Contract.Kinds.Delete, (await ReadAsync(largeDelete)).Selection!);
            var compoundRetireReply = CallAsync(compoundRetire, "compound-retire", Contract.Kinds.Terminate,
                (await ReadAsync(compoundRetire)).Selection!, thenDelete: true);
            var compoundKeepReply = CallAsync(compoundKeep, "compound-keep", Contract.Kinds.Terminate,
                (await ReadAsync(compoundKeep)).Selection!, thenDelete: true);
            var outstanding = CallAsync(withJob, "outstanding", Contract.Kinds.Terminate, (await ReadAsync(withJob)).Selection!);
            while (!await db.Messages.AnyAsync(row => row.Target == largeDeleteActor && row.Kind == Kinds.Delete && !row.Done, token))
                await Task.Delay(25, token);
            var observation = new ParkReadObservation("bounded-park-page");
            var probe = new EfStore(() => OpenFoundationDb(file, definitions, observation), postgres: false, fenceInBatch) { CommitSql = CommitSql.Owned };
            await probe.EnableRootJobsAsync(token);
            probe.BindSqliteWorkerNode();
            var probeFence = (await probe.TakeAsync(0, "park-page-plan", Defaults.Lease, token))!;
            var polled = await probe.PollAsync(0, probeFence.Epoch, TimeSpan.Zero, 20, token);
            Require(polled.Messages.Any(item => item.Message is { Id: "large-delete", Operator.RootParkFacts: { Page: 2, FitsPage: false } }) &&
                observation.Plan.Any(step => step.Contains("IX_FoundationRootInbox_Kept", StringComparison.Ordinal) &&
                    step.Contains("Target=?", StringComparison.Ordinal)),
                "SQLite quotes the byte-limited page through its actor-scoped kept-row index");
            await probe.ReleaseAsync(0, probeFence.Epoch, token);
            await using var owner = Host();
            await owner.StartAsync(token);
            Require(await terminateReply is { Channel: Request.Response, Item: "terminated" } &&
                await WaitAsync(owner, plain, 2) is { Status: InstanceStatus.Cancelled, TerminalRevision: 2, ParkAt: null },
                "held Terminate ends a creator Park in one fenced checkpoint");
            Require((await reads.GetAsync(scope, plain, token)) is { Header.Status: Contract.ActorStatus.Cancelled, Park: null, Selection: not null },
                "the read projection exposes the terminal selection after the Park closes");
            Require(await deleteReply is { Channel: Request.Response, Item: "deleted" } && await reads.GetAsync(scope, delete, token) is null &&
                await compound is { Channel: Request.Response, Item: "deleted" } && await reads.GetAsync(scope, pair, token) is null,
                "held Delete and compound Terminate and Delete each retire one selected Park");
            Require(await protectedReply is { Error: "protected" } && await WaitAsync(owner, protectedActor, 3) is
                { Status: InstanceStatus.Faulted, TerminalRevision: null, ParkAt: > 0 }, "a protected Delete preserves the same Park");
            var outstandingReply = await outstanding;
            var parkedJob = await store.ReadFirstRootAsync(scope.Tenant, withJob.Id, token);
            Require(outstandingReply is { Error: "park outstanding" } && parkedJob is
                { Status: InstanceStatus.Faulted, Jobs: not null, ParkAt: > 0, Revision: 3 },
                $"a Park with an outstanding job refuses Terminate and preserves the job (reply {outstandingReply.Error ?? outstandingReply.Item}, revision {parkedJob?.Revision})");
            Require(await largeReply is { Channel: Request.Response, Item: "started" } &&
                await WaitAsync(owner, large, 4) is { Status: InstanceStatus.Cancelled, TerminalRevision: 4, ParkAt: null },
                "held Terminate opens a larger Park and its next durable control completes it");
            Require(await largeDeleteReply is { Channel: Request.Response, Item: "started" }, "the byte-limited Park returns a clearing receipt");
            await WaitAsync(owner, largeDelete, 4);
            Require(await ReadAsync(largeDelete) is { Park.OpenResolution: "deleting", Header.EndedAt: null },
                "inspection reports the committed Delete clearing while its retry waits");
            Require(await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = {largeDeleteActor} AND "FoundationParkClearing" = 'large-delete'
                    AND "FoundationParkCleared" = 2 AND "FoundationParkAt" IS NOT NULL)
                    AND (SELECT count(*) FROM "Messages" WHERE "Target" = {largeDeleteActor} AND "FoundationKeptBy" IS NOT NULL) = 1
                    AND EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {largeDeleteActor} AND "Revision" = 4 AND "Entries" =
                        {Json.Write(new { Outcome = "refused", Operation = "large-delete", Reason = "unavailable" })}) AS "Value"
                """).SingleAsync(token), "the byte cut keeps the second large row and persists a retry when final-page protection is unavailable");
            Require(await compoundRetireReply is { Channel: Request.Response, Item: "started" } &&
                await compoundKeepReply is { Channel: Request.Response, Item: "started" },
                "both held compound operations acknowledge paged clearing with started");
            while (await reads.GetAsync(scope, compoundRetire, token) is not null) await Task.Delay(25, token);
            var kept = await WaitAsync(owner, compoundKeep, 4);
            Require(kept is { Status: InstanceStatus.Cancelled, TerminalAt: > 0, ParkAt: null } &&
                kept.TerminalRevision == kept.Revision &&
                await reads.GetAsync(scope, compoundKeep, token) is { Header.Status: Contract.ActorStatus.Cancelled, Park: null, Selection: not null } &&
                await db.Database.SqlQuery<bool>($"""
                    SELECT NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" IN ({compoundRetireActor}, {compoundKeepActor})
                            AND "FoundationKeptBy" IS NOT NULL)
                        AND (SELECT count(*) FROM "Journal" WHERE "Instance" = {compoundRetireActor}) = 1
                        AND EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {compoundRetireActor} AND "Revision" = -1 AND "Entries" = '["delete"]')
                        AND EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {compoundKeepActor} AND "Revision" = 3
                            AND "Entries" = {Json.Write(new { Outcome = "clearing", Operation = "compound-keep", Cleared = InMemoryStore.ParkPageLimit })})
                        AND EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {compoundKeepActor} AND "Revision" = {kept.Revision}
                            AND "Entries" = {Json.Write(new { Outcome = "protected", Disposition = "terminated", Operation = "compound-keep", Cleared = InMemoryStore.ParkPageLimit + 1 })})
                        AS "Value"
                    """).SingleAsync(token), "the last page retires one compound root and leaves the protected root Cancelled with its two outcomes");
            var originalEpoch = (await FenceAsync(file, token)).Epoch;
            await owner.StopAsync(token);
            Require(owner.LastError is null, "the first page owner stops with the clearing and its continuation committed");

            var exhaustedProtectionReads = 0;
            var protectionRestored = false;
            var replacement = new ParkOpeningActor(protection: header =>
            {
                if (header.TypeData == "exhausted" && !Volatile.Read(ref protectionRestored) &&
                    Interlocked.Increment(ref exhaustedProtectionReads) > 1)
                    throw new InvalidOperationException("The protection source remains unavailable.");
                return false;
            });
            await using var successor = SqliteEmbeddedHostedService.ForFoundation(file, "park-control-successor", replacement,
                reason => throw new InvalidOperationException(reason), Loops, fenceInBatch);
            await successor.StartAsync(token);
            while (await reads.GetAsync(scope, largeDelete, token) is not null) await Task.Delay(25, token);
            Require((await FenceAsync(file, token)).Epoch > originalEpoch,
                "replacement ownership completes the same byte-limited Delete after protection returns");
            Require(await db.Database.SqlQuery<bool>($"""
                SELECT NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" IN ({largeActor}, {largeDeleteActor}) AND "FoundationKeptBy" IS NOT NULL)
                    AND EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {largeActor} AND "Revision" = 4 AND "Entries" =
                        {Json.Write(new { Outcome = "terminated", Operation = "large", Cleared = InMemoryStore.ParkPageLimit + 1 })})
                    AND (SELECT count(*) FROM "Journal" WHERE "Instance" = {largeDeleteActor}) = 1
                    AND EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {largeDeleteActor} AND "Revision" = -1 AND "Entries" = '["delete"]')
                    AS "Value"
                """).SingleAsync(token), "both larger Parks clear every kept row and persist their exact end or deletion journal");
            var remaining = (await ReadAsync(exhausted)).Selection!;
            Require(await ingress.SendAsync(scope, new Contract.Target.Ref(exhausted), "exhausted-delete", Contract.Kinds.Delete,
                Command(exhausted, remaining), cancellationToken: token) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "a separate paged Delete opens while its protection can still be read");
            var firstRefusalRevision = 4;
            for (var refusal = 0; refusal < FoundationRootParkFacts.MaxProtectionRefusals - 1; refusal++)
            {
                await WaitAsync(successor, exhausted, firstRefusalRevision + refusal);
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "Messages" SET "Due" = 0
                    WHERE "Target" = {exhaustedActor} AND "Kind" = {Kinds.Delete} AND NOT "Done"
                    """, token);
            }
            var stopped = await WaitAsync(successor, exhausted, firstRefusalRevision + FoundationRootParkFacts.MaxProtectionRefusals - 1);
            Require(stopped is { Status: InstanceStatus.Faulted, TerminalRevision: null, ParkAt: > 0 } &&
                await ReadAsync(exhausted) is { Park.OpenResolution: null } && await db.Database.SqlQuery<bool>($"""
                    SELECT (SELECT count(*) FROM "Messages" WHERE "Target" = {exhaustedActor} AND "FoundationKeptBy" IS NOT NULL) = 1
                        AND NOT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {exhaustedActor} AND "Kind" = {Kinds.Delete} AND NOT "Done")
                        AND EXISTS (SELECT 1 FROM "Journal" WHERE "Instance" = {exhaustedActor} AND "Revision" = {stopped.Revision}
                            AND "Entries" = {FoundationRootParkFacts.ProtectionStopped("exhausted-delete", InMemoryStore.ParkPageLimit)}) AS "Value"
                    """).SingleAsync(token), "the retry ceiling stops the clearing, keeps its last page and publishes the exact stopped outcome");
            Volatile.Write(ref protectionRestored, true);
            Require(await ingress.SendAsync(scope, new Contract.Target.Ref(exhausted), "delete-after-ceiling", Contract.Kinds.Delete,
                Command(exhausted, remaining), cancellationToken: token) is { Outcome: Contract.AdmissionOutcome.Accepted },
                "a new operation can address the same Park after protection becomes readable");
            while (await reads.GetAsync(scope, exhausted, token) is not null) await Task.Delay(25, token);

            await successor.StopAsync(token);
            Require(successor.LastError is null, "the replacement parked-operator owner stops cleanly");
            return 0;

            SqliteEmbeddedHostedService Host() => SqliteEmbeddedHostedService.ForFoundation(file, "park-control-node", executor,
                reason => throw new InvalidOperationException(reason), Loops, fenceInBatch, requests: requests);

            async Task<Contract.ActorRef> AdmitAsync(string id, string definition) => (await ingress.SendAsync(scope,
                new Contract.Target.Start(new(ParkKind, definition)), id, Contract.Kinds.Start, cancellationToken: token)).Actor!.Value;

            async Task<Contract.ActorView> ReadAsync(Contract.ActorRef root) => (await reads.GetAsync(scope, root, token))!;

            static string Command(Contract.ActorRef root, string selection, bool thenDelete = false) =>
                Json.Write(new Contract.OperatorCommand(selection, ThenDelete: thenDelete, ExpectedTarget: root));

            async Task<Contract.Reply> CallAsync(Contract.ActorRef root, string id, string kind, string selection, bool thenDelete = false) =>
                await ingress.RequestAsync(scope, new Contract.Target.Ref(root), id, kind, Command(root, selection, thenDelete),
                    null, new(null, await requests.ReadClockAsync(token) + 20_000), token);

            async Task<EfStore.RootImage> WaitAsync(SqliteEmbeddedHostedService service, Contract.ActorRef root, long revision)
            {
                while (true)
                {
                    if (service.LastError is { } problem) throw new InvalidOperationException("The parked operator owner failed.", problem);
                    var image = await store.ReadFirstRootAsync(scope.Tenant, root.Id, token);
                    if (image is not null && image.Revision >= revision) return image;
                    await Task.Delay(25, token);
                }
            }

            async Task SeedKeptPageAsync(string actor, int count, int bytes)
            {
                Require(await db.Database.ExecuteSqlInterpolatedAsync($"""
                    WITH RECURSIVE ids(n) AS (VALUES (1) UNION ALL SELECT n + 1 FROM ids WHERE n < {count}),
                        base(seq) AS MATERIALIZED (SELECT coalesce(max("Seq"), 0) FROM "Messages")
                    INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Seq", "Kind", "Due", "Payload", "KeepUntil",
                        "Done", "Seen", "Attempts", "Trial", "JobGeneration", "FoundationKeptBy")
                    SELECT {actor} || '/retained-test-' || n, {InboxKey.FoundationOccurrenceScope}, {actor}, 0, base.seq + n, 'fail', 0, printf('%.*c', {bytes}, 'x'), 0,
                        1, 0, 0, 0, 0, {(short)ParkKeepReason.Consumed} FROM ids CROSS JOIN base
                    """, token) == count, "the fixture adds retained inputs for a row- or byte-limited Park");
            }
        }
        finally { directory.Delete(recursive: true); }
    }
}
