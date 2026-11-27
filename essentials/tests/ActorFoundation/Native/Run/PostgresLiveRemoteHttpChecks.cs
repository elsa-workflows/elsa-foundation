using System.Net;
using System.Text.Json.Nodes;
using Elsa.ActorFoundation.TestSupport;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using LiveRequest = Kernel.Durable.Request;
using NativeHttpRequest = Kernel.Native.HttpRequest;
using NativeHttpResponse = Kernel.Native.HttpResponse;
using LiveWorkflowActor = Kernel.WorkflowActor.WorkflowActor;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Kernel.Native.Run;

internal static class PostgresLiveRemoteHttpChecks
{
    internal static async Task<int> RunAsync()
    {
        var frames = new PostgresReplyFrames(new byte[32]);
        var channel = frames.Channel("wire-limit");
        var fits = new Reply("accepted", "answer", LiveRequest.Response, new string('π', 682 * 4_096));
        var wideName = new JsonObject { [new string('n', 6 * 1024 * 1024)] = true };
        Check(frames.Encode(channel, fits).Length > 3_000 && frames.Encode(channel, fits with { Item = wideName }).Length > 1_000,
            "wire-legal Unicode text and long JSON names fit within the byte limit");
        JsonNode? deep = null;
        for (var depth = 0; depth < 64; depth++) deep = new JsonArray(deep);
        var refusedDepth = false;
        try { frames.Encode(channel, fits with { Item = deep }); }
        catch (InvalidOperationException) { refusedDepth = true; }
        Check(refusedDepth, "the producer refuses a nested reply that cannot pass the receiver's wire depth");

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stop = timeout.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootShipped(options);
        var ownerConnection = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "elsa-live-reply-owner",
            MaxPoolSize = 1,
            Timeout = 3
        };
        var ownerOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(ownerConnection.ConnectionString).Options;
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the PostgreSQL database is empty");
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(1);
        await store.InstallPreviewRootSchemaAsync(stop);

