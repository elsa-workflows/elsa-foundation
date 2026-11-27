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
using Contract = Kernel.Foundation.Contracts;
using NativeHttpRequest = Kernel.Native.HttpRequest;
using NativeHttpResponse = Kernel.Native.HttpResponse;
using LiveWorkflowActor = Kernel.WorkflowActor.WorkflowActor;

namespace Kernel.Native.Run;

internal static class PostgresLiveRemoteHttpChecks
{
    internal static async Task<int> RunAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stop = timeout.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh PostgreSQL database.");
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        KernelDb Open() => KernelDb.ForPostgresRootShipped(options);
        await using var db = Open();
        Check(await PostgresFixtureDatabase.IsEmptyAsync(db, stop), "the PostgreSQL database is empty");
        var store = new EfStore(Open, postgres: true);
        await store.CreateAsync(1);
        await store.InstallPreviewRootSchemaAsync(stop);

        const string owner = "a-workflow-owner";
        const string caller = "z-http-caller";
        var scope = ContractScopes.Bind("postgres-live-replies");
        var body = new string('π', 96 * 1024);
        var registry = new WorkflowRegistry()
            .Add(Wf.Workflow("reply", Wf.Sequence("main", Wf.HttpEndpoint("open", "reply"), Wf.Respond("answer", body, 202))))
            .Add(Wf.Workflow("oversized", Wf.Sequence("main", Wf.HttpEndpoint("open", "oversized"), Wf.Node<OverWireBudget>("answer"))))
            .Add(Wf.Workflow("end-only", Wf.HttpEndpoint("open", "end-only")));
        var model = new LiveWorkflowActor([new NativeExecutionModel(registry)]);
        var loops = new LoopOptions { BalanceEvery = TimeSpan.FromMilliseconds(125), PollInterval = TimeSpan.FromMilliseconds(25) };
        var edge = PostgresPreviewHosting.CreateRequests(Open, caller, model.Type);
        var ownerEdge = PostgresPreviewHosting.CreateRequests(Open, owner, model.Type);
        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, model.Type, edge, cancellationToken: stop);
        void Escalate(string message) => throw new InvalidOperationException(message);
        var refusedWire = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = await PostgresPreviewHosting.StartAsync(Open, owner, model, Escalate, loops, stop,
            onFault: error =>
            {
                if (error is InvalidDataException) refusedWire.TrySetResult();
            }, requests: ownerEdge);
        while (await db.Partitions.AsNoTracking().Where(row => row.Id == 0).Select(row => row.Owner).SingleAsync(stop) != owner)
            await Task.Delay(25, stop);
        await using var second = await PostgresPreviewHosting.StartAsync(Open, caller, model, Escalate, loops, stop, requests: edge);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var application = builder.Build();
        application.MapPost("/{workflow}", async (HttpContext context, string workflow) =>
        {
            var token = context.RequestAborted;
            var start = Start(workflow);
            var reply = await foundation.RequestAsync(scope, new Contract.Target.Start(start), workflow + "-id", Contract.Kinds.Start,
                null, null, new(null, await edge.ReadClockAsync(token) + (workflow == "oversized" ? 2_000 : 10_000)), token);
            var answer = KernelHttpEdge.Answer(reply);
            foreach (var field in answer.Headers) context.Response.Headers[field.Key] = field.Value;
            return Results.Text(answer.Body, answer.ContentType, statusCode: answer.StatusCode);
        });
        await application.StartAsync(stop);
        using var client = new HttpClient { BaseAddress = new Uri(application.Urls.Single()) };
        using var delivered = await client.PostAsync("/reply", new StringContent(""), stop);
        Check(delivered.StatusCode == HttpStatusCode.Accepted && await delivered.Content.ReadAsStringAsync(stop) == body,
            "the live HTTP response reconstructs all notification frames in order");
        var run = new Contract.ActorRef(delivered.Headers.GetValues("X-Elsa-Workflow-Instance-Id").Single());
        var actor = await foundation.GetAsync(scope, run, stop);
        var checkpoint = await store.ReadFirstRootAsync(scope.Tenant, run.Id, stop);
        if (checkpoint is not { Revision: 1, TerminalRevision: 1, Status: InstanceStatus.Finished })
            throw new InvalidOperationException("The remote response must have one committed terminal checkpoint.");
        Check(actor is { Header.Status: Contract.ActorStatus.Finished } &&
            await db.Partitions.AsNoTracking().Where(row => row.Id == 0).Select(row => row.Owner).SingleAsync(stop) == owner &&
            !await db.Messages.AsNoTracking().AnyAsync(row => row.Target == checkpoint.Actor.Id && row.ReplyTo != null, stop),
            "the remote owner committed once and cleared the live route before delivery");

        using var oversized = await client.PostAsync("/oversized", new StringContent(""), stop);
        await refusedWire.Task.WaitAsync(stop);
        Check(oversized.StatusCode == HttpStatusCode.GatewayTimeout &&
            await oversized.Content.ReadAsStringAsync(stop) == "the workflow did not answer in time",
            "an authored reply over the real carrier's byte limit leaves the caller to time out");
        var original = await foundation.SendAsync(scope, new Contract.Target.Start(Start("oversized")), "oversized-id",
            Contract.Kinds.Start, cancellationToken: stop);
        Check(original is { Outcome: Contract.AdmissionOutcome.Duplicate, Actor: { } oversizedActor } &&
            await store.ReadFirstRootAsync(scope.Tenant, oversizedActor.Id, stop) is { Status: InstanceStatus.Finished, TerminalRevision: 1 },
            "wire failure leaves the authored terminal commit intact");
        using var endOnly = await client.PostAsync("/end-only", new StringContent(""), stop);
        Check(endOnly.StatusCode == HttpStatusCode.NoContent && endOnly.Headers.Contains(KernelHttpEdge.WorkflowInstanceIdHeader),
            "an actor that ends without emitting still answers with its committed terminal status");
        await application.StopAsync(stop);
        return 0;

        static Contract.StartSpec Start(string workflow) => LiveWorkflowActor.Start(NativeExecutionModel.Start(workflow, triggerNode: "open",
            trigger: Values.From(new NativeHttpRequest("POST", "/" + workflow, new JsonObject()))));
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
