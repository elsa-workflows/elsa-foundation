using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// K10 (#4229): on the shipped format 24, a root's output reaches the callers its owner holds for it, whichever turn
// consumed their delivery, and a Native workflow that ends Faulted commits, its held callers answered with that end, as
// the in-memory reference's Worker.Release does. One node runs through PostgresPreviewHostedService with held requests.
internal static class PostgresHeldCallerChecks
{
    private const string Node = "held-callers-node";
    private const long RequestTimeout = 20_000;

    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        var stop = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL preview database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => PostgresPreviewHosting.CreateShippedContext(options);
        await using (var db = Open())
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the held-caller check uses an empty PostgreSQL database");
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(1);
        await store.InstallPreviewRootSchemaAsync(stop);

        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("orders", Wf.Sequence("main",
                Wf.HttpEndpoint("start", "orders"),
                Wf.Http("send", "https://partner.example/ok", expectedStatusCodes: [200]),
                Wf.Respond("created", "created", 201))))
            .Add(Wf.Workflow("approval", Wf.Sequence("main",
                Wf.Node<PostgresHeldDirectChecks.ApprovalSignal>("approve"),
                Wf.Http("send", "https://partner.example/ok", expectedStatusCodes: [200]),
                Wf.Respond("approved", "approved"))))
            .Add(Wf.Workflow("slow", Wf.Sequence("main",
                Wf.Http("send", "https://partner.example/slow", expectedStatusCodes: [200]),
                Wf.Respond("late", "late"))))
            .Add(Wf.Workflow("refused", Wf.Sequence("main",
                Wf.Http("send", "https://partner.example/ok",
                    headers: Binding.Literal(new JsonObject { ["Authorization"] = "Bearer not-through-a-connection" })),
                Wf.Respond("unreached", "unreached"))))
            .Add(Wf.Workflow("faults", Wf.Sequence("main", Wf.Fault("boom", Binding.Literal("deliberate"), Binding.Literal("a deliberate fault")))))
            .Add(Wf.Workflow("quick", Wf.Sequence("main", Wf.WriteLine("done", "done"))));
        var routes = new HttpRoutes(registry);
        var partner = new Partner();
        var actor = new WorkflowActor.WorkflowActor([new NativeExecutionModel(registry,
            new NativeExecutionOptions { Http = new NativeHttpOptions { Client = new HttpClient(partner) } })]);
        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, WorkflowActor.WorkflowActor.ActorType,
            PostgresPreviewHosting.CreateRequests(Open, Node, WorkflowActor.WorkflowActor.ActorType), cancellationToken: stop);
        var clock = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        var scope = EmbeddedTenant.Bind("held-callers");
        var escalated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var service = new PostgresPreviewHostedService(Open, Node, actor, message => escalated.TrySetResult(message),
            new() { PollInterval = TimeSpan.FromMilliseconds(20) }, requests: foundation.Requests);
        await service.StartAsync(stop);
        try
        {
            // (a) An HTTP start whose response follows a SendHttpRequest job.
            var match = HttpRouteMatcher.Match(routes, "POST", "/orders");
            var orders = await RequestAsync(new Contract.Target.Start(WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start(
                    match.Route!.Workflow, triggerNode: match.Route.Node,
                    trigger: Values.From(new HttpRequest("POST", "/orders", new JsonObject()) { RouteValues = match.Values })))),
                "orders-1", Contract.Kinds.Start, null, RequestTimeout);
            Require(Status(orders) == 201 && partner.Calls("/ok") == 1,
                "an HTTP start is answered with the response written after its SendHttpRequest job");
            Require(await SettledAsync(await RootOfAsync("orders", "orders-1", match)) is { Status: InstanceStatus.Finished },
                "the HTTP-started root commits Finished");

            // (b) A direct input whose response follows a job.
            var approval = (await foundation.SendAsync(scope, new Contract.Target.Start(Start("approval")), "approval-1", Contract.Kinds.Start,
                cancellationToken: stop)).Actor!.Value;
            var waiting = await RunningAsync(approval);
            var frame = Json.Read<NativeState>(waiting.State!).Frames.Values.Single(f => f.Node == "approve").Id;
            var approved = await RequestAsync(new Contract.Target.Ref(approval), "approve-1", CompositeSignal.Kind,
                Json.Write(new CompositeSignal(frame, "approve", "Ada")), RequestTimeout);
            Require(Status(approved) == 200 && await SettledAsync(approval) is { Status: InstanceStatus.Finished },
                "a held direct input is answered with the response written after the job it led to, and the root finishes");

            // (c) A job slower than the caller's deadline: the caller times out (504) while the root runs on and finishes.
            var slow = await RequestAsync(new Contract.Target.Start(Start("slow")), "slow-1", Contract.Kinds.Start, null, 1_000);
            Require(slow is { Error: "timeout", Status: Contract.ActorStatus.Running },
                $"a held start whose job outlasts the deadline ends with a timeout while its root runs ({Describe(slow)})");
            Require(await SettledAsync(await RootOfAsync("slow", "slow-1")) is { Status: InstanceStatus.Finished },
                "the slow root runs on after its caller timed out, and finishes");

            // (d) A job that faults its activity: the caller is answered with the Faulted end, and the root commits Faulted.
            var refused = await RequestAsync(new Contract.Target.Start(Start("refused")), "refused-1", Contract.Kinds.Start, null, RequestTimeout);
            Require(refused is { Error: null, Channel: null, Status: Contract.ActorStatus.Faulted },
                $"a held start whose job faults is answered with the Faulted end ({Describe(refused)})");
            Require(await SettledAsync(await RootOfAsync("refused", "refused-1")) is { Status: InstanceStatus.Faulted },
                "the faulted root commits Faulted");

            // (e) A plain fault at birth commits, and the node goes on with later roots.
            var faults = await RequestAsync(new Contract.Target.Start(Start("faults")), "faults-1", Contract.Kinds.Start, null, RequestTimeout);
            var faultedRoot = await RootOfAsync("faults", "faults-1");
            Require(faults is { Status: Contract.ActorStatus.Faulted } && await SettledAsync(faultedRoot) is { Status: InstanceStatus.Faulted },
                $"a workflow that faults in its first turn commits Faulted and its held caller gets that end ({Describe(faults)})");
            var view = await foundation.GetAsync(scope, faultedRoot, stop);
            Require(view is { Header.Status: Contract.ActorStatus.Faulted }, "the faulted root reads back Faulted");
            var later = (await foundation.SendAsync(scope, new Contract.Target.Start(Start("quick")), "quick-1", Contract.Kinds.Start,
                cancellationToken: stop)).Actor!.Value;
            Require(await SettledAsync(later) is { Status: InstanceStatus.Finished }, "the node goes on to finish a later root");

            Require(!escalated.Task.IsCompleted && service.LastError is null, "the node ran every case without a fault of its own");
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
        return 0;

        async Task<Contract.Reply> RequestAsync(Contract.Target target, string id, string kind, string? payload, long timeout) =>
            await foundation.RequestAsync(scope, target, id, kind, payload, null,
                new(null, await clock.ReadReplyTimeAsync(stop) + timeout), stop);

        // A start's repeat names the root its operation started.
        async Task<Contract.ActorRef> RootOfAsync(string workflow, string id, NativeHttpRouteMatch? route = null)
        {
            var spec = route is { Route: { } start }
                ? WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start(start.Workflow, triggerNode: start.Node,
                    trigger: Values.From(new HttpRequest("POST", "/orders", new JsonObject()) { RouteValues = route.Values })))
                : Start(workflow);
            return (await foundation.SendAsync(scope, new Contract.Target.Start(spec), id, Contract.Kinds.Start, cancellationToken: stop)) is
                { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } root } ? root : throw new InvalidOperationException($"'{id}' started no root.");
        }

        async Task<EfStore.RootImage?> SettledAsync(Contract.ActorRef root)
        {
            for (var attempt = 0; attempt < 500; attempt++)
            {
                if (await store.ReadFirstRootAsync(scope.Tenant, root.Id, stop) is { Status: not InstanceStatus.Running } done) return done;
                await Task.Delay(20, stop);
            }
            return await store.ReadFirstRootAsync(scope.Tenant, root.Id, stop);
        }

        async Task<EfStore.RootImage> RunningAsync(Contract.ActorRef root)
        {
            for (var attempt = 0; ; attempt++)
            {
                if (await store.ReadFirstRootAsync(scope.Tenant, root.Id, stop) is { Status: InstanceStatus.Running } running) return running;
                if (attempt == 250) throw new InvalidOperationException("The waiting root did not commit.");
                await Task.Delay(20, stop);
            }
        }
    }

    private static Contract.StartSpec Start(string workflow) => WorkflowActor.WorkflowActor.Start(NativeExecutionModel.Start(workflow));

    private static int? Status(Contract.Reply reply) =>
        reply is { Error: null, Channel: "response", Item: JsonNode item } ? Values.To<HttpResponse>(item)?.StatusCode : null;

    private static string Describe(Contract.Reply reply) => $"channel {reply.Channel}, status {reply.Status}, error {reply.Error}";

    // The partner the jobs call: /ok answers at once, /slow after three seconds.
    private sealed class Partner : HttpMessageHandler
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> calls = new(StringComparer.Ordinal);

        internal int Calls(string path) => calls.GetValueOrDefault(path);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            calls.AddOrUpdate(path, 1, (_, count) => count + 1);
            if (path == "/slow") await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
            return path is "/ok" or "/slow"
                ? new(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true}""", Encoding.UTF8, "application/json") }
                : new(HttpStatusCode.NotFound);
        }
    }

    private static void Require(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine($"ok {description}");
    }
}
