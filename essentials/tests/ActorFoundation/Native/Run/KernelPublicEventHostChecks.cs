using Elsa.ActorFoundation.TestSupport;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Foundation.Sqlite;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static partial class KernelAuthoredEventChecks
{
    internal static async Task<int> RunPublicEventHostAsync(string provider, bool inBatch, bool protectedStarts, bool observeReplies = false)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = stop.Token;
        var scope = ContractScopes.Bind("event-host");
        var ambient = new object();
        var liveAmbient = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var model = new PublicEventActor(value => liveAmbient.TrySetResult(value));
        var directory = Directory.CreateTempSubdirectory("public-event-host-");
        var path = Path.Combine(directory.FullName, "events.db");
        var host = new RootPublicEventHost(model.Type, protectedStarts);
        try
        {
            var frames = new TaskCompletionSource<Reply>(TaskCreationOptions.RunContinuationsAsynchronously);
            var (open, ingress, reads, owner, _, restart) = await OpenAsync(model, provider, path, inBatch, token, publicEventHost: host,
                observeReplies: observeReplies ? transport => new EventReplyObserver(transport, reply =>
                {
                    if (reply.Status is not null) frames.TrySetResult(reply);
                }) : null);
            await using (owner)
            {
                const string startKey = "outside-start";
                var creator = new Contract.StartSpec(model.Type, "held-recipient", "event start");
                if (protectedStarts)
                    await RequireRefusalAsync<NotSupportedException>(() => ingress.SetStartAsync(scope, startKey, "start", creator, token).AsTask());
                else
                    await ingress.SetStartAsync(scope, startKey, "start", creator, token);

                var startReply = await RequestAsync(startKey, "creator", "start item");
                Check(protectedStarts ? startReply.Error == "no waiter" :
                    startReply is { Channel: "held-event", Error: null } && startReply.Item?.ToString() == "start item",
                    "the durable host authority decides whether a public wait can create an actor");

                var live = await ingress.SendAsync(scope, new Contract.Target.Start(new(model.Type, "live-waiter", "fixture")),
                    "waiter", Contract.Kinds.Start, cancellationToken: token);
                Check(live is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "the fixture has a durable live waiter");
                await using (var db = open())
                    while (!await db.Subscriptions.AnyAsync(item => item.Key == scope.Tenant + "_" + PublicEventActor.Key &&
                        item.Wait == "live", token))
                        await Task.Delay(25, token);

                var liveReply = await RequestAsync(PublicEventActor.Key, "live", "live item", ambient);
                Check(liveReply is { Status: Contract.ActorStatus.Finished, Error: null }, "the Event host still holds replies from exact live waits");
                Check(liveAmbient.Task.IsCompletedSuccessfully && ReferenceEquals(liveAmbient.Task.Result, ambient),
                    "the live Event actor receives the caller's exact nonnull Ambient object");
                if (observeReplies)
                    Check(frames.Task.IsCompletedSuccessfully && frames.Task.Result is { Channel: null, Error: null } frame &&
                        frame.Tenant == scope.Tenant, "the observer saw the tenant-bound terminal reply on the protected hosted node");
                var details = await reads.GetAsync(scope, live.Actor!.Value, token);
                Check(details is not null && DurableKeyJson.Read<string[]>(details.Header.TypeData) is [_, PublicEventActor.Key, "live", "live item", "live"],
                    "the live waiter received the exact outside Event even when generic starts were closed");
            }

            if (provider == "postgres")
            {
                await using var source = open();
                var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(source.Database.GetConnectionString()).Options;
                await RequireRefusalAsync<StoreFormatException>(() => InstallAsync(model.Type, !protectedStarts));
                await RequireRefusalAsync<StoreFormatException>(() => InstallAsync("different-actor", protectedStarts));
                KernelDb WrongMode() => PostgresPreviewHosting.CreatePublicEventHostQualificationContext(options, model.Type, !protectedStarts);
                await RequireRefusalAsync<StoreFormatException>(() =>
                    PostgresPreviewHosting.OpenFoundationAsync(WrongMode, model.Type, cancellationToken: token));
                var standalone = await PostgresPreviewHosting.OpenFoundationAsync(open, model.Type, cancellationToken: token);
                var standaloneSend = await standalone.SendAsync(scope, new Contract.Target.Event("standalone"), "standalone-event",
                    "ignored", cancellationToken: token);
                Check(standaloneSend is { Outcome: Contract.AdmissionOutcome.Accepted, Publication.EventId: "standalone-event" },
                    "standalone PostgreSQL ingress supports public Event publication with the exact installed binding");

                async Task InstallAsync(string actorType, bool protectsStarts)
                {
                    await using var db = PostgresPreviewHosting.CreatePublicEventHostQualificationContext(options, actorType, protectsStarts);
                    await PostgresRootAdmission.InstallAsync(db, token);
                }
            }
            else
            {
                var wrong = SqliteEmbeddedFoundation.ForPublicEventHostQualification(path, "reopened", model.Type,
                    protectedStarts ? null : (_, _) => ValueTask.FromResult(true));
                await RequireRefusalAsync<StoreFormatException>(() => wrong.InstallShippedAsync(token));
                var wrongType = SqliteEmbeddedFoundation.ForPublicEventHostQualification(path, "reopened", "different-actor",
                    protectedStarts ? (_, _) => ValueTask.FromResult(true) : null);
                await RequireRefusalAsync<StoreFormatException>(() => wrongType.InstallShippedAsync(token));
                if (protectedStarts)
                {
                    var wrongRequests = SqliteEmbeddedHosting.CreateRequests(path, "reopened", model.Type,
                        qualificationFormat: StoreFormat.RootPublicEventHostQualification);
                    await RequireRefusalAsync<StoreFormatException>(() =>
                        SqliteEmbeddedHosting.OpenFoundationWithRequestsAsync(path, model.Type, wrongRequests, token));
                }
            }

            await using var resumed = await restart(token);
            var following = await ingress.SendAsync(scope, new Contract.Target.Event("after-reopen"), "outside", "ignored",
                cancellationToken: token);
            Check(following is { Outcome: Contract.AdmissionOutcome.Accepted, Publication.EventId: "outside" },
                "the exact owner reopens without changing public publication authority");
            return 0;

            async Task<Contract.Reply> RequestAsync(string key, string id, string item, object? callerAmbient = null)
            {
                await using var db = open();
                var now = provider == "postgres" ? await RootEventAdmission.ReadPostgresClockAsync(db, token)
                    : await SqliteRootAdmission.ReadClockAsync(db, token);
                return await ingress.RequestAsync(scope, new Contract.Target.Event(key), id, Contract.Kinds.Event, item, null,
                    new(callerAmbient, now + 6000), token);
            }
        }
        finally { directory.Delete(recursive: true); }
    }

    private sealed class EventReplyObserver(IReplyTransport edge, Action<Reply>? observe = null) : IReplyTransport
    {
        public void Send(string node, Reply reply)
        {
            observe?.Invoke(reply);
            edge.Send(node, reply);
        }
    }

    private static async Task RequireRefusalAsync<TException>(Func<Task> work) where TException : Exception
    {
        try { await work(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
