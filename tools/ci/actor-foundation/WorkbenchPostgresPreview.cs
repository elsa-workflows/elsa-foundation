using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native;
using Kernel.Native.Preview;
using Kernel.Native.Run;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using WorkflowActorType = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workbench.Qualification;

internal static class WorkbenchPostgresPreview
{
    private sealed record Receipt(string Actor, string Bookmark, string Frame, string StateHash, string JournalHash, long Epoch, int Process);
    private sealed record Checkpoint(long Revision, string State, long Epoch, string? Owner);

    internal static async Task RunAsync(string[] arguments)
    {
        if (arguments is not [var phase and ("birth" or "restart"), var receiptPath])
            throw new ArgumentException("Expected birth or restart and a receipt file.");

        PrintVersion(typeof(DbContext).Assembly);
        PrintVersion(typeof(NpgsqlDbContextOptionsBuilderExtensions).Assembly);
        PrintVersion(typeof(NpgsqlConnection).Assembly);
        using var time = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var token = time.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("A disposable PostgreSQL database is required.");
        var settings = new NpgsqlConnectionStringBuilder(connection);
        if (settings.Host != "127.0.0.1" || !settings.ShouldSerialize("Port") ||
            settings.Port == 5095 || string.IsNullOrEmpty(settings.Database) ||
            settings.SslMode != SslMode.Disable || settings.GssEncryptionMode != GssEncryptionMode.Disable)
            throw new ArgumentException("Use an explicit plaintext loopback PostgreSQL database and port for the wire meter.");

        var scope = EmbeddedTenant.Bind("workbench-preview");
        var observationOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(settings.ConnectionString).Options;
        KernelDb OpenObservation() => new(observationOptions);
        if (phase == "birth")
        {
            await using var db = OpenObservation();
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "birth uses an empty database");
            var setup = new EfStore(OpenObservation, postgres: true);
            await setup.CreateAsync();
            await setup.InstallPreviewRootSchemaAsync(token);
        }

