using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
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
            var (open, ingress, reads, owner, _, restart) = await OpenAsync(model, provider, Path.Combine(directory.FullName, "store.db"),
                inBatch, token, publicStarts: true);
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
                    UPDATE "FoundationPublicEventPublications" SET "ReceiptUntil" = 0
                    WHERE "Tenant" = {scope.Tenant} AND "Key" = {scope.Tenant + "_" + key}
                    """, token);
            }
            var recreated = await ingress.SendAsync(scope, target, "event\0id", "ignored", "second incarnation", options, token);
            var fourth = (await CapturedAsync()).Single(item => item.Wait == "outside" && item.EventId == "event\0id" && item.Reference != third.Reference);
            Check(recreated is { Outcome: Contract.AdmissionOutcome.Accepted, Publication: not null } &&
                recreated.Publication.Incarnation != outside.Publication!.Incarnation && fourth.Registration == third.Registration,
                "reuse after receipt expiry preserves the registration and creates a different event and actor life");
            await VerifyDeliveryAsync(new(fourth.Reference), replacement.Input!, "outside", "second incarnation", "event\0id");
            if (owner is RuntimeNode postgresOwner) await postgresOwner.StopAsync(token);
            else await ((SqliteEmbeddedHostedService)owner).StopAsync(token);

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

            await Task.Delay(quietOptions.KeepFor!.Value + TimeSpan.FromMilliseconds(100), token);
            await using var successor = await restart(token);
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
                    SELECT r."CatchThrough" = 0 AND p."KeepUntil" < r."RetainAfter" AS "Value"
                    FROM "FoundationPublicEventStarts" r JOIN "FoundationPublicEventPublications" p
                        ON p."Tenant" = r."Tenant" AND p."Key" = r."Key"
                    WHERE r."Tenant" = {scope.Tenant} AND r."Key" = {scope.Tenant + "_" + quietKey} AND r."Wait" = 'after-clock-backstep'
                    """).SingleAsync(token);
                Check(barred && backdated.Outcome == Contract.AdmissionOutcome.Accepted,
                    "retention cleanup's tenant watermark survives a clock backstep without changing publication acceptance");
            }
            return 0;

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
        public string Type => "public-event-probe";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start)
                {
                    turn.State = message.Payload;
                    if (turn.Header.TypeData == "live-waiter") turn.Subscribe(Key, "live");
                    if (turn.Header.TypeData == "publisher")
                    {
                        var id = turn.Publish(Key, "authored payload", TimeSpan.FromMinutes(1));
                        turn.SetHeader(id, turn.Header.Labels);
                        turn.End(Contract.ActorStatus.Finished);
                    }
                }
                if (message.Event is { } published)
                {
                    turn.SetHeader(JsonSerializer.Serialize(new[] { turn.State, published.Key, published.Wait, message.Payload, published.Id }),
                        turn.Header.Labels);
                    turn.End(Contract.ActorStatus.Finished);
                }
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => new([], [], []);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