        const string owner = "a-workflow-owner";
        const string caller = "z-http-caller";
        var scope = ContractScopes.Bind("postgres-live-replies");
        var body = new string('π', 96 * 1024);
        var serving = new KernelServing(capabilities: new(Schedules: false, Children: false, HttpWaits: false));
        await serving.ServeHostWorkflowAsync(Wf.Workflow("reply", Wf.Sequence("main",
            Wf.HttpEndpoint("open", "reply"), Wf.Respond("answer", body, 202))), stop);
        await serving.ServeHostWorkflowAsync(Wf.Workflow("oversized", Wf.Sequence("main",
            Wf.HttpEndpoint("open", "oversized"), Wf.Node<OverWireBudget>("answer"))), stop);
        await serving.ServeHostWorkflowAsync(Wf.Workflow("end-only", Wf.HttpEndpoint("open", "end-only")), stop);
        var flowchart = NativeExecutableMapper.Map(ExecutableOf("mapped-http", "mapped-http",
            Node("flow", Types.Flowchart, structure: Structure("elsa.flowchart.structure", new
            {
                startNodeId = "open",
                connections = new[] { new { source = new { nodeId = "open", port = "Done" }, target = new { nodeId = "answer" } } }
            }), slots: [Slot("Flowchart.Activities",
                Node("open", Types.HttpEndpoint, [Literal("Path", "flowchart"), Literal("SupportedMethods", new[] { "POST" }),
                    Literal("CanStartWorkflow", true, "Boolean")]),
                Node("answer", Types.WriteHttpResponse, [Literal("StatusCode", 201, "Int32"), Literal("Body", "mapped flowchart")]))])),
            serving.Capabilities);
        await serving.ServeHostWorkflowAsync(flowchart, stop);
        var model = new LiveWorkflowActor([new NativeExecutionModel(serving.Registry)]);
        var loops = new LoopOptions { BalanceEvery = TimeSpan.FromMilliseconds(125), PollInterval = TimeSpan.FromMilliseconds(25) };
        var edge = PostgresPreviewHosting.CreateRequests(Open, caller, model.Type);
        var ownerEdge = PostgresPreviewHosting.CreateRequests(Open, owner, model.Type);
        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, model.Type, edge, cancellationToken: stop);
        void Escalate(string message) => throw new InvalidOperationException(message);
        var refusedWire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var flushTimedOut = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = await PostgresPreviewHosting.StartAsync(Open, owner, model, Escalate, loops, stop,
            onFault: error =>
            {
                if (error is InvalidDataException) refusedWire.TrySetResult();
                if (error is TimeoutException) flushTimedOut.TrySetResult();
            }, requests: ownerEdge);
        while (await db.Partitions.AsNoTracking().Where(row => row.Id == 0).Select(row => row.Owner).SingleAsync(stop) != owner)
            await Task.Delay(25, stop);
        await using var second = await PostgresPreviewHosting.StartAsync(Open, caller, model, Escalate, loops, stop,
            onFault: error => Console.WriteLine($"receiver: {error.GetType().Name}: {error.Message}"), requests: edge);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var application = builder.Build();
        application.MapPost("/{workflow}", async (HttpContext context, string workflow) =>
        {
            var token = context.RequestAborted;
            var start = Start(workflow);
            var reply = await foundation.RequestAsync(scope, new Contract.Target.Start(start.Spec), workflow + "-id", Contract.Kinds.Start,
                null, null, new(null, await edge.ReadClockAsync(token) + (workflow == "oversized" ? 2_000 : 10_000), start.RespondBeforeCommit), token);
            var answer = KernelHttpEdge.Answer(reply);
            foreach (var field in answer.Headers) context.Response.Headers[field.Key] = field.Value;
            return Results.Text(answer.Body, answer.ContentType, statusCode: answer.StatusCode);
        });
        await application.StartAsync(stop);
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var delivered = await client.PostAsync("/reply", new StringContent(""), stop);
        var received = await delivered.Content.ReadAsStringAsync(stop);
        Check(delivered.StatusCode == HttpStatusCode.Accepted && received == body,
            $"the live HTTP response reconstructs all notification frames in order (HTTP {(int)delivered.StatusCode}, {received.Length} characters)");
        var run = new Contract.ActorRef(delivered.Headers.GetValues("X-Elsa-Workflow-Instance-Id").Single());
        var actor = await foundation.GetAsync(scope, run, stop);
        var checkpoint = await store.ReadFirstRootAsync(scope.Tenant, run.Id, stop);
        if (checkpoint is not { Revision: 1, TerminalRevision: 1, Status: InstanceStatus.Finished })
            throw new InvalidOperationException("The remote response must have one committed terminal checkpoint.");
        Check(actor is { Header.Status: Contract.ActorStatus.Finished } &&
            await db.Partitions.AsNoTracking().Where(row => row.Id == 0).Select(row => row.Owner).SingleAsync(stop) == owner &&
            !await db.Messages.AsNoTracking().AnyAsync(row => row.Target == checkpoint.Actor.Id && row.ReplyTo != null, stop),
            "the remote owner committed once and cleared the live route before delivery");

        using var mapped = await client.PostAsync("/flowchart", new StringContent(""), stop);
        Check(mapped.StatusCode == HttpStatusCode.Created && await mapped.Content.ReadAsStringAsync(stop) == "mapped flowchart" &&
            mapped.Headers.GetValues(KernelHttpEdge.WorkflowInstanceIdHeader).Single() is { } mappedId &&
            await store.ReadFirstRootAsync(scope.Tenant, mappedId, stop) is { Status: InstanceStatus.Finished, TerminalRevision: 1 } &&
            await db.Partitions.AsNoTracking().Where(row => row.Id == 0).Select(row => row.Owner).SingleAsync(stop) == owner,
            "the mapped Flowchart answers through the actual carrier after its remote owner commits");
        using var oversized = await client.PostAsync("/oversized", new StringContent(""), stop);
        await refusedWire.Task.WaitAsync(stop);
        Check(oversized.StatusCode == HttpStatusCode.GatewayTimeout &&
            await oversized.Content.ReadAsStringAsync(stop) == "the workflow did not answer in time",
            "an authored reply over the real carrier's byte limit leaves the caller to time out");
        var original = await foundation.SendAsync(scope, new Contract.Target.Start(Start("oversized").Spec), "oversized-id",
            Contract.Kinds.Start, cancellationToken: stop);
        Check(original is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } oversizedActor } &&
            await store.ReadFirstRootAsync(scope.Tenant, oversizedActor.Id, stop) is { Status: InstanceStatus.Finished, TerminalRevision: 1 },
            "wire failure leaves the authored terminal commit intact");
        using var endOnly = await client.PostAsync("/end-only", new StringContent(""), stop);
        Check(endOnly.StatusCode == HttpStatusCode.NoContent && endOnly.Headers.Contains(KernelHttpEdge.WorkflowInstanceIdHeader),
            "an actor that ends without emitting still answers with its committed terminal status");

        var deadline = await edge.ReadClockAsync(stop) + 10_000;
        var remoteWait = edge.Pending.OpenBound("remote-handoff", deadline, scope.Tenant, ambient: null);
        var localWait = edge.Pending.OpenBound("local-reset", deadline, scope.Tenant, ambient: null);
        Reply Lose(LiveRequest request) => new(request.Registration, "lost", null, null, Error: "lost") { Tenant = scope.Tenant };
        var ownerTransport = (IReplyTransport)ownerEdge;
        ownerTransport.Send(caller, Lose(remoteWait));
        ownerTransport.Send(caller, new(remoteWait.Registration, "answer", LiveRequest.Response, "successor") { Tenant = scope.Tenant });
        ((IReplyTransport)edge).Send(caller, Lose(localWait));
        Check((await edge.Pending.CompletionOf(remoteWait).WaitAsync(stop)).Item is "successor" &&
              (await edge.Pending.CompletionOf(localWait).WaitAsync(stop)).Error == "lost",
            "remote handoff keeps the receiving wait open and local reset ends it");

        using (var narrowStop = CancellationTokenSource.CreateLinkedTokenSource(stop))
        {
            var narrow = await PostgresReplyRelay.OpenAsync(OpenOwner, "single-connection-relay", _ => { }, onFault: null, stop);
            var listening = narrow.RunAsync(narrowStop.Token);
            try
            {
                await narrow.ReadyAsync(stop);
                var pending = edge.Pending.OpenBound("single-pool", deadline, scope.Tenant, ambient: null);
                narrow.Send(caller, new(pending.Registration, "answer", LiveRequest.Response, "one connection") { Tenant = scope.Tenant });
                Check((await edge.Pending.CompletionOf(pending).WaitAsync(TimeSpan.FromSeconds(5), stop)).Item is "one connection",
                    "the listener and publisher share a pool of one when the context opens its own connection");
            }
            finally
            {
                await narrowStop.CancelAsync();
                await listening;
            }
        }

        var stall = new StalledValue();
        ownerTransport.Send(caller, new("detached", "stalled", LiveRequest.Response, stall) { Tenant = scope.Tenant });
        try
        {
            await stall.Entered.Task.WaitAsync(stop);
            await first.StopAsync(stop).AsTask().WaitAsync(TimeSpan.FromSeconds(6), stop);
            Check(flushTimedOut.Task.IsCompletedSuccessfully && await db.Partitions.AsNoTracking().Where(row => row.Id == 0)
                .Select(row => row.Owner).SingleAsync(stop) != owner,
                "a stalled reply publisher cannot hold shutdown after durable ownership is released");
        }
        finally { stall.Release.TrySetResult(); }

        await application.StopAsync(stop);
        return 0;

        KernelDb OpenOwner()
        {
            var context = KernelDb.ForPostgresRootShipped(ownerOptions);
            try
            {
                context.Database.OpenConnection();
                return context;
            }
            catch
            {
                context.Dispose();
                throw;
            }
        }

        KernelHttpStart Start(string workflow)
        {
            var path = "/" + workflow;
            return KernelHttpEdge.Match(serving, new NativeHttpRequest("POST", path, new JsonObject()), path)
                ?? throw new InvalidOperationException("The fixture route does not start its registered workflow.");
        }
    }

    private sealed class StalledValue
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Value
        {
            get
            {
                Entered.TrySetResult();
                Release.Task.GetAwaiter().GetResult();
                return "released";
            }
        }
    }

    public sealed class OverWireBudget : Activity
    {
        protected override ValueTask<Transition> ExecuteAsync(ActivityContext context)
        {
            var body = JsonValue.Create(new string('π', 3 * 1024 * 1024));
            var reply = Values.From(new NativeHttpResponse(202, "text/plain", body));
            return new(Complete().With(new EmitEffect("response", reply)));
        }
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
