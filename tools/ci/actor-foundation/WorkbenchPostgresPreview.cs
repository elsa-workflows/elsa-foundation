using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.ActorFoundation.Hosting;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native;
using Kernel.Native.Preview;
using Kernel.Native.Run;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;
using WorkflowActorType = Kernel.WorkflowActor.WorkflowActor;

namespace Elsa.Workbench.Qualification;

internal static class WorkbenchPostgresPreview
{
    private const int JobTicketScope = -6;
    private const int TimedJobScope = -7;

    private sealed record Receipt(string Actor, string Bookmark, string Frame, string StateHash, string JournalHash, long Epoch, int Process);
    private sealed record Checkpoint(long Revision, string State, long Epoch, string? Owner);
    private sealed record ActiveJob(long Epoch, string Owner, string State, string TicketId, string DeadlineId,
        long Deadline, long Now);

    internal static async Task RunAsync(string[] arguments, string? executable = null)
    {
        if (arguments is not [var phase and ("birth" or "restart" or "disabled"), var receiptPath, .. var profileArguments] ||
            profileArguments is not ([] or ["jobs" or "timed" or "timeout" or "takeover"]))
            throw new ArgumentException("Expected birth, restart or disabled, a receipt file and optional jobs, timed, timeout or takeover profile.");
        var profile = profileArguments.SingleOrDefault();
        var takeover = profile == "takeover";
        var timed = profile is "timed" or "timeout" or "takeover";
        var timesOut = profile is "timeout" or "takeover";
        var nativeJobs = profile is not null;
        var ticketScope = timed ? TimedJobScope : JobTicketScope;

        if ((phase == "disabled" || takeover) && executable is null)
            throw new ArgumentException("The disabled and takeover checks require a Workbench executable.");

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
        KernelDb OpenObservation() => OpenProfile(observationOptions, profile);
        var wire = Wire.Start("postgres", $"Host={settings.Host};Port={settings.Port}");
        settings.Port = wire.LocalPort;
        if (phase == "disabled")
        {
            using var disabled = WorkbenchProcess.Start(executable!, settings.ConnectionString, enabled: false, profile);
            try
            {
                await disabled.WaitReadyAsync(token);
            }
            finally
            {
                await disabled.StopAsync();
            }
            Require(wire.Connections == 0 && wire.Trips == 0, "disabled Workbench never opened the preview database");
            return;
        }

        if (phase == "birth")
        {
            await using var db = OpenObservation();
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "birth uses an empty database");
            var setup = new EfStore(OpenObservation, postgres: true);
            await setup.CreateAsync();
            await setup.InstallPreviewRootSchemaAsync(token);
            if (nativeJobs) await InstallJobWitnessAsync(db, ticketScope, token);
        }

        Receipt? previous = null;
        if (phase == "restart")
        {
            previous = JsonSerializer.Deserialize<Receipt>(await File.ReadAllTextAsync(receiptPath, token))
                ?? throw new InvalidOperationException("A birth receipt is required.");
            var checkpoint = await Read(new(previous.Actor));
            Require(checkpoint is { Revision: 2, Owner: null } && checkpoint.Epoch == previous.Epoch &&
                StateHash(checkpoint.State) == previous.StateHash,
                "the receipt matches the stopped waiting actor before owner startup");
            var observation = new EfStore(OpenObservation, postgres: true);
            var observer = await observation.OpenPreviewRootReadsAsync(64, token);
            var stoppedActor = new Contract.ActorRef(previous.Actor);
            var prefix = await observer.TailAsync(scope, stoppedActor, cancellationToken: token);
            Require(JournalHash(prefix) == previous.JournalHash && WaitFrame(prefix) == previous.Frame,
                "the receipt matches the frame and journal before owner startup");
            var suffix = await observer.TailAsync(scope, stoppedActor, new Contract.PageToken(previous.Bookmark), token);
            Require(suffix.Items.Count == 0, "the receipt bookmark is caught up before owner startup");
        }

