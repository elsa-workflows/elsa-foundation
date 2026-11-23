using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Native;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresRootTimedJobIssuance
{
    private const string Command = "pg-root-timed-job-issuance";
    private const string DatabaseVariable = "KERNEL_PREVIEW_POSTGRES";
    private const string FixtureVariable = "KERNEL_TIMED_JOB_ISSUANCE_FIXTURE";
    private const string Tenant = "timed-jobs";
    private const string Definition = "timed-job-issuance";
    private const string BackgroundNode = "background";
    private const string FirstOwner = "issuance";
    private static readonly TimeSpan JobTimeout = TimeSpan.FromSeconds(10);

    internal static async Task<int> RunAsync(string fence, string[] phase)
    {
        var connection = Environment.GetEnvironmentVariable(DatabaseVariable)
            ?? throw new InvalidOperationException("Supply a disposable PostgreSQL fixture server.");
        using var limit = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var stop = limit.Token;
        if (phase.Length == 0) return await CheckAsync(connection, fence, stop);

        var database = new NpgsqlConnectionStringBuilder(connection).Database;
        Check(database is not null && database.StartsWith("kernel_timed_fixture_", StringComparison.Ordinal) &&
            Environment.GetEnvironmentVariable(FixtureVariable) == database, "Only the disposable fixture may enter a child phase.");
        var cut = new CommitCut();
        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).AddInterceptors(cut).Options;
        KernelDb Open() => KernelDb.ForPostgresRootTimedJobIssuance(options);
        var store = new EfStore(Open, postgres: true, fenceInBatch: fence == "in-batch") { CommitSql = CommitSql.Owned };
        var roots = new PostgresRootAdmission(Open, TimeSpan.FromMinutes(5));
        if (phase is ["issue"])
        {
            await using var empty = Open();
            Check(await PostgresFixtureDatabase.IsEmptyAsync(empty, stop), "Issuance requires the fresh fixture database.");
            await store.CreateAsync(1);
            await roots.InstallAsync(stop);
            var api = new PostgresPreviewFoundation(roots, store, new(store, await roots.OpenJournalCursorAsync(stop), 16),
                WorkflowActor.WorkflowActor.ActorType);
            var scope = FixedTenantScope.Bind(Tenant);
            var receipt = await new WorkflowRuntime(api, api).StartAsync(scope, "issue", NativeExecutionModel.Start(Definition), cancellationToken: stop);
            var actor = receipt is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } reference }
                ? reference : throw new InvalidOperationException("The public Native workflow was not admitted.");
            await store.EnableRootJobsAsync(stop);
            var registry = new WorkflowRegistry().Add(Wf.Workflow(Definition, new Node(BackgroundNode, typeof(MustNotExecute)) { JobTimeout = JobTimeout }));
            var owner = await PostgresNativeRootOwner.PrepareAsync(store, FirstOwner, Tenant, actor.Id, registry, stop,
                (_, _, now) => cut.Receipt = FormattableString.Invariant($"issued {Environment.ProcessId} {actor.Id} {now}"));
            await using var activation = await owner.TryActivateAsync(stop) ?? throw new InvalidOperationException("The first owner could not take the partition.");
            while (!stop.IsCancellationRequested)
            {
                await activation.WokenAsync(null, stop);
                await activation.RunAsync(stop);
                await Task.Delay(10, stop);
            }
            throw new InvalidOperationException("The issuing commit did not reach the cut.");
        }

        if (phase is not ["reload", var birthPid, var actorId, var issued] ||
            !int.TryParse(birthPid, out var firstPid) || !long.TryParse(issued, CultureInfo.InvariantCulture, out var at)) return 2;
        Check(Environment.ProcessId != firstPid, "The reload must run in another process.");
        await store.EnableRootJobsAsync(stop);
        var root = await store.BindWorkerRootAsync(Tenant, actorId, stop);
        await using var db = Open();
        var previous = await db.Partitions.SingleAsync(p => p.Id == root.Partition, stop);
        var older = new EfStore(() => KernelDb.ForPostgresRootJobs(options), postgres: true);
        try
        {
            await older.TakeAsync(root.Partition, "protocol-6", Defaults.Lease, stop);
            throw new InvalidOperationException("A protocol-6 owner accepted the issuance-only format.");
        }
        catch (StoreFormatException) { }
        await db.Database.ExecuteSqlInterpolatedAsync($"""UPDATE "Nodes" SET "LeaseUntil" = 0 WHERE "Id" = {FirstOwner} """, stop);
        var taken = await store.TakeAsync(root.Partition, "inspection", Defaults.Lease, stop);
        Check(previous.Owner == FirstOwner && previous.Position == 1 && taken is { Position: 1 } && taken.Epoch == previous.Epoch + 1,
            "The cold owner must take the same journal position at a higher epoch.");
        var image = (await store.LoadAsync([root.Actor], stop)).Single();
        var checkpoint = Json.Read<NativeState>(image.State);
        var (waitAddress, wait) = checkpoint.Waits.Single();
        var pair = await store.ReadRootTimedJobIssuanceAsync(root, stop);
        var ticket = pair.Ticket.Read<JobTicket>();
        using var wrapped = JsonDocument.Parse(ticket.Arguments!);
        Check(image is { Model: WorkflowActor.WorkflowActor.ActorType, FoundationDispatchHeader: { Tenant: Tenant } binding } &&
            image.Id == root.Actor && binding.Reference == actorId,
            "The cold checkpoint must load the exact bound workflow actor and public reference.");
        using var dispatch = JsonDocument.Parse(image.FoundationDispatchHeader!.Header.TypeData);
        var workflow = dispatch.RootElement.GetProperty("Definition").GetString();
        var nativeModel = dispatch.RootElement.GetProperty("Model").GetString();
        Check(workflow == Definition && nativeModel == NativeExecutionModel.ModelKey,
            "The persisted dispatch header must name the exact Native model and definition.");
        Check(ticket.Model == image.Model, "The kernel ticket must name the persisted workflow actor type.");
        Check(wrapped.RootElement.GetProperty("Model").GetString() == nativeModel,
            "The workflow job arguments must name the persisted Native model.");
        Check(waitAddress == ticket.Key, "The Native wait dictionary must address the exact issued job key.");
        Check(wait.Key == ticket.Key, "The Native wait must carry the exact issued job key.");
        var arguments = wrapped.RootElement.GetProperty("Arguments").GetString();
        var job = arguments is null ? null : JsonSerializer.Deserialize<BackgroundArgs>(arguments);
        Check(job?.Workflow == workflow, "The Native job arguments must name the checkpoint's workflow definition.");
        Check(job?.Frame == wait.Frame, "The Native job arguments must name the frame that owns the saved wait.");
        Check(checkpoint.Frames.TryGetValue(wait.Frame, out var frame) && frame.Id == wait.Frame &&
            frame.Node == BackgroundNode && job?.Node == frame.Node,
            "The Native job arguments must name the background node of the saved frame.");
        var deadline = at + (long)JobTimeout.TotalMilliseconds;
        Check(image is { Creation: 1, Revision: 1, Status: InstanceStatus.Running, NextJobGeneration: 1 } &&
            wait.Kind == "job" && wait.Job is { } saved && saved.Key == ticket.Key && saved.Arguments == arguments && saved.Deadline == deadline &&
            JobRegistry.Read(image.Jobs).SequenceEqual([new(ticket.Key, 0)]) &&
            pair.Ticket.JobGeneration == 0 && ticket.Deadline == deadline && ticket.Timer == pair.Deadline.Id &&
            pair.Deadline.Due == deadline && pair.Deadline.Read<JobDeadline>().Key == ticket.Key,
            $"The exact Native wait, registered generation, ticket, and private deadline must survive the process loss. Deadline expected {deadline}, Native {wait.Job?.Deadline}, ticket {ticket.Deadline}, timer {pair.Deadline.Due}.");
        var atomic = await db.Database.SqlQuery<bool>($"""
            SELECT count(*) = 2 AND bool_and(m.xmin = i.xmin AND i.xmin = j.xmin AND m."Attempts" = 0 AND m."Trial" = 0) AS "Value"
            FROM "Messages" m JOIN "Instances" i ON m."Target" = i."Id" AND m."Partition" = i."Partition"
            JOIN "Journal" j ON j."Instance" = i."Id" AND j."Partition" = i."Partition" AND j."Revision" = i."Revision"
            WHERE i."Id" = {root.Actor} AND m."InboxScope" = {EfStore.RootTimedJobScope} AND NOT m."Done" AND i."Revision" = 1
            """).SingleAsync(stop);
        Check(atomic && (await store.ReadJournalOfAsync(root.Partition, root.Actor, 0, stop)).Count == 1,
            "The checkpoint, job ticket, deadline and only journal turn must share the issuing transaction.");
        Check(pair.Now < deadline, "The cold owner must observe the deadline before it is due.");
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Min(100, deadline - pair.Now)), stop);
            pair = await store.ReadRootTimedJobIssuanceAsync(root, stop);
        } while (pair.Now < deadline);
        var due = await store.PollAsync(root.Partition, taken!.Epoch, TimeSpan.Zero, 10, stop);
        Check(due.Messages.Count == 0 && pair.Deadline.Due == deadline, "A cold store poll must exclude both issued rows at the deadline.");
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "Messages" SET "Due" = "Due" + 1 WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition}
                AND "InboxScope" = {EfStore.RootTimedJobScope} AND "Kind" = {Kinds.JobDeadline}
            """, stop);
        try
        {
            await store.ReadRootTimedJobIssuanceAsync(root, stop);
            throw new InvalidOperationException("The cold owner accepted a deadline changed independently of the original ticket.");
        }
        catch (NotSupportedException) { }
        Console.WriteLine($"ok Native timed job issuance {fence}, processes {firstPid}->{Environment.ProcessId}, epochs {previous.Epoch}->{taken.Epoch}, revision 1");
        return 0;
    }

    private static async Task<int> CheckAsync(string server, string fence, CancellationToken stop)
    {
        var database = "kernel_timed_fixture_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(server);
        await admin.OpenAsync(stop);
        await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
        await create.ExecuteNonQueryAsync(stop);
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(server) { Database = database, Pooling = false }.ConnectionString;
            var issuance = await Child(["issue"]);
            var segments = issuance.Trim().Split(' ');
            Check(segments is ["issued", _, _, _], "The first process must exit directly after committing issuance.");
            Console.Write(await Child(["reload", .. segments[1..]]));
            return 0;

            async Task<string> Child(string[] phase)
            {
                var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, UseShellExecute = false };
                foreach (var argument in new[] { typeof(PostgresRootTimedJobIssuance).Assembly.Location, Command, fence }.Concat(phase))
                    start.ArgumentList.Add(argument);
                start.Environment[DatabaseVariable] = connection;
                start.Environment[FixtureVariable] = database;
                using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the fixture owner.");
                try
                {
                    var output = await child.StandardOutput.ReadToEndAsync(stop);
                    await child.WaitForExitAsync(stop);
                    Check(child.ExitCode == 0, $"Fixture owner exited with code {child.ExitCode}. {output}");
                    return output;
                }
                finally
                {
                    if (!child.HasExited)
                    {
                        child.Kill();
                        await child.WaitForExitAsync(CancellationToken.None);
                    }
                }
            }
        }
        finally
        {
            await using var drop = new NpgsqlCommand($"DROP DATABASE {database}", admin);
            await drop.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private sealed class CommitCut : DbTransactionInterceptor
    {
        internal string? Receipt { get; set; }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Receipt is { } receipt)
            {
                Console.WriteLine(receipt);
                Console.Out.Flush();
                Environment.Exit(0);
            }
            return Task.CompletedTask;
        }
    }

    [Background]
    public sealed class MustNotExecute : Activity<string>
    {
        protected override ValueTask<Transition> ExecuteAsync(ActivityContext context) =>
            throw new InvalidOperationException("The issuance-only fixture cannot dispatch the job.");
    }

    private static void Check(bool ok, string failure)
    {
        if (!ok) throw new InvalidOperationException(failure);
    }
}
