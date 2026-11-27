using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    private sealed record CapturedPublicStart(string Wait, string Registration, string Start, string? Payload, string Reference,
        string PackedEventId)
    {
        internal string EventId => DurableKeyJson.Read<string>(PackedEventId);
    }

    internal static async Task<int> RunPublicStartsAsync(string provider, bool inBatch)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = stop.Token;
        var scope = ContractScopes.Bind("public-events");
        var other = ContractScopes.Bind("public-other");
        var model = new PublicEventActor();
        const string key = PublicEventActor.Key;
        var directory = Directory.CreateTempSubdirectory("public-event-starts");
        try
        {
            var path = Path.Combine(directory.FullName, "store.db");
            var (open, ingress, reads, owner, _, restart) = await OpenAsync(model, provider, path, inBatch, token, publicStarts: true);
            await using var owned = owner;
            var initial = new Contract.StartSpec(model.Type, "starter", "first\0template");
            var replacement = initial with { Input = "replacement" };
            await ingress.SetStartAsync(scope, key, "starter", initial, token);
            await ingress.SetStartAsync(scope, key, "starter", initial, token);
            await ingress.SetStartAsync(scope, "event:orders", "starter", replacement, token);
            await ingress.SetStartAsync(other, key, "starter", replacement, token);
            var firstId = await StartPublisherAsync("one");
            var first = (await CapturedAsync()).Single(item => item.EventId == firstId);
            Check(first.Wait == "starter" && DurableKeyJson.Read<Contract.StartSpec>(first.Start) == initial &&
                DurableKeyJson.Read<string>(first.Payload!) == "authored payload",
                "both engines select exactly one ordinal, tenant-bound registration");
            await VerifyDeliveryAsync(new(first.Reference), initial.Input!, "starter", "authored payload");

            await ingress.SetStartAsync(scope, key, "starter", replacement, token);
            var secondId = await StartPublisherAsync("two");
            var second = (await CapturedAsync()).Single(item => item.EventId == secondId);
            Check(DurableKeyJson.Read<Contract.StartSpec>(second.Start) == replacement && (await CapturedAsync()).Contains(first),
                "replacement affects later publications and preserves the original captured template");
            await VerifyDeliveryAsync(new(second.Reference), replacement.Input!, "starter", "authored payload");
            CapturedPublicStart? retainedAuthored = null;
            await UntilAsync(async () => (retainedAuthored = (await CapturedAsync()).SingleOrDefault(item =>
                item.EventId == firstId && item.Registration == second.Registration)) is not null);
            await VerifyDeliveryAsync(new(retainedAuthored!.Reference), replacement.Input!, "starter", "authored payload", firstId);

            await ingress.SetStartAsync(scope, key, "starter", null, token);
            await ingress.SetStartAsync(scope, key, "starter", null, token);
            var thirdId = await StartPublisherAsync("three");
            Check((await CapturedAsync()).All(item => item.EventId != thirdId), "idempotent unpublish excludes the removed registration");

            var live = await StartAsync("live-waiter", "one live waiter");
            await UntilAsync(async () =>
            {
                await using var db = open();
                return await db.Subscriptions.AnyAsync(row => row.Key == scope.Tenant + "_" + key && row.Wait == "live", token);
            });
            await ingress.SetStartAsync(scope, key, "outside", replacement, token);
            await ingress.SetStartAsync(scope, key, "outside-also", initial, token);
            var target = new Contract.Target.Event(key);
            var options = new Contract.AddOptions(KeepFor: TimeSpan.FromMinutes(10));
            var invalid = await ingress.SendAsync(scope, target, "~reserved", "ignored", "refused", options, token);
            var outside = await ingress.SendAsync(scope, target, "event\0id", Contract.Kinds.Recover, "outside payload", options, token);
            var replay = await ingress.SendAsync(scope, target, "event\0id", "different kind", "retry content", options, token);
            Check(invalid is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.InvalidEventId } &&
                outside is { Outcome: Contract.AdmissionOutcome.Accepted, Publication.Status: Contract.PublicationStatus.Matched } &&
                outside.Publication is { CorrelationKey: key, EventId: "event\0id" } &&
                replay.Outcome == Contract.AdmissionOutcome.Duplicate && replay.Publication == outside.Publication,
                "outside send ignores kind, refuses a reserved id and returns the original incarnation and receipt on replay");
            var third = (await CapturedAsync()).Single(item => item.Wait == "outside" && item.EventId == "event\0id");
            var sibling = (await CapturedAsync()).Single(item => item.Wait == "outside-also" && item.EventId == "event\0id");
            Check(sibling.Reference != third.Reference, "one publication gives each registered start a distinct exact root");
            await VerifyDeliveryAsync(live, "one live waiter", "live", "outside payload", "event\0id");
            await VerifyDeliveryAsync(new(third.Reference), replacement.Input!, "outside", "outside payload", "event\0id");
            await VerifyDeliveryAsync(new(sibling.Reference), initial.Input!, "outside-also", "outside payload", "event\0id");
            var tooWide = await ingress.SendAsync(scope, target, "too-wide", "ignored", new string('x', 9 * 1024 * 1024), options, token);
            Check(tooWide is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.InboxFull, Publication: null } &&
                (await CapturedAsync()).All(item => item.EventId != "too-wide"), "a shared fanout overflow creates no extra target or accepted receipt");

            await using (var db = open())
            {
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationPublicEventPublications" SET "ReceiptUntil" = 100
                    WHERE "Tenant" = {scope.Tenant} AND "Key" = {scope.Tenant + "_" + key}
                        AND "Operation" = {FoundationRootCreator.OperationDigest("event\0id")}
                    """, token);
            }
            var recreated = await ingress.SendAsync(scope, target, "event\0id", "ignored", "second incarnation", options, token);
            var fourth = (await CapturedAsync()).Single(item => item.Wait == "outside" && item.EventId == "event\0id" && item.Reference != third.Reference);
            Check(recreated is { Outcome: Contract.AdmissionOutcome.Accepted, Publication: not null } &&
                recreated.Publication.Incarnation != outside.Publication!.Incarnation && fourth.Registration == third.Registration,
                "reuse after receipt expiry preserves the registration and creates a different event and actor life");
            await VerifyDeliveryAsync(new(fourth.Reference), replacement.Input!, "outside", "second incarnation", "event\0id");
            const string heldKey = "held-public-start";
            var heldTarget = new Contract.Target.Event(heldKey);
            await ingress.SetStartAsync(scope, heldKey, "held", new(model.Type, "held-recipient", "held template"), token);
            var held = await RequestAsync(heldTarget, "held-id", "held item", new(KeepFor: TimeSpan.Zero));
            Check(held is { Channel: "held-event", Error: null } && held.Item?.ToString() == "held item",
                "a held Event creates the registered actor and returns its first matching reply");
            // The short-lived target can be pruned as soon as the held publication has been dispatched.
            var heldActors = await reads.ListAsync(scope, new Contract.ActorQuery(Type: model.Type, Status: Contract.ActorStatus.Finished),
                cancellationToken: token);
            var heldActor = heldActors.Items.Single(item => item.Header.TypeData ==
                JsonSerializer.Serialize(new[] { "held template", heldKey, "held", "held item", "held-id" }));
            await VerifyDeliveryAsync(heldActor.Header.Ref, "held template", "held", "held item", "held-id", heldKey);
            var heldReplay = await ingress.SendAsync(scope, heldTarget, "held-id", "ignored", "different item", cancellationToken: token);
            Check(heldReplay is { Outcome: Contract.AdmissionOutcome.Duplicate, Publication.EventId: "held-id" },
                "one-way retry observes the held Event publication receipt without creating another actor");
            const string fanoutKey = "held-fanout";
            await ingress.SetStartAsync(scope, fanoutKey, "a-quiet", new(model.Type, "fanout-quiet", "quiet template"), token);
            await ingress.SetStartAsync(scope, fanoutKey, "b-emits", new(model.Type, "fanout-emits", "reply template"), token);
            var fanout = RequestAsync(new(fanoutKey), "fanout-id", "fanout item");
            try
            {
                await UntilAsync(async () =>
                {
                    var finished = await reads.ListAsync(scope, new Contract.ActorQuery(Type: model.Type, Status: Contract.ActorStatus.Finished),
                        cancellationToken: token);
                    return finished.Items.Any(item => item.Header.TypeData ==
                        JsonSerializer.Serialize(new[] { "quiet template", fanoutKey, "a-quiet", "fanout item", "fanout-id" }));
                });
                Check(!fanout.IsCompleted, "a quiet actor's terminal result leaves the shared held request open for other exact matches");
            }
            finally { model.ReleaseFanout.TrySetResult(); }
            var winner = await fanout;
            Check(winner is { Channel: "held-fanout", Error: null } && winner.Item?.ToString() == "fanout item",
                "the first emitted output wins after another held Event recipient finishes quietly");

            const string quietFanoutKey = "held-all-quiet";
            await ingress.SetStartAsync(scope, quietFanoutKey, "one", new(model.Type, "fanout-quiet", "one"), token);
            await ingress.SetStartAsync(scope, quietFanoutKey, "two", new(model.Type, "fanout-quiet", "two"), token);
            var quietFanout = await RequestAsync(new(quietFanoutKey), "all-quiet", "no output");
            Check(quietFanout is { Channel: null, Status: Contract.ActorStatus.Finished, Error: null },
                "a held Event with only quiet matches returns their terminal result");

            var unmatched = new Contract.Target.Event("unmatched-held-start");
            var missing = await RequestAsync(unmatched, "unmatched", "discarded");
            var afterMissing = await ingress.SendAsync(scope, unmatched, "unmatched", "ignored", "accepted later", cancellationToken: token);
            Check(missing.Error == "no waiter" && afterMissing is { Outcome: Contract.AdmissionOutcome.Accepted },
                "an unmatched held Event leaves no publication receipt");

            const string selfCatchUp = PublicEventActor.KeepingStartTopic;
            await ingress.SetStartAsync(scope, selfCatchUp, "kept", new(model.Type, "start-keeps", selfCatchUp), token);
            var startsKeeper = await ingress.SendAsync(scope, new Contract.Target.Event(selfCatchUp), "already-delivered",
                "ignored", "same occurrence", options, token);
            Check(startsKeeper.Outcome == Contract.AdmissionOutcome.Accepted, "a retained publication creates a registered keeping actor");
            var keepingStart = (await CapturedAsync()).Single(item => item.EventId == "already-delivered");
            await UntilAsync(async () =>
            {
                await using var db = open();
                return await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (
                        SELECT 1 FROM "FoundationPublicEventLiveWaits" w
                        JOIN "FoundationRootBirths" b ON b."Actor" = w."Instance"
                        WHERE b."Reference" = {keepingStart.Reference}
                            AND w."CatchAfter" = w."CatchThrough" AND w."CatchThrough" > 0
                            AND (SELECT count(*) FROM "Messages" m WHERE m."Target" = b."Actor" AND m."Kind" = 'event') = 1
                            AND EXISTS (SELECT 1 FROM "Messages" m WHERE m."Target" = b."Actor" AND m."Kind" = 'event' AND m."Done")
                    ) AS "Value"
                    """).SingleAsync(token);
            });
            Check((await reads.GetAsync(scope, new(keepingStart.Reference), token))?.Header.Status == Contract.ActorStatus.Running,
                "a keeping start's catch-up cursor recognizes the original exact matching input without re-enqueuing it");

            var keeper = await StartAsync("keeper", "cold subscription actor");
            await UntilAsync(async () => (await reads.GetAsync(scope, keeper, token))?.Header.Status == Contract.ActorStatus.Running);
            await StopOwnerAsync(owner);

            const string quietKey = "quiet";
            var quiet = new Contract.Target.Event(quietKey);
            var quietOptions = new Contract.AddOptions(KeepFor: TimeSpan.FromSeconds(3));
            var unrepresentable = await ingress.SendAsync(scope, quiet, "too-large-after-encoding", "ignored",
                new string('\0', 3 * 1024 * 1024), quietOptions, token);
            Check(unrepresentable is { Outcome: Contract.AdmissionOutcome.Refused, Publication: null },
                "acceptance without a waiter still checks the encoded delivery limit");
            var kept = await ingress.SendAsync(scope, quiet, "retained", "ignored", "kept payload", quietOptions, token);
            Check(kept is { Outcome: Contract.AdmissionOutcome.Accepted, Publication: not null },
                "an event with no waiter commits a receipt and retained payload while the owner is stopped");
            await ingress.SetStartAsync(scope, quietKey, "late", replacement, token);
            await ingress.SetStartAsync(other, quietKey, "foreign", replacement, token);
            await using (var db = open())
            {
                var owed = await db.Database.SqlQuery<bool>($"""
                    SELECT p."ReceiptUntil" > p."KeepUntil" AND p."Payload" = {DurableKeyJson.Write("kept payload")}
                        AND r."CatchAfter" < p."Ordinal" AND r."CatchThrough" = p."Ordinal" AS "Value"
                    FROM "FoundationPublicEventPublications" p JOIN "FoundationPublicEventStarts" r
                        ON r."Tenant" = p."Tenant" AND r."Key" = p."Key"
                    WHERE p."Tenant" = {scope.Tenant} AND p."Key" = {scope.Tenant + "_" + quietKey} AND r."Wait" = 'late'
                    """).SingleAsync(token);
                Check(owed, "registration durably covers the retained event while replay has an independent deadline");
            }

            const string keeperKey = "retained-for-actor";
            var keeperEvent = new Contract.Target.Event(keeperKey);
            var beforeSubscribe = await ingress.SendAsync(scope, keeperEvent, "before-subscribe", "ignored", "cold kept item",
                new(KeepFor: TimeSpan.FromMinutes(1)), token);
            var arm = await ingress.SendAsync(scope, new Contract.Target.Ref(keeper), "arm-keep", "arm-keep", keeperKey,
                cancellationToken: token);
            Check(beforeSubscribe.Outcome == Contract.AdmissionOutcome.Accepted && arm.Outcome == Contract.AdmissionOutcome.Accepted,
                "the old owner stops before a retained publication and the subscription command are admitted");
            await Task.Delay(quietOptions.KeepFor!.Value + TimeSpan.FromMilliseconds(100), token);
            await using var successor = await restart(token);
            await VerifyDeliveryAsync(keeper, "cold subscription actor", "kept", "cold kept item", "before-subscribe", keeperKey);
            await using (var db = open())
            {
                var consumed = await db.Database.SqlQuery<bool>($"""
                    SELECT count(r."Delivery") = 1 AS "Value"
                    FROM "FoundationPublicEventPublications" p
                    JOIN "FoundationPublicEventLiveReceipts" r ON r."Occurrence" = p."Occurrence"
                    WHERE p."Tenant" = {scope.Tenant} AND p."Key" = {scope.Tenant + "_" + keeperKey}
                    """).SingleAsync(token);
                Check(consumed, "the cold owner matched one retained occurrence to the keeping actor's exact wait");
            }
            CapturedPublicStart? late = null;
            // The first live checkpoint reads the reference before bounded cleanup removes the settled capture record.
            // Query the permanent birth by its receipt identity via the generated actor's public list if cleanup won first.
            Contract.ActorView? lateActor = null;
            await UntilAsync(async () =>
            {
                late ??= (await CapturedAsync()).SingleOrDefault(item => item.Wait == "late" && item.EventId == "retained");
                var views = await reads.ListAsync(scope, new Contract.ActorQuery(Type: model.Type, Status: Contract.ActorStatus.Finished),
                    cancellationToken: token);
                lateActor = views.Items.SingleOrDefault(item => item.Header.TypeData.Contains("\"retained\"", StringComparison.Ordinal));
                return late is not null || lateActor is not null;
            });
            await VerifyDeliveryAsync(late is null ? lateActor!.Header.Ref : new(late.Reference), replacement.Input!, "late", "kept payload",
                "retained", quietKey);
            await UntilAsync(async () =>
            {
                await using var db = open();
                return await db.Database.SqlQuery<bool>($"""
                    SELECT "Payload" IS NULL AND "CleanupAt" = "ReceiptUntil" AS "Value" FROM "FoundationPublicEventPublications"
                    WHERE "Tenant" = {scope.Tenant} AND "Key" = {scope.Tenant + "_" + quietKey}
                    """).SingleAsync(token);
            });
            var quietReplay = await ingress.SendAsync(scope, quiet, "retained", "ignored", "ignored", quietOptions, token);
            Check(quietReplay.Outcome == Contract.AdmissionOutcome.Duplicate && quietReplay.Publication == kept.Publication,
                "cleanup drops the expired payload after cold catch-up and preserves the exact replay receipt");
            await using (var db = open())
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationPublicEventPublications" SET "ReceiptUntil" = 0, "CleanupAt" = 0
                    WHERE "Tenant" = {scope.Tenant} AND "Key" = {scope.Tenant + "_" + quietKey}
                    """, token);
            await UntilAsync(async () =>
            {
                await using var db = open();
                return !await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "FoundationPublicEventPublications"
                        WHERE "Tenant" = {scope.Tenant} AND "Key" = {scope.Tenant + "_" + quietKey}) AS "Value"
                    """).SingleAsync(token);
            });
            var foreign = await reads.ListAsync(other, new Contract.ActorQuery(Type: model.Type), cancellationToken: token);
            Check(foreign.Items.Count == 0 && (await CapturedAsync()).All(item => item.Wait != "foreign" && item.Wait != "late"),
                "bounded cleanup removes expired receipt and target data without a foreign delivery");
            await ingress.SetStartAsync(scope, quietKey, "late", null, token);
            await using (var db = open())
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationPublicEventHorizons" SET "Through" = "Through" + 60000 WHERE "Tenant" = {scope.Tenant}
                    """, token);
            var backdated = await ingress.SendAsync(scope, quiet, "below-horizon", "ignored", "new publication", quietOptions, token);
            await ingress.SetStartAsync(scope, quietKey, "after-clock-backstep", replacement, token);
            await using (var db = open())
            {
                var barred = await db.Database.SqlQuery<bool>($"""
                    SELECT r."CatchThrough" = 0 AND p."KeepUntil" < r."RetainFrom" AS "Value"
                    FROM "FoundationPublicEventStarts" r JOIN "FoundationPublicEventPublications" p
                        ON p."Tenant" = r."Tenant" AND p."Key" = r."Key"
                    WHERE r."Tenant" = {scope.Tenant} AND r."Key" = {scope.Tenant + "_" + quietKey} AND r."Wait" = 'after-clock-backstep'
                    """).SingleAsync(token);
                Check(barred && backdated.Outcome == Contract.AdmissionOutcome.Accepted,
                    "retention cleanup's tenant watermark survives a clock backstep without changing publication acceptance");
            }
            await StopOwnerAsync(successor);
            await CheckPublicationClockAsync();
            await CheckReinstallationAsync();
            return 0;

            async Task StopOwnerAsync(IAsyncDisposable host)
            {
                if (host is RuntimeNode postgresOwner) await postgresOwner.StopAsync(token);
                else await ((SqliteEmbeddedHostedService)host).StopAsync(token);
            }

            async Task CheckPublicationClockAsync()
            {
                var clockScope = ContractScopes.Bind("public-event-clock");
                var zeroScope = ContractScopes.Bind("public-event-zero");
                const string outsideZero = "outside-zero";
                const string authoredZero = "authored-zero";
                var zeroOutsideKey = zeroScope.Tenant + "_" + outsideZero;
                var zeroAuthoredKey = zeroScope.Tenant + "_" + authoredZero;
                var qualifiedHeldKey = scope.Tenant + "_" + heldKey;
                var retention = new Contract.AddOptions(KeepFor: TimeSpan.FromMinutes(1));
                var anchor = await ReadClockAsync();
                var sweptUntil = anchor + 100;
                var retainedUntil = anchor + 200;
                await ingress.SendAsync(clockScope, new Contract.Target.Event("swept"), "swept", "ignored", "first", retention, token);
                await ingress.SendAsync(clockScope, new Contract.Target.Event("retained"), "retained", "ignored", "second", retention, token);
                await ingress.SetStartAsync(zeroScope, outsideZero, "initial", replacement, token);
                var zero = await ingress.SendAsync(zeroScope, new Contract.Target.Event(outsideZero), outsideZero,
                    Contract.Kinds.Suspend, "outside zero payload", cancellationToken: token);
                Check(zero.Outcome == Contract.AdmissionOutcome.Accepted,
                    "outside Event ignores operator message kinds");

                await using var db = open();
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                Func<FormattableString, FormattableString>? bind = null;
                if (provider == "postgres")
                {
                    var marker = await db.Database.SqlQueryRaw<long>("""
                        SELECT pg_catalog.to_regclass('"KernelStoreVersions"')::oid::bigint AS "Value"
                        """).SingleAsync(token);
                    bind = await PostgresRootAdmission.PinAuthoredEventSourceAsync(db, checked((uint)marker), token);
                    await RootEventAdmission.LockPostgresKeysAsync(db,
                        [Qualify("swept"), Qualify("retained"), zeroOutsideKey, zeroAuthoredKey, qualifiedHeldKey, scope.Tenant + "_" + key], token);
                }

                var previous = await RootPublicEventPublications.ReadReplayAsync(db, scope.Tenant, scope.Tenant + "_" + key, "event\0id",
                    FoundationRootCreator.OperationDigest("event\0id"), 50, sql => RootPublicEventStarts.Bind(sql, bind), token);
                Check(previous == recreated.Publication!.Incarnation, "a clock backstep after id reuse returns the newest receipt");
                var olderPayload = await db.Database.SqlQuery<bool>($"""
                    SELECT "ReceiptUntil" IS NULL AND "KeepUntil" IS NOT NULL AND "Payload" = {DurableKeyJson.Write("outside payload")} AS "Value"
                    FROM "FoundationPublicEventPublications" WHERE "Incarnation" = {outside.Publication!.Incarnation.ToString("N")}
                    """).SingleAsync(token);
                Check(olderPayload, "superseding a receipt leaves its independent retained payload intact");
                var heldOnly = await db.Database.SqlQuery<bool>($"""
                    SELECT "KeepUntil" IS NULL AND "Payload" IS NULL AND "ReceiptUntil" > {anchor} AS "Value"
                    FROM "FoundationPublicEventPublications" WHERE "Tenant" = {scope.Tenant} AND "Key" = {qualifiedHeldKey}
                    """).SingleAsync(token);
                Check(heldOnly, "held KeepFor zero preserves the ordinary replay window and never retains Event material");

                var zeroAt = await db.Database.SqlQuery<long?>($"""
                    SELECT "KeepUntil" AS "Value" FROM "FoundationPublicEventPublications"
                    WHERE "Tenant" = {zeroScope.Tenant} AND "Key" = {zeroOutsideKey}
                    """).SingleAsync(token) ?? throw new InvalidOperationException("An ordinary zero-window Event has a keep deadline.");
                await RootPublicEventStarts.SetAsync(db, zeroScope.Tenant, zeroAuthoredKey, "initial", replacement, zeroAt, bind, token);
                await RootEventAdmission.PublishAuthoredAsync(db,
                    [new EventRow(zeroAuthoredKey, "~authored-zero", "authored zero payload", 0) { FoundationTenant = zeroScope.Tenant }],
                    zeroAt, bind, token);
                var recorded = await db.Database.SqlQuery<bool>($"""
                    SELECT count(*) = 2 AS "Value"
                    FROM "FoundationPublicEventPublications" p JOIN "FoundationPublicEventLiveReceipts" r ON r."Occurrence" = p."Occurrence"
                    WHERE p."Tenant" = {zeroScope.Tenant} AND p."KeepUntil" = {zeroAt}
                        AND ((p."Key" = {zeroOutsideKey} AND p."Payload" = {DurableKeyJson.Write("outside zero payload")})
                            OR (p."Key" = {zeroAuthoredKey} AND p."Payload" = {DurableKeyJson.Write("authored zero payload")}))
                    """).SingleAsync(token);
                Check(recorded, "outside and authored zero-window Events retain their exact deadlines, payloads and immediate match receipts");

                await VerifyZeroCatchUpAsync(zeroOutsideKey, zeroAt - 1, "outside zero payload");
                await VerifyZeroCatchUpAsync(zeroAuthoredKey, zeroAt, "authored zero payload");
                // Exercise the held row at the same low horizon without the earlier fixture's swept events.
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationPublicEventHorizons" SET "Through" = 0 WHERE "Tenant" = {scope.Tenant}
                    """, token);
                await RootPublicEventStarts.SetAsync(db, scope.Tenant, qualifiedHeldKey, "after-held-zero", replacement, zeroAt - 1000, bind, token);
                var heldExcluded = await db.Database.SqlQuery<bool>($"""
                    SELECT "CatchThrough" = 0 AS "Value" FROM "FoundationPublicEventStarts"
                    WHERE "Tenant" = {scope.Tenant} AND "Key" = {qualifiedHeldKey} AND "Wait" = 'after-held-zero'
                    """).SingleAsync(token);
                Check(heldExcluded, "a held zero-window Event remains receipt-only even on a clock backstep");

                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationPublicEventPublications"
                    SET "KeepUntil" = CASE WHEN "Key" = {Qualify("swept")} THEN {sweptUntil} ELSE {retainedUntil} END,
                        "CleanupAt" = CASE WHEN "Key" = {Qualify("swept")} THEN {sweptUntil} ELSE {retainedUntil} END
                    WHERE "Tenant" = {clockScope.Tenant} AND "Key" IN ({Qualify("swept")}, {Qualify("retained")})
                    """, token);
                var swept = await db.Database.SqlQuery<long>($"""
                    SELECT "Ordinal" AS "Value" FROM "FoundationPublicEventPublications" WHERE "Tenant" = {clockScope.Tenant}
                        AND "Key" = {Qualify("swept")}
                    """).SingleAsync(token);
                Check(await RootPublicEventPublications.CleanupAsync(db, new(true, clockScope.Tenant, Qualify("swept"), swept),
                    anchor + 500, bind, token), "the bounded sweep closes only the selected retained publication");
                foreach (var candidate in new[] { "swept", "retained" })
                    await RootPublicEventStarts.SetAsync(db, clockScope.Tenant, Qualify(candidate), "after-backstep", replacement, retainedUntil, bind, token);

                var keepsExactSelection = await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "FoundationPublicEventHorizons" WHERE "Tenant" = {clockScope.Tenant}
                            AND "Through" = {sweptUntil})
                        AND (SELECT count(*) FROM "FoundationPublicEventStarts" WHERE "Tenant" = {clockScope.Tenant}
                            AND "Wait" = 'after-backstep' AND "RetainFrom" = {retainedUntil}) = 2
                        AND EXISTS (SELECT 1 FROM "FoundationPublicEventStarts" WHERE "Tenant" = {clockScope.Tenant}
                            AND "Key" = {Qualify("retained")} AND "CatchThrough" > 0)
                        AND NOT EXISTS (SELECT 1 FROM "FoundationPublicEventStarts" WHERE "Tenant" = {clockScope.Tenant}
                            AND "Key" = {Qualify("swept")} AND "CatchThrough" <> 0) AS "Value"
                    """).SingleAsync(token);
                Check(keepsExactSelection, "sweep advances only to the actual retention deadline, including equal eligible material after a backstep");
                await transaction.RollbackAsync(token);

                string Qualify(string candidate) => clockScope.Tenant + "_" + candidate;

                async Task VerifyZeroCatchUpAsync(string qualified, long registeringAt, string payload)
                {
                    const string wait = "after-zero";
                    await RootPublicEventStarts.SetAsync(db, zeroScope.Tenant, qualified, wait, replacement, registeringAt, bind, token);
                    var registration = await db.Database.SqlQuery<string>($"""
                        SELECT "Incarnation" AS "Value" FROM "FoundationPublicEventStarts"
                        WHERE "Tenant" = {zeroScope.Tenant} AND "Key" = {qualified} AND "Wait" = {wait}
                        """).SingleAsync(token);
                    Check(await RootPublicEventStarts.CatchUpAsync(db, new(true, zeroScope.Tenant, qualified, wait, registration), bind, token),
                        "an eligible zero-window Event has durable catch-up work");
                    var matched = await db.Database.SqlQuery<bool>($"""
                        SELECT count(*) = 1 AS "Value" FROM "FoundationPublicEventTargets"
                        WHERE "Tenant" = {zeroScope.Tenant} AND "Key" = {qualified} AND "Registration" = {registration}
                            AND "Payload" = {DurableKeyJson.Write(payload)} AND "Reference" IS NOT NULL
                        """).SingleAsync(token);
                    Check(matched, "a zero-window Event delivers its original payload once at equality or after a clock backstep");
                }
            }

            async Task CheckReinstallationAsync()
            {
                await ReinstallAsync();
                var afterReinstall = await ingress.SendAsync(scope, heldTarget, "held-id", "ignored", "ignored", cancellationToken: token);
                Check(afterReinstall.Outcome == Contract.AdmissionOutcome.Duplicate && afterReinstall.Publication == heldReplay.Publication,
                    "reinstalling a complete public Event store preserves the exact publication receipt");

                await using var db = open();
                await db.Database.ExecuteSqlRawAsync("""DROP TABLE "FoundationPublicEventLiveReceipts" """, token);
                var rejected = false;
                try { await ReinstallAsync(); }
                catch (StoreFormatException error) when (error.Message.Contains(provider == "postgres"
                    ? "missing an authoritative relation" : "installed SQLite store definitions differ", StringComparison.Ordinal))
                {
                    rejected = true;
                }
                var stillMissing = provider == "postgres"
                    ? await db.Database.SqlQueryRaw<bool>("""
                        SELECT pg_catalog.to_regclass('"FoundationPublicEventLiveReceipts"') IS NULL AS "Value"
                        """).SingleAsync(token)
                    : await db.Database.SqlQueryRaw<bool>("""
                        SELECT NOT EXISTS (SELECT 1 FROM main.sqlite_schema WHERE name = 'FoundationPublicEventLiveReceipts') AS "Value"
                        """).SingleAsync(token);
                Check(rejected && stillMissing, "reinstallation refuses to recreate missing live receipts in an initialized public Event store");
            }

            Task ReinstallAsync() => provider == "postgres"
                ? new EfStore(open, postgres: true).InstallPreviewRootSchemaAsync(token)
                : SqliteEmbeddedFoundation.ForPublicEventStartsQualification(path, "authored-event-owner", model.Type).InstallShippedAsync(token);

            async Task<long> ReadClockAsync()
            {
                await using var db = open();
                return provider == "postgres" ? await RootEventAdmission.ReadPostgresClockAsync(db, token)
                    : await SqliteRootAdmission.ReadClockAsync(db, token);
            }

            async Task<Contract.Reply> RequestAsync(Contract.Target.Event requested, string id, string item, Contract.AddOptions? options = null)
            {
                var now = await ReadClockAsync();
                return await ingress.RequestAsync(scope, requested, id, Contract.Kinds.Event, item, options, new(null, now + 6000), token);
            }

            async Task<string> StartPublisherAsync(string operation)
            {
                var actor = await StartAsync("publisher", operation);
                Contract.ActorView? view = null;
                await UntilAsync(async () => (view = await reads.GetAsync(scope, actor, token))?.Header.Status == Contract.ActorStatus.Finished);
                return view!.Header.TypeData;
            }

            async Task<Contract.ActorRef> StartAsync(string role, string operation)
            {
                var spec = new Contract.StartSpec(model.Type, role, operation);
                var result = await ingress.SendAsync(scope, new Contract.Target.Start(spec), operation, Contract.Kinds.Start,
                    cancellationToken: token);
                return result is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } actor } ? actor :
                    throw new InvalidOperationException($"Fixture actor start was refused: {result}");
            }

            async Task VerifyDeliveryAsync(Contract.ActorRef actor, string input, string wait, string payload,
                string? id = null, string correlationKey = key)
            {
                Contract.ActorView? seen = null;
                await UntilAsync(async () =>
                {
                    seen = await reads.GetAsync(scope, actor, token);
                    if (seen?.Park is not null) throw new InvalidOperationException(seen.Header.Fault);
                    return seen?.Header.Status == Contract.ActorStatus.Finished;
                });
                var delivery = JsonSerializer.Deserialize<string[]>(seen!.Header.TypeData)!;
                Check(delivery.Length == 5 && delivery[0] == input && delivery[1] == correlationKey && delivery[2] == wait &&
                    delivery[3] == payload && (id is null || delivery[4] == id),
                    "the exact actor handles its configured creator input before the published event");
            }

            async Task<List<CapturedPublicStart>> CapturedAsync()
            {
                await using var db = open();
                return await db.Database.SqlQuery<CapturedPublicStart>($"""
                    SELECT "Wait", "Registration", "Start", "Payload", "Reference", "EventId" AS "PackedEventId"
                    FROM "FoundationPublicEventTargets" WHERE "Tenant" = {scope.Tenant} OR "Tenant" = {other.Tenant}
                    """).ToListAsync(token);
            }

            async Task UntilAsync(Func<Task<bool>> done)
            {
                while (!await done()) await Task.Delay(20, token);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class PublicEventActor : Contract.IActorType
    {
        internal const string Key = "event:Orders";
        internal const string KeepingStartTopic = "retained-start-same-wait";
        internal TaskCompletionSource ReleaseFanout { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Type => "public-event-probe";
        public bool Withholds(Contract.ActorHeader header) => false;

        public async ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start)
                {
                    turn.State = message.Payload;
                    if (turn.Header.TypeData == "live-waiter") turn.Subscribe(Key, "live");
                    if (turn.Header.TypeData == "start-keeps") turn.Subscribe(KeepingStartTopic, "kept", keep: true);
                    if (turn.Header.TypeData == "publisher")
                    {
                        var id = turn.Publish(Key, "authored payload", TimeSpan.FromMinutes(1));
                        turn.SetHeader(id, turn.Header.Labels);
                        turn.End(Contract.ActorStatus.Finished);
                    }
                }
                if (message.Kind == "arm-keep") turn.Subscribe(message.Payload!, "kept", keep: true);
                if (message.Event is { } published)
                {
                    if (turn.Header.TypeData == "held-recipient") turn.Emit("held-event", message.Payload);
                    if (turn.Header.TypeData == "fanout-emits")
                    {
                        await ReleaseFanout.Task.WaitAsync(cancellationToken);
                        turn.Emit("held-fanout", message.Payload);
                    }
                    turn.SetHeader(JsonSerializer.Serialize(new[] { turn.State, published.Key, published.Wait, message.Payload, published.Id }),
                        turn.Header.Labels);
                    if (turn.State != KeepingStartTopic) turn.End(Contract.ActorStatus.Finished);
                }
            }
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
