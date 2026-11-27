using System.Net;
using System.Text.Json.Nodes;
using Elsa.ActorFoundation.TestSupport;
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
        var registry = new WorkflowRegistry().Add(Wf.Workflow("reply", Wf.Sequence("main",
            Wf.HttpEndpoint("open", "reply"), Wf.Respond("answer", body, 202))));
        var model = new LiveWorkflowActor([new NativeExecutionModel(registry)]);
        var loops = new LoopOptions { BalanceEvery = TimeSpan.FromMilliseconds(125), PollInterval = TimeSpan.FromMilliseconds(25) };
        var edge = PostgresPreviewHosting.CreateRequests(Open, caller, model.Type);
        var ownerEdge = PostgresPreviewHosting.CreateRequests(Open, owner, model.Type);
        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, model.Type, edge, cancellationToken: stop);
        void Escalate(string message) => throw new InvalidOperationException(message);
        await using var first = await PostgresPreviewHosting.StartAsync(Open, owner, model, Escalate, loops, stop, requests: ownerEdge);
        while (await db.Partitions.AsNoTracking().Where(row => row.Id == 0).Select(row => row.Owner).SingleAsync(stop) != owner)
            await Task.Delay(25, stop);
        await using var second = await PostgresPreviewHosting.StartAsync(Open, caller, model, Escalate, loops, stop, requests: edge);

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Configuration.Sources.Clear();
        builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
        await using var application = builder.Build();
        application.MapPost("/reply", async (HttpContext context) =>
        {
            var token = context.RequestAborted;
            var start = LiveWorkflowActor.Start(NativeExecutionModel.Start("reply", triggerNode: "open",
                trigger: Values.From(new NativeHttpRequest("POST", "/reply", new JsonObject()))));
            var reply = await foundation.RequestAsync(scope, new Contract.Target.Start(start), "reply-id", Contract.Kinds.Start,
                null, null, new(null, await edge.ReadClockAsync(token) + 10_000), token);
            if (reply is not { Actor: { } actor, Channel: "response", Item: JsonNode item, Error: null } ||
                Values.To<NativeHttpResponse>(item) is not { Body: { } responseBody } response)
                return Results.Text(reply.Error ?? "invalid live reply", statusCode: 504);
            context.Response.Headers["X-Elsa-Workflow-Instance-Id"] = actor.Id;
            return Results.Text(responseBody.GetValue<string>(), statusCode: response.StatusCode);
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
        await application.StopAsync(stop);
        return 0;
    }

    private static void Check(bool condition, string description)
    {
        if (!condition) throw new InvalidOperationException(description);
        Console.WriteLine("ok " + description);
    }
}