        Receipt? previous = null;
        if (phase == "restart")
        {
            previous = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(receiptPath, token))
                ?? throw new InvalidOperationException("A birth receipt is required.");
            var checkpoint = await Read(new(previous.Actor));
            Require(checkpoint is { Revision: 2, Owner: null } && checkpoint.Epoch == previous.Epoch &&
                previous.Process != Environment.ProcessId && StateHash(checkpoint.State) == previous.StateHash,
                "the receipt matches the stopped waiting actor before owner startup");
        }

        var wire = Wire.Start("postgres", $"Host={settings.Host};Port={settings.Port}");
        settings.Port = wire.LocalPort;
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(settings.ConnectionString).Options;
        KernelDb Open() => new(options);
        var store = new EfStore(Open, postgres: true);
        var ingress = await store.OpenPreviewRootIngressAsync(WorkflowActorType.ActorType, TimeSpan.FromHours(1), token);
        var reads = await store.OpenPreviewRootReadsAsync(64, token);
        var definition = DirectApprovalWorkflow.Build();
        var model = new NativeExecutionModel(new WorkflowRegistry().Add(definition));
        var node = $"workbench-preview-{Environment.ProcessId}-{Guid.NewGuid():N}";
        var builder = Host.CreateEmptyApplicationBuilder(new());
        builder.Logging.ClearProviders();
        builder.Services.AddHostedService(_ => new PostgresPreviewHostedService(Open, node,
            new WorkflowActorType([model]), Environment.FailFast));
        using var host = builder.Build();
        await host.StartAsync(token);
        Receipt? receipt = null;
        Contract.ActorRef actor;
        Checkpoint last;
        try
        {
            if (previous is null)
            {
                var runtime = new WorkflowRuntime(ingress, reads);
                var start = await runtime.StartAsync(scope, "approval-start", NativeExecutionModel.Start(definition.Id), cancellationToken: token);
                Require(start is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "public start admitted the Native workflow");
                actor = start.Actor!.Value;
                await WaitFor(actor, 1);
                var initial = await reads.TailAsync(scope, actor, cancellationToken: token);
                var frame = initial.Items.Select(Entry).Single(entry => entry.StartsWith(DirectApprovalWorkflow.WaitEntry, StringComparison.Ordinal))
                    [DirectApprovalWorkflow.WaitEntry.Length..];
                Require(await Approve(actor, "approval-1", "Ada", frame) == Contract.AdmissionOutcome.Accepted, "first approval was admitted");
                var waiting = await WaitFor(actor, 2);
                Require(Native(waiting).Frames[frame].State!.AsArray().Select(item => item!.GetValue<string>()).SequenceEqual(["Ada"]),
                    "the checkpoint holds the first approval");
                var journal = await reads.TailAsync(scope, actor, cancellationToken: token);
                Require(journal.Next is not null && journal.Items.Count(entry => Entry(entry) == "approved:approval-1" && entry.Revision == 2) == 1,
                    "the public journal records the first approval once");
                receipt = new(actor.Id, journal.Next!.Value.Value, frame, StateHash(waiting.State), JournalHash(journal), waiting.Epoch, Environment.ProcessId);
                last = waiting;
            }
            else
            {
                actor = new(previous.Actor);
                var bookmark = new Contract.PageToken(previous.Bookmark);
                var waiting = await WaitFor(actor, 2);
                Require(waiting.Epoch > previous.Epoch && Environment.ProcessId != previous.Process && StateHash(waiting.State) == previous.StateHash,
                    "a different Workbench process owns the same waiting checkpoint at a later epoch");
                Require(await reads.GetAsync(scope, actor, token) is { Header.Status: Contract.ActorStatus.Running },
                    "the public read reports the waiting actor");
                Require(await Approve(actor, "approval-1", "changed", previous.Frame) == Contract.AdmissionOutcome.Duplicate,
                    "replaying the first approval returns Duplicate");
                var unchanged = await Read(actor);
                var history = await reads.TailAsync(scope, actor, cancellationToken: token);
                Require(unchanged == waiting && JournalHash(history) == previous.JournalHash &&
                    (await reads.TailAsync(scope, actor, bookmark, token)).Items.Count == 0,
                    "the replay leaves checkpoint bytes and the full journal unchanged");
                Require(await Approve(actor, "approval-2", "Grace", previous.Frame) == Contract.AdmissionOutcome.Accepted,
                    "a new approval was admitted");
                var completion = await WaitFor(actor, 3);
                Require(await reads.GetAsync(scope, actor, token) is { Header.Status: Contract.ActorStatus.Finished } &&
                    Native(completion).Results["approve"]!.AsArray().Select(item => item!.GetValue<string>()).SequenceEqual(["Ada", "Grace"]),
                    "the persisted result contains both approvals exactly once");
                var suffix = await reads.TailAsync(scope, actor, bookmark, token);
                Require(suffix.Items.Count(entry => Entry(entry) == "approved:approval-2") == 1 && suffix.Items.All(entry => entry.Revision == 3),
                    "the saved bookmark returns the one resumed approval at revision three");
                last = completion;
            }
        }
        finally
        {
            using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await host.StopAsync(shutdown.Token);
        }

        Require(await Read(actor) == (last with { Owner = null }),
            "StopAsync clears the partition owner at the same epoch before host disposal");
        if (receipt is not null)
            await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt), token);
        Require(wire.Trips > 0 && wire.Connections > 0 && wire.ProtocolRequests > 0, "the meter observed the preview provider traffic");
        Console.WriteLine($"wire {phase} {wire.Trips} round trips, {wire.ProtocolRequests} protocol groups, {wire.Connections} connections");
        Console.WriteLine($"ok Workbench process {Environment.ProcessId} {phase} stopped");

        async Task<Contract.AdmissionOutcome> Approve(Contract.ActorRef actor, string id, string person, string frame) =>
            (await ingress.SendAsync(scope, new Contract.Target.Ref(actor), id, CompositeSignal.Kind,
                JsonSerializer.Serialize(new CompositeSignal(frame, "approve", person)), cancellationToken: token)).Outcome;

        async Task<Checkpoint?> Read(Contract.ActorRef actor)
        {
            await using var db = OpenObservation();
            return await db.Database.SqlQuery<Checkpoint>($"""
                SELECT i."Revision", i."State", p."Epoch", p."Owner" FROM "Instances" i
                JOIN "Partitions" p ON p."Id" = i."Partition"
                WHERE i."FoundationTenant" = {scope.Tenant} AND i."FoundationReference" = {actor.Id}
                """).SingleOrDefaultAsync(token);
        }

        async Task<Checkpoint> WaitFor(Contract.ActorRef actor, long revision)
        {
            while (true)
            {
                var checkpoint = await Read(actor);
                if (checkpoint?.Revision > revision)
                    throw new InvalidOperationException($"Unexpected checkpoint revision {checkpoint.Revision}.");
                if (checkpoint is not null && checkpoint.Revision == revision && checkpoint.Owner == node)
                    return checkpoint;
                await Task.Delay(40, token);
            }
        }
    }

    private static void PrintVersion(Assembly assembly) =>
        Console.WriteLine($"provider {assembly.GetName().Name} {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]}");

    private static string Entry(Contract.JournalEntry entry)
    {
        using var parsed = JsonDocument.Parse(entry.Entry);
        return parsed.RootElement.ValueKind == JsonValueKind.String ? parsed.RootElement.GetString()! : "";
    }

    private static NativeState Native(Checkpoint checkpoint)
    {
        const string prefix = "!kernel-state/4:public-root:";
        Require(checkpoint.State.StartsWith(prefix, StringComparison.Ordinal), "the state uses the ordinary preview framing");
        return JsonSerializer.Deserialize<NativeState>(checkpoint.State[prefix.Length..])!;
    }

    private static string JournalHash(Contract.Page<Contract.JournalEntry> page) =>
        StateHash(JsonSerializer.Serialize(page.Items));

    private static string StateHash(string state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