        var options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(settings.ConnectionString).Options;
        KernelDb Open() => OpenProfile(options, profile);
        var store = new EfStore(Open, postgres: true);
        var ingress = await store.OpenPreviewRootIngressAsync(WorkflowActorType.ActorType, TimeSpan.FromHours(1), token);
        var reads = await store.OpenPreviewRootReadsAsync(64, token);
        var definition = takeover ? DirectApprovalWorkflow.BuildWithTimedTakeover() :
            timed ? DirectApprovalWorkflow.BuildWithTimedJob(timesOut) :
            nativeJobs ? DirectApprovalWorkflow.BuildWithJob() : DirectApprovalWorkflow.Build();
        var registry = new WorkflowRegistry().Add(definition);
        if (timed) registry.Add(DirectApprovalWorkflow.Build());
        var model = new NativeExecutionModel(registry);
        var workflowActor = new WorkflowActorType([model]);
        if (phase == "birth")
        {
            try
            {
                var wrong = await PostgresPreviewHosting.StartAsync(() => OpenProfile(observationOptions, profile is null ? "jobs" : timed ? "jobs" : null),
                    "wrong-preview-format", workflowActor, Environment.FailFast, cancellationToken: token);
                await wrong.StopAsync();
                throw new InvalidOperationException("The wrong preview profile started on this database.");
            }
            catch (StoreFormatException)
            {
                await using var observation = OpenObservation();
                Require(!await observation.Nodes.AnyAsync(node => node.Id == "wrong-preview-format", token),
                    "preview startup refused the other profile before registering a node");
            }
        }
        var node = $"workbench-preview-{Environment.ProcessId}-{Guid.NewGuid():N}";
        using var host = executable is null ? CreateFixtureHost() : null;
        var workbench = executable is null ? null : WorkbenchProcess.Start(executable, settings.ConnectionString, enabled: true, profile);
        var ownerPid = workbench?.Id ?? Environment.ProcessId;
        var nodePrefix = workbench is null ? node : $"workbench-core-preview-{ownerPid}-";
        Receipt? receipt = null;
        Contract.ActorRef actor;
        Checkpoint last;
        try
        {
            if (host is not null)
                await host.StartAsync(token);
            if (workbench is not null)
                await workbench.WaitReadyAsync(token);

            if (previous is null)
            {
                var runtime = new WorkflowRuntime(ingress, reads);
                var start = await runtime.StartAsync(scope, "approval-start", NativeExecutionModel.Start(definition.Id), cancellationToken: token);
                Require(start is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "public start admitted the Native workflow");
                actor = start.Actor!.Value;
                await WaitFor(actor, 1);
                var initial = await reads.TailAsync(scope, actor, cancellationToken: token);
                var frame = WaitFrame(initial);
                Require(await Approve(actor, "approval-1", "Ada", frame) == Contract.AdmissionOutcome.Accepted, "first approval was admitted");
                var waiting = await WaitFor(actor, 2);
                Require(Native(waiting, timed).Frames[frame].State!.AsArray().Select(item => item!.GetValue<string>()).SequenceEqual(["Ada"]),
                    "the checkpoint holds the first approval");
                var journal = await reads.TailAsync(scope, actor, cancellationToken: token);
                Require(journal.Next is not null && journal.Items.Count(entry => Entry(entry) == "approved:approval-1" && entry.Revision == 2) == 1,
                    "the public journal records the first approval once");
                if (timed)
                    await CheckJobFreeNeighbor(runtime);
                receipt = new(actor.Id, journal.Next!.Value.Value, frame, StateHash(waiting.State), JournalHash(journal), waiting.Epoch, ownerPid);
                last = waiting;
            }
            else
            {
                actor = new(previous.Actor);
                var bookmark = new Contract.PageToken(previous.Bookmark);
                var waiting = await WaitFor(actor, 2);
                Require(waiting.Epoch > previous.Epoch && ownerPid != previous.Process && StateHash(waiting.State) == previous.StateHash,
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
                ActiveJob? cut = null;
                if (takeover)
                {
                    // This profile requires the real Workbench executable. The old process is the only owner until killed.
                    var former = workbench!;
                    workbench = null;
                    using (former)
                    {
                        Require(await former.WaitForJobEntryAsync(token) == actor.Id,
                            "the original Workbench entered the selected actor's timed job");
                        cut = await ReadActiveJob(actor) ?? throw new InvalidOperationException("The entered job has no preserved ticket and deadline.");
                        Require(cut.Owner == waiting.Owner && cut.Epoch == waiting.Epoch && cut.Deadline > cut.Now,
                            "revision three contains the active job on the original epoch with time left");
                        await former.CrashAsync(token);
                        var interrupted = await ReadActiveJob(actor);
                        Require(interrupted is not null && (interrupted with { Now = cut.Now }) == cut && interrupted.Now < cut.Deadline,
                            "abrupt exit leaves the original fence, checkpoint, ticket and deadline intact before timeout");
                    }

                    workbench = WorkbenchProcess.Start(executable!, settings.ConnectionString, enabled: true, profile);
                    Require(workbench.Id != ownerPid, "a different Workbench process starts after the owner exits");
                    ownerPid = workbench.Id;
                    nodePrefix = $"workbench-core-preview-{ownerPid}-";
                    await workbench.WaitReadyAsync(token);
                }
                var chained = timed && !timesOut;
                var finalRevision = chained ? 7L : timed ? 5L : nativeJobs ? 4L : 3L;
                var completion = await WaitFor(actor, finalRevision);
                if (cut is not null)
                    Require(completion.Epoch > cut.Epoch, "the replacement Workbench applied the outcome at a higher epoch");
                var native = Native(completion, timed);
                Require(await reads.GetAsync(scope, actor, token) is { Header.Status: Contract.ActorStatus.Finished } &&
                    native.Results["approve"]!.AsArray().Select(item => item!.GetValue<string>()).SequenceEqual(["Ada", "Grace"]),
                    "the persisted result contains both approvals exactly once");
                var suffix = await reads.TailAsync(scope, actor, bookmark, token);
                Require(suffix.Items.Where(entry => Entry(entry) == "approved:approval-2").Select(entry => entry.Revision).SequenceEqual([3L]) &&
                    suffix.Items.All(entry => entry.Revision >= 3 && entry.Revision <= finalRevision),
                    "the saved bookmark returns the one resumed approval at revision three");
                if (nativeJobs)
                {
                    var resultNode = timesOut ? DirectApprovalWorkflow.TimeoutBoundary : DirectApprovalWorkflow.JobNode;
                    Require(native.Results[resultNode]?.GetValue<string>() == (timesOut ? "timeout" : DirectApprovalWorkflow.JobResult),
                        "the restarted Workbench persisted the expected job outcome");
                    var firstResultRevision = timed ? 5L : 4L;
                    Require(OutcomeRevisions(DirectApprovalWorkflow.JobNode, timesOut).SequenceEqual([firstResultRevision]) &&
                        suffix.Items.Where(entry => Entry(entry) == "end").Select(entry => entry.Revision).SequenceEqual([finalRevision]),
                        "the journal records the first job outcome and one end at the expected revisions");
                    if (chained)
                        Require(native.Results[DirectApprovalWorkflow.SuccessorJobNode]?.GetValue<string>() == DirectApprovalWorkflow.JobResult &&
                            OutcomeRevisions(DirectApprovalWorkflow.SuccessorJobNode, faulted: false).SequenceEqual([finalRevision]),
                            "the successor job result advances the same workflow once");

                    IEnumerable<long> OutcomeRevisions(string node, bool faulted) => suffix.Items
                        .Where(entry => Entry(entry).StartsWith(faulted ? "f:" : "c:", StringComparison.Ordinal) &&
                            Entry(entry).EndsWith(":" + node, StringComparison.Ordinal)).Select(entry => entry.Revision);
                    await using var observation = OpenObservation();
                    var jobCount = chained ? 2 : 1;
                    var settled = await observation.Database.SqlQuery<bool>($"""
                        SELECT i."Jobs" IS NULL AND i."NextJobGeneration" = {jobCount} AND NOT EXISTS (
                            SELECT 1 FROM "Messages" m WHERE m."Partition" = i."Partition" AND m."Target" = i."Id"
                                AND (m."InboxScope" = {ticketScope} OR NOT m."Done")
                        ) AS "Value" FROM "Instances" i
                        WHERE i."FoundationTenant" = {scope.Tenant} AND i."FoundationReference" = {actor.Id}
                        """).SingleAsync(token);
                    Require(settled, "settlement clears the job registry and physically removes the job ticket");
                    await CheckIssuance(generation: 0, revision: 3);
                    if (chained) await CheckIssuance(generation: 1, revision: 5);

                    async Task CheckIssuance(long generation, long revision)
                    {
                        var issuance = await observation.Database.SqlQuery<bool>($"""
                            SELECT count(*) = {(timed ? 2 : 1)} AND bool_and(w."Generation" = {generation})
                                AND count(*) FILTER (WHERE w."Kind" = 'kernel.job' AND w."Due" = 0) = 1
                                AND count(*) FILTER (WHERE w."Kind" = 'kernel.job-deadline' AND w."Due" > 0) = {(timed ? 1 : 0)} AS "Value"
                            FROM "WorkbenchJobWitness" w JOIN "Instances" i ON i."Partition" = w."Partition" AND i."Id" = w."Target"
                            JOIN "Journal" j ON j."Partition" = w."Partition" AND j."Instance" = w."Target" AND j.xmin = w."Transaction"
                            WHERE i."FoundationTenant" = {scope.Tenant} AND i."FoundationReference" = {actor.Id} AND j."Revision" = {revision}
                            """).SingleAsync(token);
                        Require(issuance, $"revision {revision} inserted job generation {generation} and its expected deadline");
                    }
                    if (timed)
                    {
                        var error = timesOut ? "timeout" : null;
                        var result = await observation.Database.SqlQuery<bool>($"""
                            SELECT count(*) = {jobCount} AND count(DISTINCT w."Generation") = {jobCount} AND bool_and(
                                w."Generation" >= 0 AND w."Generation" < {jobCount} AND w."Due" = 0
                                AND c."Revision" = 4 + 2 * w."Generation" AND c."Status" = 0
                                AND c."Epoch" = {completion.Epoch} AND c."At" >= {cut?.Deadline ?? 0}
                                AND (w."Payload"::jsonb ->> 'Error') IS NOT DISTINCT FROM {error}
                                AND CASE WHEN {timesOut} THEN (w."Payload"::jsonb ->> 'Result') IS NULL
                                    ELSE ((w."Payload"::jsonb ->> 'Result')::jsonb ->> 'Result') IS NOT DISTINCT FROM {DirectApprovalWorkflow.JobResult}
                                END) AS "Value"
                            FROM "WorkbenchJobWitness" w JOIN "Instances" i ON i."Partition" = w."Partition" AND i."Id" = w."Target"
                            JOIN "WorkbenchCheckpointWitness" c ON c."Partition" = w."Partition" AND c."Target" = w."Target"
                                AND c."Transaction" = w."Transaction"
                            WHERE i."FoundationTenant" = {scope.Tenant} AND i."FoundationReference" = {actor.Id} AND w."Kind" = 'job-finished'
                            """).SingleAsync(token);
                        Require(result, cut is null ? "each timed generation recorded the expected result in its own running checkpoint"
                            : "only the new owner recorded a timeout at or after the original database deadline");
                        if (cut is not null)
                        {
                            var exactPair = await observation.Database.SqlQuery<bool>($"""
                                SELECT count(*) = 2 AND
                                    count(*) FILTER (WHERE d."Id" = {cut.TicketId} AND d."Due" = 0) = 1 AND
                                    count(*) FILTER (WHERE d."Id" = {cut.DeadlineId} AND d."Due" = {cut.Deadline}) = 1 AS "Value"
                                FROM "WorkbenchJobRemovalWitness" d JOIN "WorkbenchCheckpointWitness" c ON c."Partition" = d."Partition"
                                    AND c."Target" = d."Target" AND c."Transaction" = d."Transaction"
                                JOIN "Instances" i ON i."Partition" = d."Partition" AND i."Id" = d."Target"
                                WHERE i."FoundationTenant" = {scope.Tenant} AND i."FoundationReference" = {actor.Id}
                                    AND c."Revision" = 4 AND c."Epoch" = {completion.Epoch}
                                """).SingleAsync(token);
                            Require(exactPair, "settlement consumes the original ticket and unchanged deadline in the timeout transaction");
                        }
                    }
                }
                if (cut is not null)
                {
                    Require(await Approve(actor, "approval-2", "changed again", previous.Frame) == Contract.AdmissionOutcome.Duplicate &&
                        await Read(actor) == completion && JournalHash(await reads.TailAsync(scope, actor, bookmark, token)) == JournalHash(suffix),
                        "replaying the job-starting approval after takeover leaves completion unchanged");
                }
                last = completion;
            }
        }
        finally
        {
            if (workbench is not null)
                using (workbench) await workbench.StopAsync();
            if (host is not null)
            {
                using var shutdown = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await host.StopAsync(shutdown.Token);
            }
        }

        Require(await Read(actor) == (last with { Owner = null }),
            workbench is null ? "StopAsync clears the partition owner at the same epoch before host disposal"
                : "the stopped Workbench has released the partition at the same epoch");
        if (receipt is not null)
            await File.WriteAllTextAsync(receiptPath, JsonSerializer.Serialize(receipt), token);
        Require(wire.Trips > 0 && wire.Connections > 0 && wire.ProtocolRequests > 0, "the meter observed the preview provider traffic");
        Console.WriteLine($"wire {phase} {wire.Trips} round trips, {wire.ProtocolRequests} protocol groups, {wire.Connections} connections");
        Console.WriteLine($"ok Workbench process {ownerPid} {phase} stopped");

        async Task CheckJobFreeNeighbor(WorkflowRuntime runtime)
        {
            var started = await runtime.StartAsync(scope, "job-free", NativeExecutionModel.Start(DirectApprovalWorkflow.Definition), cancellationToken: token);
            Require(started is { Outcome: Contract.AdmissionOutcome.Accepted, Actor: not null }, "timed profile admits an ordinary root beside the timed workflow");
            var neighbor = started.Actor!.Value;
            await WaitFor(neighbor, 1);
            var frame = WaitFrame(await reads.TailAsync(scope, neighbor, cancellationToken: token));
            foreach (var (person, revision) in new[] { ("Ada", 2L), ("Grace", 3L) })
            {
                Require(await Approve(neighbor, "neighbor-" + person, person, frame) == Contract.AdmissionOutcome.Accepted,
                    "the ordinary root's approval was admitted");
                await WaitFor(neighbor, revision);
            }
            Require(await reads.GetAsync(scope, neighbor, token) is { Header.Status: Contract.ActorStatus.Finished },
                "the same Workbench process finished the ordinary root");
        }

        IHost CreateFixtureHost()
        {
            var builder = Host.CreateEmptyApplicationBuilder(new());
            builder.Logging.ClearProviders();
            builder.Services.AddHostedService(_ => new PostgresPreviewHostedService(Open, node,
                workflowActor, Environment.FailFast));
            return builder.Build();
        }

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

        async Task<ActiveJob?> ReadActiveJob(Contract.ActorRef actor)
        {
            await using var db = OpenObservation();
            return await db.Database.SqlQuery<ActiveJob>($"""
                SELECT p."Epoch", p."Owner", i."State", j."Id" AS "TicketId", d."Id" AS "DeadlineId", d."Due" AS "Deadline",
                    floor(extract(epoch from clock_timestamp()) * 1000)::bigint AS "Now"
                FROM "Instances" i JOIN "Partitions" p ON p."Id" = i."Partition"
                JOIN "Messages" j ON j."Partition" = i."Partition" AND j."Target" = i."Id"
                JOIN "Messages" d ON d."Partition" = i."Partition" AND d."Target" = i."Id"
                WHERE i."FoundationTenant" = {scope.Tenant} AND i."FoundationReference" = {actor.Id}
                    AND p."Owner" IS NOT NULL AND i."Status" = 0 AND i."Revision" = 3 AND i."Jobs" IS NOT NULL AND i."NextJobGeneration" = 1
                    AND j."InboxScope" = {TimedJobScope} AND j."Kind" = 'kernel.job' AND j."JobGeneration" = 0 AND NOT j."Done"
                    AND d."InboxScope" = {TimedJobScope} AND d."Kind" = 'kernel.job-deadline' AND d."JobGeneration" = 0 AND NOT d."Done"
                    AND NOT EXISTS (SELECT 1 FROM "Messages" m WHERE m."Partition" = i."Partition" AND m."Target" = i."Id"
                        AND m."InboxScope" = {TimedJobScope} AND m."Kind" = 'job-finished')
                """).SingleOrDefaultAsync(token);
        }

        async Task<Checkpoint> WaitFor(Contract.ActorRef actor, long revision)
        {
            while (true)
            {
                var checkpoint = await Read(actor);
                if (checkpoint?.Revision > revision)
                    throw new InvalidOperationException($"Unexpected checkpoint revision {checkpoint.Revision}.");
                if (checkpoint is not null && checkpoint.Revision == revision &&
                    (workbench is null ? checkpoint.Owner == node : checkpoint.Owner?.StartsWith(nodePrefix, StringComparison.Ordinal) == true))
                    return checkpoint;
                await Task.Delay(40, token);
            }
        }
    }

