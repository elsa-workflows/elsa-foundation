using System.Text.Json;
using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    internal static async Task<int> RunPublicEventRecoverAsync(string provider, bool inBatch)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(85));
        var token = stop.Token;
        var scope = ContractScopes.Bind("kept-recover");
        var model = new KeepingRecoverActor();
        var directory = Directory.CreateTempSubdirectory("kept-recover");
        try
        {
            var (open, ingress, reads, host, _, restart) = await OpenAsync(model, provider,
                Path.Combine(directory.FullName, "store.db"), inBatch, token, publicStarts: true,
                authorizeOperator: (called, _, kind) => called == scope && kind == Contract.Kinds.Recover);
            await using var owner = host;
            var started = await ingress.SendAsync(scope, new Contract.Target.Start(new(model.Type, "recover-fixture", null)),
                "start", Contract.Kinds.Start, null, cancellationToken: token);
            var actor = started.Actor ?? throw new InvalidOperationException($"Fixture start was refused: {started}");
            var exact = new Contract.Target.Ref(actor);
            var topic = new Contract.Target.Event(KeepingRecoverActor.Topic);
            var qualified = scope.Tenant + "_" + KeepingRecoverActor.Topic;

            await UntilAsync(async () =>
            {
                await using var db = open();
                return await db.Subscriptions.CountAsync(wait => wait.Key == qualified, token) == 2;
            });
            Check((await ingress.SendAsync(scope, exact, "fault", "fault", null, cancellationToken: token)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "the actor receives a deliberate failing turn");
            Contract.ActorView? parked = null;
            await UntilAsync(async () =>
            {
                parked = await reads.GetAsync(scope, actor, token);
                return parked is { Header.Status: Contract.ActorStatus.Faulted, Park: not null };
            });
            await using (var db = open())
                Check(!await db.Subscriptions.AnyAsync(wait => wait.Key == qualified, token), "Park removes both waits");

            // A page with no eligible publication still advances. Zero-window publications expire before Recover.
            for (var index = 0; index <= InMemoryStore.ParkPageLimit; index++)
            {
                var expired = await ingress.SendAsync(scope, topic, "expired-" + index, "ignored", null, cancellationToken: token);
                if (expired.Outcome != Contract.AdmissionOutcome.Accepted)
                    throw new InvalidOperationException($"Zero-window event {index} was refused: {expired}");
            }

            const int retainedCount = 2 * InMemoryStore.ParkPageLimit + 1;
            var retain = new Contract.AddOptions(KeepFor: TimeSpan.FromMinutes(5));
            for (var index = 0; index < retainedCount; index++)
            {
                var admission = await ingress.SendAsync(scope, topic, "parked-" + index, "ignored", null, retain, token);
                if (admission.Outcome != Contract.AdmissionOutcome.Accepted)
                    throw new InvalidOperationException($"Retained event {index} was refused: {admission}");
            }

            var command = JsonSerializer.Serialize(new Contract.OperatorCommand(parked!.Selection, ExpectedTarget: actor));
            Check((await ingress.SendAsync(scope, exact, "recover", Contract.Kinds.Recover, command, cancellationToken: token)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "Recover accepts the exact Park selection");
            await UntilAsync(async () =>
            {
                var view = await reads.GetAsync(scope, actor, token);
                if (view?.Header.Status == Contract.ActorStatus.Faulted &&
                    (await reads.TailAsync(scope, actor, cancellationToken: token)).Items.Any(item =>
                        item.Entry.Contains("recover", StringComparison.Ordinal) && item.Entry.Contains("refused", StringComparison.Ordinal)))
                    throw new InvalidOperationException("The provider refused a valid keeping Recover plan.");
                return view is { Header.Status: Contract.ActorStatus.Running, Park: null };
            });
            Check((await ingress.SendAsync(scope, topic, "after-open", "ignored", null, cancellationToken: token)).Outcome ==
                Contract.AdmissionOutcome.Accepted, "the live event joins both restored waits after Recover");

            await UntilAsync(async () =>
            {
                await using var db = open();
                return await db.Database.SqlQuery<bool>($"""
                    SELECT EXISTS (SELECT 1 FROM "FoundationPublicEventLiveWaits"
                        WHERE "Key" = {qualified} AND "Wait" = {KeepingRecoverActor.Keeping}
                            AND "CatchAfter" > 0 AND "CatchAfter" < "CatchThrough") AS "Value"
                    """).SingleAsync(token);
            });
            await owner.DisposeAsync();
            await using (var db = open())
            {
                var checkpoint = await db.Database.SqlQuery<long>($"""
                    SELECT "CatchAfter" AS "Value" FROM "FoundationPublicEventLiveWaits"
                    WHERE "Key" = {qualified} AND "Wait" = {KeepingRecoverActor.Keeping} AND "CatchAfter" < "CatchThrough"
                    """).SingleAsync(token);
                Check(checkpoint > 0, "owner shutdown preserves a committed incomplete catch-up cursor");
            }

            await using var successor = await restart(token);
            var expected = $"{retainedCount + 1}:1";
            await UntilAsync(async () =>
            {
                var view = await reads.GetAsync(scope, actor, token);
                if (view?.Park is not null) throw new InvalidOperationException(view.Header.Fault);
                return view?.Header.TypeData == expected;
            });
            await using (var db = open())
            {
                var settled = await db.Database.SqlQuery<bool>($"""
                    SELECT count(*) = 1 AND min("CatchAfter") = min("CatchThrough") AS "Value"
                    FROM "FoundationPublicEventLiveWaits" WHERE "Key" = {qualified}
                    """).SingleAsync(token);
                Check(settled, "restart finishes exactly one keeping cursor; the ordinary wait receives only the new event");
            }
            return 0;

            async Task UntilAsync(Func<Task<bool>> done)
            {
                while (!await done()) await Task.Delay(20, token);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class KeepingRecoverActor : Contract.IActorType
    {
        internal const string Topic = "recover-events";
        internal const string Keeping = "keeping";
        private const string Ordinary = "ordinary";

        public string Type => "kept-recover-check";
        public bool Withholds(Contract.ActorHeader header) => false;

        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            foreach (var message in turn.Inbox)
            {
                if (message.Kind == Contract.Kinds.Start)
                {
                    turn.State = "[]";
                    turn.Subscribe(Topic, Keeping, keep: true);
                    turn.Subscribe(Topic, Ordinary);
                }
                if (message.Kind == "fault") throw new InvalidOperationException("deliberate fixture fault");
                if (message.Event is not { } received) continue;

                if (received.Id.StartsWith("expired-", StringComparison.Ordinal) ||
                    received.Wait != Keeping && (received.Wait != Ordinary || received.Id != "after-open"))
                    throw new InvalidOperationException($"Delivery {received.Id} reached ineligible wait {received.Wait}.");
                var seen = JsonSerializer.Deserialize<HashSet<string>>(turn.State!)!;
                if (!seen.Add(received.Wait + "/" + received.Id))
                    throw new InvalidOperationException($"Duplicate delivery {received.Id} for {received.Wait}.");
                turn.State = JsonSerializer.Serialize(seen);
                var kept = seen.Count(item => item.StartsWith(Keeping + "/", StringComparison.Ordinal));
                turn.SetHeader($"{kept}:{seen.Count - kept}", turn.Header.Labels);
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) =>
            new([], [], [new(Topic, Keeping, Keep: true), new(Topic, Ordinary, Keep: false)]);
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
