using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    private sealed record CapturedPublicStart(string Wait, string Registration, string Start, string? Payload, string Reference);

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
            var (open, ingress, reads, owner, _) = await OpenAsync(model, provider, Path.Combine(directory.FullName, "store.db"),
                inBatch, token, publicStarts: true);
            await using var owned = owner;
            var initial = new Contract.StartSpec(model.Type, "starter", "first\0template");
            var replacement = initial with { Input = "replacement" };
            await ingress.SetStartAsync(scope, key, "starter", initial, token);
            await ingress.SetStartAsync(scope, key, "starter", initial, token);
            await ingress.SetStartAsync(scope, "event:orders", "starter", replacement, token);
            await ingress.SetStartAsync(other, key, "starter", replacement, token);
            await StartPublisherAsync("one");
            var first = (await CapturedAsync()).Single();
            Check(first.Wait == "starter" && DurableKeyJson.Read<Contract.StartSpec>(first.Start) == initial &&
                DurableKeyJson.Read<string>(first.Payload!) == "authored payload",
                "both engines select exactly one ordinal, tenant-bound registration");
            await VerifyDeliveryAsync(new(first.Reference), initial.Input!, "starter", "authored payload");

            await ingress.SetStartAsync(scope, key, "starter", replacement, token);
            await StartPublisherAsync("two");
            var second = (await CapturedAsync()).Single(item => item.Registration != first.Registration);
            Check(DurableKeyJson.Read<Contract.StartSpec>(second.Start) == replacement && (await CapturedAsync()).Contains(first),
                "replacement affects later publications and preserves the original captured template");
            await VerifyDeliveryAsync(new(second.Reference), replacement.Input!, "starter", "authored payload");

            await ingress.SetStartAsync(scope, key, "starter", null, token);
            await ingress.SetStartAsync(scope, key, "starter", null, token);
            await StartPublisherAsync("three");
            Check((await CapturedAsync()).Count == 2, "idempotent unpublish excludes the removed registration");

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
            var third = (await CapturedAsync()).Single(item => item.Wait == "outside");
            var sibling = (await CapturedAsync()).Single(item => item.Wait == "outside-also");
            Check(sibling.Reference != third.Reference, "one publication gives each registered start a distinct exact root");
            await VerifyDeliveryAsync(live, "one live waiter", "live", "outside payload", "event\0id");
            await VerifyDeliveryAsync(new(third.Reference), replacement.Input!, "outside", "outside payload", "event\0id");
            await VerifyDeliveryAsync(new(sibling.Reference), initial.Input!, "outside-also", "outside payload", "event\0id");
            var tooWide = await ingress.SendAsync(scope, target, "too-wide", "ignored", new string('x', 9 * 1024 * 1024), options, token);
            Check(tooWide is { Outcome: Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.InboxFull, Publication: null } &&
                (await CapturedAsync()).Count == 4, "a shared fanout overflow creates no extra target or accepted receipt");

            var quiet = new Contract.Target.Event("quiet");
            var kept = await ingress.SendAsync(scope, quiet, "unused", "ignored", "kept payload", options, token);
            Check(kept is { Outcome: Contract.AdmissionOutcome.Accepted, Publication: not null },
                "an event still commits a receipt and retained payload when there is no waiter");
            await using (var db = open())
            {
                var valid = await db.Database.SqlQuery<bool>($"""
                    SELECT "KeepUntil" > 0 AND "ReceiptUntil" > "KeepUntil" AND "Payload" = {DurableKeyJson.Write("kept payload")} AS "Value"
                    FROM "FoundationPublicEventPublications" WHERE "Tenant" = {scope.Tenant} AND "Key" = {scope.Tenant + "_quiet"}
                    """).SingleAsync(token);
                Check(valid, "the stored retention and replay deadlines are independent");
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationPublicEventPublications" SET "ReceiptUntil" = 0
                    WHERE "Tenant" = {scope.Tenant} AND "Key" = {scope.Tenant + "_" + key}
                    """, token);
            }
            var recreated = await ingress.SendAsync(scope, target, "event\0id", "ignored", "second incarnation", options, token);
            var fourth = (await CapturedAsync()).Single(item => item.Wait == "outside" && item.Reference != third.Reference);
            Check(recreated is { Outcome: Contract.AdmissionOutcome.Accepted, Publication: not null } &&
                recreated.Publication.Incarnation != outside.Publication!.Incarnation && fourth.Registration == third.Registration,
                "reuse after receipt expiry preserves the registration and creates a different event and actor life");
            await VerifyDeliveryAsync(new(fourth.Reference), replacement.Input!, "outside", "second incarnation", "event\0id");
            return 0;

            async Task StartPublisherAsync(string operation)
            {
                var actor = await StartAsync("publisher", operation);
                await UntilAsync(async () => (await reads.GetAsync(scope, actor, token))?.Header.Status == Contract.ActorStatus.Finished);
            }

            async Task<Contract.ActorRef> StartAsync(string role, string operation)
            {
                var spec = new Contract.StartSpec(model.Type, role, operation);
                var result = await ingress.SendAsync(scope, new Contract.Target.Start(spec), operation, Contract.Kinds.Start,
                    cancellationToken: token);
                return result is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } actor } ? actor :
                    throw new InvalidOperationException($"Fixture actor start was refused: {result}");
            }

            async Task VerifyDeliveryAsync(Contract.ActorRef actor, string input, string wait, string payload, string? id = null)
            {
                Contract.ActorView? seen = null;
                await UntilAsync(async () =>
                {
                    seen = await reads.GetAsync(scope, actor, token);
                    if (seen?.Park is not null) throw new InvalidOperationException(seen.Header.Fault);
                    return seen?.Header.Status == Contract.ActorStatus.Finished;
                });
                var delivery = JsonSerializer.Deserialize<string[]>(seen!.Header.TypeData)!;
                Check(delivery.Length == 5 && delivery[0] == input && delivery[1] == key && delivery[2] == wait &&
                    delivery[3] == payload && (id is null || delivery[4] == id),
                    "the exact actor handles its configured creator input before the published event");
            }

            async Task<List<CapturedPublicStart>> CapturedAsync()
            {
                await using var db = open();
                return await db.Database.SqlQuery<CapturedPublicStart>($"""
                    SELECT "Wait", "Registration", "Start", "Payload", "Reference"
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
                        turn.Publish(Key, "authored payload", TimeSpan.FromMinutes(1));
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