    private sealed class WorkbenchProcess(Process process, DirectoryInfo workspace, Task<string> log) : IDisposable
    {
        private readonly TaskCompletionSource<string> jobEntry = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Id => process.Id;
        internal Task<string> WaitForJobEntryAsync(CancellationToken token) => jobEntry.Task.WaitAsync(token);

        internal static WorkbenchProcess Start(string executable, string connection, bool enabled, string? profile)
        {
            var binary = Path.GetFullPath(executable);
            var workspace = Directory.CreateTempSubdirectory("elsa-workbench-core-preview-");
            var source = Path.GetDirectoryName(binary)!;
            foreach (var file in new[] { "appsettings.json", "appsettings.Development.json", "shells.json", "shells.Development.json" })
                if (File.Exists(Path.Combine(source, file)))
                    File.Copy(Path.Combine(source, file), Path.Combine(workspace.FullName, file));
            workspace.CreateSubdirectory("packages");
            var start = new ProcessStartInfo(binary)
            {
                WorkingDirectory = workspace.FullName,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in new[] { "--contentRoot", workspace.FullName, "--urls", "http://127.0.0.1:0" })
                start.ArgumentList.Add(argument);
            // Workbench reads database and module configuration from its environment at startup.
            start.Environment.Clear();
            foreach (var name in new[] { "HOME", "USERPROFILE", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_CACHE_HOME", "DOTNET_CLI_HOME", "TMPDIR" })
                start.Environment[name] = workspace.FullName;
            start.Environment["DOTNET_ROOT"] = Path.GetFullPath(Path.Combine(
                System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";
            start.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            start.Environment["Nuplane__Setup__StateFilePath"] = Path.Combine(workspace.FullName, ".nuplane", "store-state.json");
            start.Environment["Logging__LogLevel__Microsoft.Hosting.Lifetime"] = "Information";
            if (enabled)
                start.Environment["Elsa__CorePreview__Enabled"] = "true";
            if (profile == "jobs")
                start.Environment["Elsa__CorePreview__NativeJobs"] = "true";
            if (profile is "timed" or "timeout" or "takeover")
                start.Environment["Elsa__CorePreview__TimedNativeJobs"] = "true";
            start.Environment["ConnectionStrings__KernelCorePreview"] = connection;
            var process = Process.Start(start) ?? throw new InvalidOperationException("Workbench did not start.");
            return new WorkbenchProcess(process, workspace, process.StandardError.ReadToEndAsync());
        }

        internal async Task WaitReadyAsync(CancellationToken token)
        {
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                // Kestrel emits this after hosted services have finished starting.
                const string listening = "Now listening on: ";
                var offset = line.IndexOf(listening, StringComparison.Ordinal);
                if (offset < 0)
                    continue;

                _ = ReadRuntimeOutputAsync();
                using var client = new HttpClient();
                using var response = await client.GetAsync(line[(offset + listening.Length)..], token);
                Require(response.IsSuccessStatusCode, "the normal Workbench root responds");
                return;
            }
            throw new InvalidOperationException($"Workbench exited before listening. {await log.WaitAsync(token)}");
        }

        private async Task ReadRuntimeOutputAsync()
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
                if (line.StartsWith(DirectApprovalWorkflow.ActiveJobEntry, StringComparison.Ordinal))
                    jobEntry.TrySetResult(line[DirectApprovalWorkflow.ActiveJobEntry.Length..]);
        }

        internal async Task CrashAsync(CancellationToken token)
        {
            Require(!process.HasExited, "the entered Workbench process is still running before abrupt exit");
            process.Kill();
            await process.WaitForExitAsync(token);
            Require(process.ExitCode != 0, "the entered owner exited abruptly");
        }

        internal async Task StopAsync()
        {
            if (!process.HasExited)
            {
                using var terminate = Process.Start("kill", ["-TERM", process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)])
                    ?? throw new InvalidOperationException("Could not request Workbench shutdown.");
                await terminate.WaitForExitAsync();
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await process.WaitForExitAsync(deadline.Token);
            Require(process.ExitCode == 0, $"Workbench exited cleanly (exit {process.ExitCode})");
        }

        public void Dispose()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }
            process.Dispose();
            workspace.Delete(recursive: true);
        }
    }

