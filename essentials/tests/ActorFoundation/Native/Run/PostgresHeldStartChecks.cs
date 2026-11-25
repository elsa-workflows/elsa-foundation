using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// K10 7b-2 (#4229): a held root start on a store with held replies. One node runs through PostgresPreviewHostedService with
// the Foundation's requests as its reply transport, and an edge in the Sim's shape matches a request's route
// (HttpRouteMatcher) and starts the route's workflow with the request as its trigger, holding the caller for the response.
// It runs on the direct-reply profile and depends only on its held-reply capability.
internal static class PostgresHeldStartChecks
{
    private const string Node = "held-start-node";
    private const long RequestTimeout = 10_000;

    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var stop = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootReplies(options);
        await using (var db = Open())
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the held-start check uses an empty PostgreSQL database");
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(1);
        await new PostgresRootAdmission(Open, TimeSpan.FromHours(1)).InstallAsync(stop);

        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("orders", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "orders"),
                Wf.Respond("created", Binding.Result("start", "Body.name"), 201))))
            .Add(Wf.Workflow("slow", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "slow"),
                Wf.Node<PostgresHeldDirectChecks.ApprovalSignal>("approve"),
                Wf.Respond("approved", "approved"))))
            .Add(Wf.Workflow("early", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "early", respondBeforeCommit: true),
                Wf.Respond("early-response", "early", 202))));
        var routes = new HttpRoutes(registry);
        var actor = new WorkflowActor.WorkflowActor([new NativeExecutionModel(registry)]);

        // Refused before anything runs: a store with held replies needs the transport its callers wait on.
        await RequireRefusedAsync(() => PostgresPreviewHosting.StartAsync(Open, Node, actor, _ => { }, cancellationToken: stop),
            "a node on a store with held replies needs the reply transport its callers wait on");

        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, WorkflowActor.WorkflowActor.ActorType,
            replyNode: Node, cancellationToken: stop);
        var requests = foundation.Requests ?? throw new InvalidOperationException("A reply node opens held requests.");
        var clock = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        var witness = new CommitWitness(Open, requests);
        var escalated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await RequireArgumentRefusedAsync(() => PostgresPreviewHosting.StartAsync(Open, "another-node", actor, _ => { },
                cancellationToken: stop, requests: requests),
            "a node is refused the held callers of another node");
        await using var service = new PostgresPreviewHostedService(Open, Node, actor, message => escalated.TrySetResult(message),
            new() { PollInterval = TimeSpan.FromMilliseconds(20) }, requests: requests) { ObserveReplies = _ => witness };
        await service.StartAsync(stop);
        var scope = EmbeddedTenant.Bind("held-start");
        try
        {
            // 1. An HTTP-style start, answered with its response.
            var created = await SendAsync(foundation, clock, routes, scope, "POST", "/Orders", new JsonObject { ["name"] = "Ada" }, "order-1", RequestTimeout, stop);
            Require(created.Reply is { Error: null, Channel: "response", Item: JsonNode } &&
                Values.To<HttpResponse>((JsonNode)created.Reply.Item!) is { StatusCode: 201 } response &&
                response.Body?.GetValue<string>() == "Ada",
                "an HTTP start admitted with a held reply is answered with its workflow's response");
            Require(witness.Sends.TryGetValue("Ada", out var finishedAtFirst) && finishedAtFirst == 1,
                "the start's response is released after the commit of the turn that wrote it");
            var order = await ReadRootAsync(store, scope, created.Actor!.Value, stop);
            Require(order is { Status: InstanceStatus.Finished }, "the started root is committed Finished");

            // 2. A repeat of the operation starts nothing and is not answered with the first start's response.
            var births = await BirthsAsync(Open, stop);
            var repeat = await SendAsync(foundation, clock, routes, scope, "POST", "/orders", new JsonObject { ["name"] = "Grace" }, "order-1", 1_500, stop);
            Require(repeat.Reply is { Error: "timeout", Status: Contract.ActorStatus.Finished, Channel: null } && await BirthsAsync(Open, stop) == births,
                "a repeated held start reaches the root its operation started, starts no other, and ends at its deadline with that root's status");

            // 3. Past its deadline the caller is answered with a timeout while the workflow keeps running.
            var slow = await SendAsync(foundation, clock, routes, scope, "POST", "/slow", new JsonObject(), "slow-1", 1_500, stop);
            Require(slow.Reply is { Error: "timeout", Status: Contract.ActorStatus.Running },
                "a held start whose workflow does not answer by the deadline ends with a timeout (504) while its root runs");
            // The root goes on: a held direct request to it is answered when it ends.
            var waiting = await store.ReadFirstRootAsync(scope.Tenant, slow.Actor!.Value.Id, stop);
            var frame = Json.Read<NativeState>(waiting!.State!).Frames.Values.Single(f => f.Node == "approve").Id;
            var approved = await foundation.RequestAsync(scope, new Contract.Target.Ref(slow.Actor.Value), "approve-1", CompositeSignal.Kind,
                Json.Write(new CompositeSignal(frame, "approve", "Ada")), null, new(null, await clock.ReadReplyTimeAsync(stop) + RequestTimeout), stop);
            Require(approved is { Error: null, Channel: "response", Item: JsonNode } &&
                Values.To<HttpResponse>((JsonNode)approved.Item!) is { StatusCode: 200 },
                "the timed-out start's root runs on, and a held direct request to it is answered with its response");
            var expired = await foundation.RequestAsync(scope, new Contract.Target.Start(Start(routes, "POST", "/slow", new JsonObject())),
                "slow-expired", Contract.Kinds.Start, null, null, new(null, 1), stop);
            Require(expired is { Error: "timeout", Status: null } && await BirthsAsync(Open, stop) == births + 1,
                "a held start whose deadline has already passed admits nothing");

            // A start this edge does not run is answered "missing", and admits nothing.
            var foreignType = await foundation.RequestAsync(scope, new Contract.Target.Start(new("another-type", "data")), "foreign-1",
                Contract.Kinds.Start, null, null, new(null, await clock.ReadReplyTimeAsync(stop) + RequestTimeout), stop);
            var ephemeral = await foundation.RequestAsync(scope,
                new Contract.Target.Start(Start(routes, "POST", "/orders", new JsonObject()) with { Ephemeral = true }), "ephemeral-1",
                Contract.Kinds.Start, null, null, new(null, await clock.ReadReplyTimeAsync(stop) + RequestTimeout), stop);
            Require(foreignType is { Error: "missing" } && ephemeral is { Error: "missing" } && await BirthsAsync(Open, stop) == births + 1,
                "a held start of another actor type, or an ephemeral one, is answered missing and admits nothing");

            // 4. A route that responds before the commit releases its response when the turn ends, before its root commits.
            var finishedBefore = witness.Finished(scope.Tenant);
            var early = await SendAsync(foundation, clock, routes, scope, "POST", "/early", new JsonObject(), "early-1", RequestTimeout, stop);
            Require(early.Reply is { Error: null, Channel: "response", Item: JsonNode } &&
                Values.To<HttpResponse>((JsonNode)early.Reply.Item!) is { StatusCode: 202 },
                "a start on a respond-before-commit route is answered with its response");
            Require(witness.Sends.TryGetValue("early", out var finishedAtEarly) && finishedAtEarly == finishedBefore,
                "a respond-before-commit response is released before the turn's commit");
            Require(await ReadRootAsync(store, scope, early.Actor!.Value, stop) is { Status: InstanceStatus.Finished },
                "the respond-before-commit root still commits Finished");

            Require(!escalated.Task.IsCompleted && service.LastError is null, "the node ran the held starts without a fault");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
        return 0;
    }

    private static Contract.StartSpec Start(HttpRoutes routes, string method, string path, JsonObject body)
    {
        var match = HttpRouteMatcher.Match(routes, method, path);
        var route = match.Route is { Start: true } start ? start : throw new InvalidOperationException($"No start route for {method} {path}.");
        return WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start(route.Workflow, triggerNode: route.Node,
            trigger: Values.From(new HttpRequest(method, path, body) { RouteValues = match.Values })));
    }

    // The edge: the route's start, held until the response or the deadline; the root it started, read back by its operation.
    private static async Task<(Contract.Reply Reply, Contract.ActorRef? Actor)> SendAsync(PostgresPreviewFoundation foundation,
        PostgresRootAdmission clock, HttpRoutes routes, Contract.BoundScope scope, string method, string path, JsonObject body, string id,
        long timeout, CancellationToken stop)
    {
        var route = HttpRouteMatcher.Match(routes, method, path).Route!;
        // Reply deadlines are in database milliseconds.
        var now = await clock.ReadReplyTimeAsync(stop);
        var reply = await foundation.RequestAsync(scope, new Contract.Target.Start(Start(routes, method, path, body)), id,
            Contract.Kinds.Start, null, null, new(null, now + timeout, route.RespondBeforeCommit), stop);
        // A start's repeat returns its root: the plain send of the same operation names it.
        var admitted = await foundation.SendAsync(scope, new Contract.Target.Start(Start(routes, method, path, body)), id, Contract.Kinds.Start,
            cancellationToken: stop);
        Require(admitted.Outcome == Contract.AdmissionOutcome.Duplicate, $"the held start '{id}' was admitted once");
        return (reply, admitted.Actor);
    }

    private static async Task<EfStore.RootImage?> ReadRootAsync(EfStore store, Contract.BoundScope scope, Contract.ActorRef actor, CancellationToken stop)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop) is { Status: not InstanceStatus.Running } done) return done;
            await Task.Delay(20, stop);
        }
        return await store.ReadFirstRootAsync(scope.Tenant, actor.Id, stop);
    }

    private static async Task<int> BirthsAsync(Func<KernelDb> open, CancellationToken stop)
    {
        await using var db = open();
        return await db.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "FoundationRootBirths" """).SingleAsync(stop);
    }

    private static async Task RequireArgumentRefusedAsync(Func<Task> start, string description)
    {
        try { await start(); }
        catch (ArgumentException) { Console.WriteLine($"ok {description}"); return; }
        throw new InvalidOperationException(description);
    }

    // The owner sends a turn's response and then its end back to back: each caller must take the response, however the
    // deliveries interleave. Part one sends both straight to the edge for many callers at once; part two runs many held
    // starts at once through a node that answers through the same edge.
    internal static async Task<int> RunOrderAsync(int callers, int starts)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(300));
        var stop = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootReplies(options);
        await using (var db = Open())
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the reply-order check uses an empty PostgreSQL database");
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(1);
        await new PostgresRootAdmission(Open, TimeSpan.FromHours(1)).InstallAsync(stop);
        var clock = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, WorkflowActor.WorkflowActor.ActorType,
            replyNode: Node, cancellationToken: stop);
        var requests = foundation.Requests!;
        IReplyTransport transport = requests;

        var now = await clock.ReadReplyTimeAsync(stop);
        var lost = 0;
        await Task.WhenAll(Enumerable.Range(0, callers).Select(i => Task.Run(async () =>
        {
            var caller = requests.Pending.OpenBound("caller-" + i, now + 120_000, "order", null);
            transport.Send(Node, new Reply(caller.Registration, $"k{i}#0", Request.Response, "response") { Tenant = "order" });
            transport.Send(Node, new Reply(caller.Registration, $"k{i}#end", null, null, InstanceStatus.Finished) { Tenant = "order" });
            if ((await requests.Pending.CompletionOf(caller).WaitAsync(stop)).Channel != Request.Response) Interlocked.Increment(ref lost);
        }, stop)));
        Require(lost == 0, $"each of {callers} callers sent a response and an end back to back, at once, takes the response");

        // A time read that fails while a response waits: its end, sent after the failure, does not overtake it.
        var failures = 0;
        var failing = 0;
        var readTime = requests.ReplyClock;
        requests.ReplyClock = token =>
        {
            if (Volatile.Read(ref failing) > 0)
            {
                Interlocked.Decrement(ref failing);
                Interlocked.Increment(ref failures);
                throw new TimeoutException("injected: the reply clock read failed");
            }
            return readTime(token);
        };
        var overtaken = 0;
        for (var i = 0; i < 20; i++)
        {
            var caller = requests.Pending.OpenBound("failing-" + i, now + 120_000, "order", null);
            var before = Volatile.Read(ref failures);
            Volatile.Write(ref failing, 2);
            transport.Send(Node, new Reply(caller.Registration, $"f{i}#0", Request.Response, "response") { Tenant = "order" });
            while (Volatile.Read(ref failures) == before) await Task.Delay(1, stop);
            transport.Send(Node, new Reply(caller.Registration, $"f{i}#end", null, null, InstanceStatus.Finished) { Tenant = "order" });
            // A dropped reply leaves its caller waiting: fail by name instead of at the run's deadline.
            Reply answer;
            try { answer = await requests.Pending.CompletionOf(caller).WaitAsync(TimeSpan.FromSeconds(10), stop); }
            catch (TimeoutException) { throw new InvalidOperationException($"caller {i} was never answered after a failed time read: a reply was dropped"); }
            if (answer.Channel != Request.Response) overtaken++;
        }
        requests.ReplyClock = readTime;
        Require(overtaken == 0 && failures >= 20,
            $"after {failures} failed time reads, each response is still delivered before the end sent after the failure");

        var registry = new WorkflowRegistry().Add(Wf.Workflow("orders", Wf.Sequence("main",
            Wf.HttpEndpoint("start", "orders"), Wf.Respond("created", Binding.Result("start", "Body.name"), 201))));
        var routes = new HttpRoutes(registry);
        await using var service = new PostgresPreviewHostedService(Open, Node, new WorkflowActor.WorkflowActor([new NativeExecutionModel(registry)]),
            message => throw new InvalidOperationException(message), new() { PollInterval = TimeSpan.FromMilliseconds(20) }, requests: requests);
        await service.StartAsync(stop);
        var scope = EmbeddedTenant.Bind("reply-order");
        int answered = 0, unanswered = 0;
        using var width = new SemaphoreSlim(16);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, starts).Select(async i =>
            {
                await width.WaitAsync(stop);
                try
                {
                    var reply = await foundation.RequestAsync(scope, new Contract.Target.Start(Start(routes, "POST", "/orders", new JsonObject { ["name"] = "n" + i })),
                        "order-" + i, Contract.Kinds.Start, null, null, new(null, await clock.ReadReplyTimeAsync(stop) + 30_000), stop);
                    if (reply is { Error: null, Channel: "response", Item: JsonNode item } && Values.To<HttpResponse>(item)?.Body?.GetValue<string>() == "n" + i)
                        Interlocked.Increment(ref answered);
                    else Interlocked.Increment(ref unanswered);
                }
                finally { width.Release(); }
            }));
        }
        finally { await service.StopAsync(CancellationToken.None); }
        Require(answered == starts && unanswered == 0, $"each of {starts} held starts, 16 at a time, is answered with its own response");
        return 0;
    }

    private static async Task RequireRefusedAsync(Func<Task> start, string description)
    {
        try { await start(); }
        catch (NotSupportedException) { Console.WriteLine($"ok {description}"); return; }
        throw new InvalidOperationException(description);
    }

    // Forwards each reply to the edge, and records, by the response's text, how many of its tenant's roots had committed
    // Finished when the owner sent it: a response released after its turn's commit counts its own root.
    private sealed class CommitWitness(Func<KernelDb> open, IReplyTransport edge) : IReplyTransport
    {
        internal System.Collections.Concurrent.ConcurrentDictionary<string, int> Sends { get; } = new(StringComparer.Ordinal);

        public void Send(string node, Reply reply)
        {
            if (reply is { Channel: Request.Response, Item: JsonNode item, Tenant: { } tenant } &&
                Values.To<HttpResponse>(item) is { Body: JsonValue body } && body.TryGetValue<string>(out var text))
                Sends.TryAdd(text, Finished(tenant));
            edge.Send(node, reply);
        }

        internal int Finished(string tenant)
        {
            using var db = open();
            return db.Database.SqlQuery<int>($"""
                SELECT count(*)::int AS "Value" FROM "Instances" i JOIN "FoundationRootBirths" b ON b."Reference" = i."FoundationReference"
                WHERE b."Tenant" = {tenant} AND i."Status" = {(int)InstanceStatus.Finished}
                """).Single();
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
