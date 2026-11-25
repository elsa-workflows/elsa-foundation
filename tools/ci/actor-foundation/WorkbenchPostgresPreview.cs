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
    private sealed record Checkpoint(long Revision, string State, long Epoch, string? Owner)
    {
        // The stable body of a profile that keeps it apart (the shipped format 25): the state itself, in its own column.
        public byte[]? Wire { get; init; }
    }
    private sealed record ActiveJob(long Epoch, string Owner, string State, string Registry,
        string TicketId, long TicketSeq, string TicketPayload, string DeadlineId, long DeadlineSeq, string DeadlinePayload,
        long Deadline, long Now);
    private sealed record JobEntry(string Actor, string EffectKey);
    private sealed record ReplayRows(string Checkpoint, string Messages, string Receipts);

    internal static async Task RunAsync(string[] arguments, string? executable = null)
    {
        if (arguments is not [var phase and ("birth" or "restart" or "disabled" or "install-empty" or "install-partial" or "install-foreign"), var receiptPath, .. var profileArguments] ||
            profileArguments is not ([] or ["jobs" or "timed" or "timeout" or "takeover" or "takeover-complete" or "publications"]))
            throw new ArgumentException("Expected birth, restart or disabled, a receipt file and optional jobs, timed, timeout, takeover, takeover-complete or publications profile.");
        var profile = profileArguments.SingleOrDefault();
        var completesTakeover = profile == "takeover-complete";
        var takeover = profile == "takeover" || completesTakeover;
        var timed = takeover || profile is "timed" or "timeout";
        var timesOut = profile is "timeout" or "takeover";
        // The publications profile is the ordinary format with the kernel serving the default shell's publications.
        var publications = profile == "publications";
        var nativeJobs = profile is not (null or "publications");
        var ticketScope = timed ? TimedJobScope : JobTicketScope;

        if ((phase is "disabled" or "install-empty" or "install-partial" or "install-foreign" || takeover) && executable is null)
            throw new ArgumentException("The disabled, install and takeover checks require a Workbench executable.");
        if (phase is "install-empty" or "install-partial" or "install-foreign" && !publications)
            throw new ArgumentException("The install checks run the publications profile, which installs its own format.");

        var releaseFile = completesTakeover ? Path.GetFullPath(receiptPath + ".release") : null;
        if (releaseFile is not null && File.Exists(releaseFile))
            throw new ArgumentException("Use a fresh receipt and release file for the takeover-complete lane.");

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

        // The publications profile runs in the tenant of the Workbench's seeded administrator, whose Runs list the check reads.
        var scope = EmbeddedTenant.Bind(publications ? "default" : "workbench-preview");
        var observationOptions = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(settings.ConnectionString).Options;
        KernelDb OpenObservation() => OpenProfile(observationOptions, profile);
        var wire = Wire.Start("postgres", $"Host={settings.Host};Port={settings.Port}");
        settings.Port = wire.LocalPort;
        if (phase is "install-empty" or "install-partial" or "install-foreign")
        {
            await CheckWorkbenchInstallAsync(executable!, settings.ConnectionString, OpenObservation, phase["install-".Length..], scope, token);
            return;
        }
        if (phase == "disabled")
        {
            if (completesTakeover) await CheckEntryBeforeListeningAsync();
            using var disabled = WorkbenchProcess.Start(executable!, settings.ConnectionString, enabled: false, profile, releaseFile);
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
        var definition = takeover ? DirectApprovalWorkflow.BuildWithTimedTakeover(releaseFile) :
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
                var wrong = await PostgresPreviewHosting.StartAsync(() => OpenProfile(observationOptions, !nativeJobs ? "jobs" : timed ? "jobs" : null),
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
        var workbench = executable is null ? null : WorkbenchProcess.Start(executable, settings.ConnectionString, enabled: true, profile, releaseFile);
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
            {
                await workbench.WaitReadyAsync(token);
                if (publications) await RequireKernelServingAsync(workbench, ProbeRunsAsync, token);
            }

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
                Require(Native(waiting, profile).Frames[frame].State!.AsArray().Select(item => item!.GetValue<string>()).SequenceEqual(["Ada"]),
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
                    JobEntry originalEntry;
                    using (former)
                    {
                        originalEntry = await former.WaitForJobEntryAsync(token);
                        Require(originalEntry.Actor == actor.Id && !string.IsNullOrWhiteSpace(originalEntry.EffectKey),
                            "the original Workbench entered the selected actor's timed job");
                        cut = await ReadActiveJob(actor) ?? throw new InvalidOperationException("The entered job has no preserved ticket and deadline.");
                        Require(cut.Owner == waiting.Owner && cut.Epoch == waiting.Epoch && cut.Deadline > cut.Now,
                            "revision three contains the active job on the original epoch with time left");
                        using var ticket = JsonDocument.Parse(cut.TicketPayload);
                        var argumentsJson = ticket.RootElement.GetProperty("Arguments").GetString()
                            ?? throw new InvalidOperationException("The active job has no invocation arguments.");
                        using var invocation = JsonDocument.Parse(argumentsJson);
                        var nativeJson = invocation.RootElement.GetProperty("Arguments").GetString()
                            ?? throw new InvalidOperationException("The workflow job has no Native arguments.");
                        var job = JsonSerializer.Deserialize<BackgroundArgs>(nativeJson);
                        Require(invocation.RootElement.GetProperty("Model").GetString() == NativeExecutionModel.ModelKey &&
                            job is not null && job.Workflow == definition.Id && job.Node == DirectApprovalWorkflow.JobNode &&
                            job.EffectKey == originalEntry.EffectKey &&
                            job.Inputs[nameof(DirectApprovalWorkflow.WaitForTakeover.ReleaseFile)]?.GetValue<string>() == releaseFile,
                            "the entered callback and release signal match the durable job recipe");
                        await former.CrashAsync(token);
                        var interrupted = await ReadActiveJob(actor);
                        Require(interrupted is not null && (interrupted with { Now = cut.Now }) == cut && interrupted.Now < cut.Deadline,
                            "abrupt exit leaves the original fence, checkpoint, ticket and deadline intact before timeout");
                    }

                    workbench = WorkbenchProcess.Start(executable!, settings.ConnectionString, enabled: true, profile, releaseFile);
                    Require(workbench.Id != ownerPid, "a different Workbench process starts after the owner exits");
                    ownerPid = workbench.Id;
                    nodePrefix = $"workbench-core-preview-{ownerPid}-";
                    await workbench.WaitReadyAsync(token);
                    if (publications) await RequireKernelServingAsync(workbench, ProbeRunsAsync, token);
                    if (releaseFile is not null)
                    {
                        Require(await workbench.WaitForJobEntryAsync(token) == originalEntry,
                            "the replacement entered the same actor's recovered job with its original effect key");
                        var resumed = await ReadActiveJob(actor);
                        Require(resumed is not null && resumed.Epoch > cut.Epoch && resumed.Owner.StartsWith(nodePrefix, StringComparison.Ordinal) &&
                            (resumed with { Epoch = cut.Epoch, Owner = cut.Owner, Now = cut.Now }) == cut && resumed.Now < cut.Deadline,
                            "the higher-epoch callback uses the original checkpoint, ticket and deadline before release");
                        using var release = new FileStream(releaseFile, FileMode.CreateNew, FileAccess.Write);
                    }
                }
                var chained = profile == "timed";
                var finalRevision = chained ? 7L : timed ? 5L : nativeJobs ? 4L : 3L;
                var completion = await WaitFor(actor, finalRevision);
                if (cut is not null)
                    Require(completion.Epoch > cut.Epoch, "the replacement Workbench applied the outcome at a higher epoch");
                var native = Native(completion, profile);
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
                                AND c."Epoch" = {completion.Epoch} AND CASE WHEN {completesTakeover}
                                    THEN floor(extract(epoch from clock_timestamp()) * 1000)::bigint < {cut?.Deadline ?? 0}
                                    ELSE c."At" >= {cut?.Deadline ?? 0} END
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
                            : completesTakeover ? "only the replacement recorded successful completion before the original database deadline"
                            : "only the new owner recorded a timeout at or after the original database deadline");
                        if (cut is not null)
                        {
                            var exactPair = await observation.Database.SqlQuery<bool>($"""
                                WITH target AS (
                                    SELECT i."Id", i."Partition" FROM "Instances" i
                                    WHERE i."FoundationTenant" = {scope.Tenant} AND i."FoundationReference" = {actor.Id}
                                ), removals AS (
                                    SELECT d.*, c."Revision", c."Epoch", c."Registry"
                                    FROM "WorkbenchJobRemovalWitness" d JOIN target i ON i."Partition" = d."Partition" AND i."Id" = d."Target"
                                    LEFT JOIN "WorkbenchCheckpointWitness" c ON c."Partition" = d."Partition"
                                        AND c."Target" = d."Target" AND c."Transaction" = d."Transaction"
                                    WHERE d."Kind" IN ('kernel.job', 'kernel.job-deadline')
                                )
                                SELECT (SELECT count(*) FROM "WorkbenchJobWitness" w
                                        JOIN target i ON i."Partition" = w."Partition" AND i."Id" = w."Target"
                                        WHERE w."Kind" IN ('kernel.job', 'kernel.job-deadline')) = 2
                                    AND count(*) = 2 AND bool_and((d."Revision" = 4 AND d."Epoch" = {completion.Epoch}
                                        AND d."Registry" = {cut.Registry} AND d."Generation" = 0) IS TRUE)
                                    AND count(*) FILTER (WHERE d."Id" = {cut.TicketId} AND d."Seq" = {cut.TicketSeq}
                                        AND d."Due" = 0 AND d."Payload" = {cut.TicketPayload}) = 1
                                    AND count(*) FILTER (WHERE d."Id" = {cut.DeadlineId} AND d."Seq" = {cut.DeadlineSeq}
                                        AND d."Due" = {cut.Deadline} AND d."Payload" = {cut.DeadlinePayload}) = 1 AS "Value"
                                FROM removals d
                                """).SingleAsync(token);
                            Require(exactPair, "the original ticket and unchanged deadline are the only pair inserted and removed");
                        }
                    }
                }
                if (cut is not null)
                {
                    var completeJournal = JournalHash(await reads.TailAsync(scope, actor, cancellationToken: token));
                    var beforeReplay = await ReadReplayRows(actor);
                    Require(await Approve(actor, "approval-2", "changed again", previous.Frame) == Contract.AdmissionOutcome.Duplicate &&
                        await Read(actor) == completion && await ReadReplayRows(actor) == beforeReplay &&
                        JournalHash(await reads.TailAsync(scope, actor, cancellationToken: token)) == completeJournal,
                        "replaying the job-starting approval leaves the checkpoint, full journal, inbox and receipts unchanged");
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
            // On the shipped format the node answers held callers.
            builder.Services.AddHostedService(_ => new PostgresPreviewHostedService(Open, node,
                workflowActor, Environment.FailFast,
                requests: publications ? PostgresPreviewHosting.CreateRequests(Open, node, workflowActor.Type) : null));
            return builder.Build();
        }

        async Task<Contract.AdmissionOutcome> Approve(Contract.ActorRef actor, string id, string person, string frame) =>
            (await ingress.SendAsync(scope, new Contract.Target.Ref(actor), id, CompositeSignal.Kind,
                JsonSerializer.Serialize(new CompositeSignal(frame, "approve", person)), cancellationToken: token)).Outcome;

        // Finished runs of the Core Preview's HTTP probe in this check's tenant.
        async Task<IReadOnlyList<string>> ProbeRunsAsync() => (await reads.ListAsync(scope, new Contract.ActorQuery(Kernel.WorkflowActor.WorkflowActor.ActorType,
            Contract.ActorStatus.Finished, new Dictionary<string, string> { ["definition"] = "core-preview-http-probe" }), cancellationToken: token)).Items
            .Select(view => view.Header.Ref.Id).ToArray();

        async Task<Checkpoint?> Read(Contract.ActorRef actor)
        {
            await using var db = OpenObservation();
            var wire = db.UsesRootStableBody ? """i."FoundationStateWire" """ : "NULL::bytea";
            var checkpoint = await db.Database.SqlQueryRaw<Checkpoint>("""
                SELECT i."Revision", i."State", p."Epoch", p."Owner", /*wire*/ AS "Wire" FROM "Instances" i
                JOIN "Partitions" p ON p."Id" = i."Partition"
                WHERE i."FoundationTenant" = {0} AND i."FoundationReference" = {1}
                """.Replace("/*wire*/", wire, StringComparison.Ordinal), scope.Tenant, actor.Id).SingleOrDefaultAsync(token);
            // A stable body's state column holds its marker: read the state from the body, under the family's prefix.
            return checkpoint is { Wire: { } body }
                ? checkpoint with { State = db.RootStateFamily.Prefix + RootSelfContent.Decode(body), Wire = null }
                : checkpoint;
        }

        async Task<ActiveJob?> ReadActiveJob(Contract.ActorRef actor)
        {
            await using var db = OpenObservation();
            return await db.Database.SqlQuery<ActiveJob>($"""
                SELECT p."Epoch", p."Owner", i."State", i."Jobs" AS "Registry",
                    j."Id" AS "TicketId", j."Seq" AS "TicketSeq", j."Payload" AS "TicketPayload",
                    d."Id" AS "DeadlineId", d."Seq" AS "DeadlineSeq", d."Payload" AS "DeadlinePayload", d."Due" AS "Deadline",
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

        async Task<ReplayRows> ReadReplayRows(Contract.ActorRef actor)
        {
            await using var db = OpenObservation();
            return await db.Database.SqlQuery<ReplayRows>($"""
                SELECT to_jsonb(i)::text AS "Checkpoint",
                    coalesce((SELECT jsonb_agg(to_jsonb(m) ORDER BY m."Seq", m."Id") FROM "Messages" m
                            WHERE m."Partition" = i."Partition" AND m."Target" = i."Id"), '[]'::jsonb)::text AS "Messages",
                    coalesce((SELECT jsonb_agg(to_jsonb(r) ORDER BY r."Operation") FROM "FoundationDirectReceipts" r
                            WHERE r."Reference" = i."FoundationReference"), '[]'::jsonb)::text AS "Receipts"
                FROM "Instances" i WHERE i."FoundationTenant" = {scope.Tenant} AND i."FoundationReference" = {actor.Id}
                """).SingleAsync(token);
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

    // With publications on, the Workbench installs the shipped format before its node starts: in an empty database, and over
    // a first install that was cut short (the store's marker seeded, the rest not). It refuses a database that holds
    // something else, ending before its node starts.
    private static async Task CheckWorkbenchInstallAsync(string executable, string connection, Func<KernelDb> openObservation, string start,
        Contract.BoundScope scope, CancellationToken token)
    {
        var foreign = start == "foreign";
        await using (var db = openObservation())
        {
            Require(await PostgresFixtureDatabase.IsEmptyAsync(db, token), "the install check starts from an empty database");
            if (foreign) await db.Database.ExecuteSqlRawAsync("CREATE TABLE someone_elses_orders (id integer PRIMARY KEY)", token);
        }
        if (start == "partial") await new EfStore(openObservation, postgres: true).CreateAsync();
        using var workbench = WorkbenchProcess.Start(executable, connection, enabled: true, "publications", null);
        if (foreign)
        {
            var (exitCode, error) = await workbench.WaitForExitAsync(token);
            await using var db = openObservation();
            var tables = await db.Database.SqlQueryRaw<string>(
                "SELECT tablename AS \"Value\" FROM pg_catalog.pg_tables WHERE schemaname = pg_catalog.current_schema()").ToListAsync(token);
            Require(exitCode != 0 && error.Contains("Recreate this database empty", StringComparison.Ordinal) &&
                    tables.SequenceEqual(["someone_elses_orders"]),
                $"a Workbench with publications on refuses a database that holds something else, installs nothing, and says to recreate it (exit {exitCode})");
            return;
        }
        try
        {
            await workbench.WaitReadyAsync(token);
            Contract.IActorReads? reads = null;
            async Task<IReadOnlyList<string>> ProbeRunsAsync()
            {
                reads ??= await new EfStore(openObservation, postgres: true).OpenPreviewRootReadsAsync(64, token);
                return (await reads.ListAsync(scope, new Contract.ActorQuery(Kernel.WorkflowActor.WorkflowActor.ActorType, Contract.ActorStatus.Finished,
                    new Dictionary<string, string> { ["definition"] = "core-preview-http-probe" }), cancellationToken: token)).Items
                    .Select(view => view.Header.Ref.Id).ToArray();
            }
            await RequireKernelServingAsync(workbench, ProbeRunsAsync, token);
            await using var db = openObservation();
            await EfStore.RequireStoreVersionsAsync(db, token);
            Require(true, start == "partial"
                ? "a Workbench with publications on completes a first install that was cut short, starts and serves"
                : "a Workbench with publications on installs the shipped format in an empty database, starts and serves");
        }
        finally
        {
            await workbench.StopAsync();
        }
    }

    // With publications on, the default shell composes the kernel's coordinator, which serves its active slots before the node runs.
    private static async Task RequireKernelServingAsync(WorkbenchProcess workbench, Func<Task<IReadOnlyList<string>>> probeRuns, CancellationToken token)
    {
        Require(workbench.KernelComposed is { } composed && composed.Contains("into shell default", StringComparison.Ordinal),
            "the Core Preview composes the kernel's feature into the default shell");
        var line = await workbench.WaitForKernelServingAsync(token);
        if (line.Contains("could not serve the publications", StringComparison.Ordinal))
            throw new InvalidOperationException($"the default shell could not serve its publications on the kernel: {line.Trim()}");
        if (line.Contains("does not compose the WorkflowsRuntimeKernel feature", StringComparison.Ordinal))
            throw new InvalidOperationException($"the default shell does not compose the kernel's feature: {line.Trim()}");
        Require(line.Contains("shell default on the kernel", StringComparison.Ordinal), "the default shell serves its publications on the kernel");

        // The kernel's HTTP edge takes /workflows/http/**, and the old core keeps /workflows/test-runs.
        var address = (await workbench.ListeningAsync(token)).TrimEnd('/');
        using var client = new HttpClient();
        var before = (await probeRuns()).Count;
        using (var probe = await client.PostAsync($"{address}/workflows/http/core-preview/probe",
                   new StringContent("""{"name":"Ada"}""", System.Text.Encoding.UTF8, "application/json"), token))
            Require(probe.StatusCode == System.Net.HttpStatusCode.OK && await probe.Content.ReadAsStringAsync(token) == "Ada",
                "a request to a kernel-served route under /workflows/http is answered by the kernel workflow");
        for (var attempt = 0; (await probeRuns()).Count != before + 1; attempt++)
        {
            if (attempt == 100) throw new InvalidOperationException("the kernel-served request left no Finished run");
            await Task.Delay(50, token);
        }
        Require(true, "the kernel-served request leaves one Finished run");
        // Studio's Runs list on the kernel shell: the runtime API lists the kernel's runs, the probe's among them, as Completed.
        using (var session = new HttpClient(new HttpClientHandler { CookieContainer = new System.Net.CookieContainer(), AllowAutoRedirect = false }))
        {
            using (var login = await session.PostAsync($"{address}/_elsa/identity/login",
                       new StringContent("""{"username":"admin","password":"Password123!"}""", System.Text.Encoding.UTF8, "application/json"), token))
                Require(login.IsSuccessStatusCode, $"the check signs in to the Workbench as its seeded administrator ({(int)login.StatusCode})");
            var expected = await probeRuns();
            using var listed = await session.GetAsync($"{address}/runtime/workflows/instances/page?definitionId=core-preview-http-probe&status=Completed&take=100", token);
            var body = await listed.Content.ReadAsStringAsync(token);
            Require(listed.IsSuccessStatusCode, $"the runtime API lists runs on the kernel shell ({(int)listed.StatusCode})");
            using var page = JsonDocument.Parse(body);
            var items = page.RootElement.GetProperty("items").EnumerateArray().ToArray();
            var ids = items.Select(item => item.GetProperty("workflowExecutionId").GetString()).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            Require(ids.SequenceEqual(expected.OrderBy(id => id, StringComparer.Ordinal)) &&
                    items.All(item => item.GetProperty("status").GetString() == "Completed" &&
                        item.GetProperty("definitionId").GetString() == "core-preview-http-probe" && item.GetProperty("createdAt").ValueKind == JsonValueKind.String),
                $"Studio's Runs list on the kernel shell lists the probe's kernel runs, Completed, with their start ({ids.Length} of {expected.Count})");
        }
        using (var unknown = await client.PostAsync($"{address}/workflows/http/no-such-route", new StringContent(""), token))
            Require(unknown.StatusCode == System.Net.HttpStatusCode.NotFound &&
                    await unknown.Content.ReadAsStringAsync(token) == "no workflow serves this route",
                "a request under /workflows/http that no route serves is answered by the kernel edge, not the old core");
        using (var testRun = await client.PostAsync($"{address}/workflows/test-runs/qualification/http/orders", new StringContent(""), token))
            Require(testRun.StatusCode == System.Net.HttpStatusCode.Unauthorized,
                "an anonymous /workflows/test-runs request still reaches the old core's test-run handler (401)");

        // The edge's own bounds and body reading.
        var oversized = new string('x', 1024 * 1024 + 1);
        using (var large = await client.PostAsync($"{address}/workflows/http/core-preview/echo",
                   new StringContent(oversized, System.Text.Encoding.UTF8, "text/plain"), token))
            Require(large.StatusCode == System.Net.HttpStatusCode.RequestEntityTooLarge,
                "a body over the edge's 1 MiB bound is refused with 413");
        using (var text = await client.PostAsync($"{address}/workflows/http/core-preview/echo",
                   new StringContent("Hello, Ada", System.Text.Encoding.UTF8, "text/plain"), token))
            Require(text.StatusCode == System.Net.HttpStatusCode.OK && await text.Content.ReadAsStringAsync(token) == "Hello, Ada",
                $"a text body reaches the workflow as its string ({(int)text.StatusCode} {await text.Content.ReadAsStringAsync(token)})");
        using (var xml = await client.PostAsync($"{address}/workflows/http/core-preview/echo",
                   new StringContent("<a/>", System.Text.Encoding.UTF8, "application/xml"), token))
            Require(xml.StatusCode == System.Net.HttpStatusCode.UnsupportedMediaType, "a body that is neither JSON nor text is refused with 415");
        using (var unknownCharset = new System.Net.Http.ByteArrayContent("Hello"u8.ToArray()))
        {
            unknownCharset.Headers.TryAddWithoutValidation("Content-Type", "text/plain; charset=x-no-such-charset");
            using var refused = await client.PostAsync($"{address}/workflows/http/core-preview/echo", unknownCharset, token);
            Require(refused.StatusCode == System.Net.HttpStatusCode.UnsupportedMediaType &&
                    (await refused.Content.ReadAsStringAsync(token)).Contains("x-no-such-charset", StringComparison.Ordinal),
                "a text body in a charset the edge does not read is refused with 415, naming the charset");
        }
        using (var unrouted = await client.PostAsync($"{address}/workflows/http/no-such-route",
                   new StringContent(oversized, System.Text.Encoding.UTF8, "text/plain"), token))
            Require(unrouted.StatusCode == System.Net.HttpStatusCode.NotFound, "an unrouted request is not found whatever its body");
        Require(workbench.WaitProbeRefusal is { } refusal &&
                refusal.Contains("An HTTP Endpoint that waits for a request doesn't run on this host yet", StringComparison.Ordinal) &&
                refusal.Contains("node 'decision'", StringComparison.Ordinal),
            "the edge's host refuses a workflow with an HTTP Endpoint that waits, with the reason and what to change");
    }

    private static async Task CheckEntryBeforeListeningAsync()
    {
        var output = new WorkbenchOutput();
        using var lines = new StringReader($"{DirectApprovalWorkflow.ActiveJobEntry}test-actor test-effect\ninfo: Now listening on: http://127.0.0.1:32100\n");
        await output.ReadAsync(lines);
        Require(output.Entry.Task.IsCompletedSuccessfully && output.Entry.Task.Result == new JobEntry("test-actor", "test-effect") &&
            output.Listening.Task.IsCompletedSuccessfully && output.Listening.Task.Result == "http://127.0.0.1:32100",
            "the Workbench output reader retains a callback entry before the listening banner");
    }

    private sealed class WorkbenchOutput
    {
        internal readonly TaskCompletionSource<JobEntry> Entry = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<string> Listening = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource<string> KernelServing = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal volatile string? KernelComposed;
        internal volatile string? WaitProbeRefusal;

        internal async Task ReadAsync(TextReader reader)
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                if (line.StartsWith(DirectApprovalWorkflow.ActiveJobEntry, StringComparison.Ordinal) &&
                    line[DirectApprovalWorkflow.ActiveJobEntry.Length..].Split(' ', 2) is [var actor, var key])
                    Entry.TrySetResult(new(actor, key));
                if (line.Contains("composed the WorkflowsRuntimeKernel feature into shell", StringComparison.Ordinal))
                    KernelComposed = line;
                if (line.Contains("refuses its HTTP wait probe", StringComparison.Ordinal))
                    WaitProbeRefusal = line;
                // The serving outcome: served, or one of the two ways it fails, which the check names instead of timing out.
                if (line.Contains("serves the publications of shell", StringComparison.Ordinal) ||
                    line.Contains("could not serve the publications of shell", StringComparison.Ordinal) ||
                    line.Contains("does not compose the WorkflowsRuntimeKernel feature", StringComparison.Ordinal))
                    KernelServing.TrySetResult(line);
                const string listening = "Now listening on: ";
                var offset = line.IndexOf(listening, StringComparison.Ordinal);
                if (offset >= 0)
                    Listening.TrySetResult(line[(offset + listening.Length)..]);
            }
        }
    }

    private sealed class WorkbenchProcess(Process process, DirectoryInfo workspace, Task<string> log) : IDisposable
    {
        private readonly WorkbenchOutput output = new();
        private Task? stdout;

        internal int Id => process.Id;
        internal Task<JobEntry> WaitForJobEntryAsync(CancellationToken token) =>
            WaitForOutputAsync(output.Entry.Task, "entering the timed job", token);
        internal string? KernelComposed => output.KernelComposed;
        internal string? WaitProbeRefusal => output.WaitProbeRefusal;
        internal Task<string> WaitForKernelServingAsync(CancellationToken token) =>
            WaitForOutputAsync(output.KernelServing.Task, "serving its publications on the kernel", token);

        internal static WorkbenchProcess Start(string executable, string connection, bool enabled, string? profile, string? releaseFile)
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
            if (profile == "publications")
            {
                start.Environment["Elsa__CorePreview__Publications"] = "true";
                start.Environment["Elsa__CorePreview__HttpProbe"] = "true";
                start.Environment["Elsa__CorePreview__Tenant"] = "default";
            }
            if (profile is "timed" or "timeout" or "takeover" or "takeover-complete")
                start.Environment["Elsa__CorePreview__TimedNativeJobs"] = "true";
            if (releaseFile is not null)
                start.Environment["Elsa__CorePreview__TimedJobReleaseFile"] = releaseFile;
            start.Environment["ConnectionStrings__KernelCorePreview"] = connection;
            var process = Process.Start(start) ?? throw new InvalidOperationException("Workbench did not start.");
            return new WorkbenchProcess(process, workspace, process.StandardError.ReadToEndAsync());
        }

        internal Task<string> ListeningAsync(CancellationToken token) => WaitForOutputAsync(output.Listening.Task, "listening", token);

        internal async Task WaitReadyAsync(CancellationToken token)
        {
            // Kestrel emits this after hosted services have finished starting. The owner can enter a recovered job first.
            var address = await WaitForOutputAsync(output.Listening.Task, "listening", token);
            using var client = new HttpClient();
            using var response = await client.GetAsync(address, token);
            Require(response.IsSuccessStatusCode, "the normal Workbench root responds");
        }

        private async Task<T> WaitForOutputAsync<T>(Task<T> signal, string expected, CancellationToken token)
        {
            var read = stdout ??= output.ReadAsync(process.StandardOutput);
            await Task.WhenAny(signal, read).WaitAsync(token);
            if (signal.IsCompletedSuccessfully) return signal.Result;
            await read;
            throw new InvalidOperationException($"Workbench exited before {expected}. {await log.WaitAsync(token)}");
        }

        internal async Task CrashAsync(CancellationToken token)
        {
            Require(!process.HasExited, "the entered Workbench process is still running before abrupt exit");
            process.Kill();
            await process.WaitForExitAsync(token);
            if (stdout is not null) await stdout.WaitAsync(token);
            Require(process.ExitCode != 0, "the entered owner exited abruptly");
        }

        /// <summary>The exit code and standard error of a Workbench that ends on its own.</summary>
        internal async Task<(int ExitCode, string Error)> WaitForExitAsync(CancellationToken token)
        {
            var read = stdout ??= output.ReadAsync(process.StandardOutput);
            await process.WaitForExitAsync(token);
            await read.WaitAsync(token);
            return (process.ExitCode, await log.WaitAsync(token));
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
            if (stdout is not null) await stdout.WaitAsync(deadline.Token);
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
            CREATE TABLE "WorkbenchJobRemovalWitness" ("Partition" int, "Target" text, "Id" text, "Kind" text, "Seq" bigint,
                "Payload" text, "Generation" bigint, "Due" bigint, "Transaction" xid);
            CREATE TABLE "WorkbenchCheckpointWitness" ("Partition" int, "Target" text, "Revision" bigint, "Status" integer, "Transaction" xid, "Epoch" bigint, "At" bigint, "Registry" text);
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
                    INSERT INTO "WorkbenchJobRemovalWitness" VALUES (OLD."Partition", OLD."Target", OLD."Id", OLD."Kind", OLD."Seq",
                        OLD."Payload", OLD."JobGeneration", OLD."Due", pg_current_xact_id()::xid);
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
                        floor(extract(epoch from clock_timestamp()) * 1000)::bigint, NEW."Jobs"
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
        "timed" or "timeout" or "takeover" or "takeover-complete" => PostgresPreviewHosting.CreateTimedNativeJobContext(options),
        "jobs" => PostgresPreviewHosting.CreateNativeJobContext(options),
        // The release profile: publications run on the shipped format 25.
        "publications" => PostgresPreviewHosting.CreateShippedContext(options),
        _ => new(options)
    };

    private static void PrintVersion(Assembly assembly) =>
        Console.WriteLine($"provider {assembly.GetName().Name} {assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]}");

    private static string Entry(Contract.JournalEntry entry)
    {
        using var parsed = JsonDocument.Parse(entry.Entry);
        return parsed.RootElement.ValueKind == JsonValueKind.String ? parsed.RootElement.GetString()! : "";
    }

    private static NativeState Native(Checkpoint checkpoint, string? profile)
    {
        // The profile's root state family: the timed-job family on 18, the composed one on the shipped format 25.
        var prefix = OpenProfile(new DbContextOptionsBuilder<KernelDb>().UseNpgsql("Host=state-family.invalid").Options, profile).RootStateFamily.Prefix;
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