    // Preserve the physical insertion witness after the owner deletes the ticket during normal result settlement.
    private static Task<int> InstallJobWitnessAsync(KernelDb db, int ticketScope, CancellationToken token)
    {
        var sql = FormattableString.Invariant($"""
            CREATE TABLE "WorkbenchJobWitness" ("Partition" int, "Target" text, "Generation" bigint, "Transaction" xid, "Kind" text, "Due" bigint, "Payload" text);
            CREATE TABLE "WorkbenchJobRemovalWitness" ("Partition" int, "Target" text, "Id" text, "Due" bigint, "Transaction" xid);
            CREATE TABLE "WorkbenchCheckpointWitness" ("Partition" int, "Target" text, "Revision" bigint, "Status" integer, "Transaction" xid, "Epoch" bigint, "At" bigint);
            CREATE FUNCTION capture_workbench_job() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF NEW."InboxScope" = {ticketScope} THEN
                    INSERT INTO "WorkbenchJobWitness" VALUES (NEW."Partition", NEW."Target", NEW."JobGeneration", NEW.xmin, NEW."Kind", NEW."Due", NEW."Payload");
                END IF;
                RETURN NEW;
            END;
            $fn$;
            CREATE TRIGGER capture_workbench_job AFTER INSERT ON "Messages" FOR EACH ROW EXECUTE FUNCTION capture_workbench_job();
            CREATE FUNCTION capture_workbench_job_removal() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF OLD."InboxScope" = {ticketScope} THEN
                    INSERT INTO "WorkbenchJobRemovalWitness" VALUES (OLD."Partition", OLD."Target", OLD."Id", OLD."Due", pg_current_xact_id()::xid);
                END IF;
                RETURN OLD;
            END;
            $fn$;
            CREATE TRIGGER capture_workbench_job_removal AFTER DELETE ON "Messages" FOR EACH ROW
                EXECUTE FUNCTION capture_workbench_job_removal();
            CREATE FUNCTION capture_workbench_checkpoint() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                INSERT INTO "WorkbenchCheckpointWitness"
                    SELECT NEW."Partition", NEW."Id", NEW."Revision", NEW."Status", NEW.xmin, p."Epoch",
                        floor(extract(epoch from clock_timestamp()) * 1000)::bigint
                    FROM "Partitions" p WHERE p."Id" = NEW."Partition";
                RETURN NEW;
            END;
            $fn$;
            CREATE TRIGGER capture_workbench_checkpoint AFTER UPDATE OF "Revision" ON "Instances" FOR EACH ROW
                EXECUTE FUNCTION capture_workbench_checkpoint();
            """);
        return db.Database.ExecuteSqlRawAsync(sql, token);
    }

    private static KernelDb OpenProfile(DbContextOptions<KernelDb> options, string? profile) => profile switch
    {
        "timed" or "timeout" or "takeover" => PostgresPreviewHosting.CreateTimedNativeJobContext(options),
        "jobs" => PostgresPreviewHosting.CreateNativeJobContext(options),
        _ => new(options)
    };

    private static void PrintVersion(Assembly assembly) =>
        Console.WriteLine($"provider {assembly.GetName().Name} {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]}");

    private static string Entry(Contract.JournalEntry entry)
    {
        using var parsed = JsonDocument.Parse(entry.Entry);
        return parsed.RootElement.ValueKind == JsonValueKind.String ? parsed.RootElement.GetString()! : "";
    }

    private static NativeState Native(Checkpoint checkpoint, bool timed)
    {
        var prefix = timed ? "!kernel-state/6:public-root-timed-jobs:" : "!kernel-state/4:public-root:";
        Require(checkpoint.State.StartsWith(prefix, StringComparison.Ordinal), "the state uses the expected preview framing");
        return JsonSerializer.Deserialize<NativeState>(checkpoint.State[prefix.Length..])!;
    }

    private static string WaitFrame(Contract.Page<Contract.JournalEntry> page) =>
        page.Items.Select(Entry).Single(entry => entry.StartsWith(DirectApprovalWorkflow.WaitEntry, StringComparison.Ordinal))
            [DirectApprovalWorkflow.WaitEntry.Length..];

    private static string JournalHash(Contract.Page<Contract.JournalEntry> page) =>
        StateHash(JsonSerializer.Serialize(page.Items));

    private static string StateHash(string state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine($"ok {message}");
    }
}
