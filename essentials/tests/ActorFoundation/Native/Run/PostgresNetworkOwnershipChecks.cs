using System.Data.Common;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Native.Preview;
using Kernel.Runtime.Hosting;
using Kernel.WorkflowActor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

// Two independently launched shipped owners. The disposable database is both the witness and the coordination channel.
internal sealed class PostgresNetworkOwnershipChecks(string connection, Guid run, string scenario, bool local, CancellationToken stop)
{
    private const int Partitions = 1024;
    private const int Share = Partitions / 2;
    private const int LeaseMs = 10_000;
    private const int RoundMs = 1_000;
    private const int CrashBoundMs = 2 * LeaseMs + RoundMs;
    private const int WedgeBoundMs = 10 * LeaseMs / 3 + RoundMs;
    private const int ObservationAllowanceMs = 1_000;
    private const int WatchdogBoundMs = 3 * LeaseMs;
    private const int WatchdogExitCode = 35;
    private const string Survivor = "k12-survivor", Victim = "k12-victim";
    private const string Tenant = "network-ownership", Second = "approval-2";
    private const string Clock = "floor(extract(epoch from pg_catalog.clock_timestamp()) * 1000)::bigint";

    private readonly DbContextOptions<KernelDb> _options = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;

    private record Signal(string Name, long At, string Detail);
    private record Identity(string Role, int Process, int Backend, string Client, string Machine, string Boot,
        string System, string Binaries);
    private record Partition(int Id, string? Owner, long Epoch, long Position);
    private record Transition(int Partition, string? Previous, long OldEpoch, string? Owner, long Epoch, bool Died,
        long? LeaseUntil, long At, string Transaction);
    private record Target(string Reference, string Instance, int Partition);
    private record WedgeSample(int Backend, long Revision, long SeenAt, long LeaseUntil);
    private record WatchdogSample(string Reason, long CancelMs, long EndMs);

    internal static async Task<int> RunAsync(Guid run, string scenario, string role, bool local)
    {
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Supply a fresh dedicated PostgreSQL database in KERNEL_PREVIEW_POSTGRES.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var check = new PostgresNetworkOwnershipChecks(connection, run, scenario, local, deadline.Token);
        if (role == "survivor") await check.RunSurvivorAsync();
        else await check.RunVictimAsync(revived: role == "revive");
        return 0;
    }

    private KernelDb Open() => PostgresPreviewHosting.CreateShippedContext(_options);

    private async Task RunSurvivorAsync()
    {
        var timing = new LoopOptions();
        Require(Defaults.ServerPartitions == Partitions && Defaults.Lease.TotalMilliseconds == LeaseMs &&
            timing.BalanceEvery.TotalMilliseconds == RoundMs && timing.PollInterval.TotalMilliseconds == RoundMs,
            "Declared topology and bounds require the current product defaults.");
        await using (var empty = Open())
            Require(await PostgresFixtureDatabase.IsEmptyAsync(empty, stop), "Choose a fresh empty database dedicated to this run.");
        await PostgresPreviewHosting.InstallShippedAsync(_options, WorkflowActor.WorkflowActor.ActorType, protectsStarts: false, stop);
        await using (var install = Open()) await install.Database.ExecuteSqlRawAsync(SetupSql, stop);
        var store = new EfStore(Open, postgres: true);
        await using var anchor = await OpenAnchorAsync(Survivor);
        Log("bounds", new { run, scenario, local, partitions = Partitions, profile = StoreFormat.RootShipped.MessageProtocol,
            model = NativeExecutionModel.ModelKey, postgres = anchor.PostgreSqlVersion.ToString(), fenceInBatch = store.FenceInBatch,
            leaseMs = LeaseMs, roundMs = RoundMs, crashMs = CrashBoundMs, wedgeMs = WedgeBoundMs,
            observationMs = ObservationAllowanceMs, watchdogMs = WatchdogBoundMs });
        var identity = await ReadIdentityAsync(anchor, Survivor);
        await WriteSignalAsync("configuration", $"{run:N}/{scenario}/{local}");
        var requests = PostgresPreviewHosting.CreateRequests(Open, Survivor, WorkflowActor.WorkflowActor.ActorType);
        var foundation = await PostgresPreviewHosting.OpenFoundationAsync(Open, WorkflowActor.WorkflowActor.ActorType,
            requests: requests, cancellationToken: stop);
        await using var owner = await StartOwnerAsync(Open, Survivor, requests, UnexpectedWatchdog, stop);
        var alone = await WaitForOwnersAsync(rows => rows.All(row => row.Owner == Survivor));
        await WriteSignalAsync("start-victim", Encode(identity));
        Log("start-victim", new { run, scenario, local });
        var peer = Decode<Identity>((await WaitSignalAsync("victim-started")).Detail);
        Require(CompatibleIdentity(identity, peer), "The owner roles do not match the declared local or distinct-machine topology and binaries.");
        Log("owners", new { survivor = identity, victim = peer });
        var split = await WaitForOwnersAsync(IsSplit);
        var moved = split.Where(row => row.Owner == Victim).ToArray();
        foreach (var row in split)
            Require(row.Epoch == alone[row.Id].Epoch + (row.Owner == Victim ? 1 : 0), "Unexpected epoch during the initial split.");
        var joins = await ChangesAsync();
        CheckSet(joins.Where(row => row.Previous == Survivor && row.Owner is null), moved, "initial give-up");
        CheckSet(joins.Where(row => row.Previous is null && row.Owner == Victim), moved, "initial gain", died: false);

        var scope = EmbeddedTenant.Bind(Tenant);
        var target = await SelectTargetAsync(foundation, scope, split);
        var reference = new Contract.ActorRef(target.Reference);
        var begun = await WaitImageAsync(store, reference, 1);
        var frame = Json.Read<NativeState>(begun.State!).Frames.Single().Key;
        await SendAsync("approval-1", "Ada", Contract.AdmissionOutcome.Accepted);
        await WaitImageAsync(store, reference, 2);
        var prefix = await foundation.TailAsync(scope, reference, cancellationToken: stop);
        Require(prefix.Next is not null && prefix.Items.Count(item => PostgresPublicPreview.Decode(item) == "approved:approval-1") == 1,
            "The victim did not commit the original approval exactly once.");
        var original = (await OwnersAsync())[target.Partition];
        Require(original == split[target.Partition] with { Position = original.Position } && original.Position == 2,
            "The selected victim did not hold the same epoch at the expected journal position.");

        long start;
        WedgeSample? wedge = null;
        if (scenario == "crash")
        {
            await WriteSignalAsync("crash", target.Reference);
            start = (await WaitSignalAsync("crash")).At;  // database-clock lower bound on the victim's exit
            await WaitAsync(async () => !await BackendExistsAsync(peer.Backend, "anchor-victim"));
            var departed = (await OwnersAsync())[target.Partition];
            var remaining = await QuerySingleAsync<long>($"""
                SELECT "LeaseUntil" - floor(extract(epoch from pg_catalog.clock_timestamp()) * 1000)::bigint AS "Value"
                FROM "Nodes" WHERE "Id" = {Victim}
                """);
            Require(departed == original && remaining > 0, "The abruptly terminated victim must still hold a live database lease.");
            Log("crash-observed", new { remainingMs = remaining });
        }
        else
        {
            await WriteSignalAsync("arm", Encode(target));
            await WaitSignalAsync("armed");
            await SendAsync(Second, "Grace", Contract.AdmissionOutcome.Accepted);
            var entered = await WaitSignalAsync("intercepted");
            wedge = Decode<WedgeSample>(entered.Detail);
            start = wedge.SeenAt;
            Require(wedge.Revision == 3 && wedge.LeaseUntil - wedge.SeenAt == LeaseMs && wedge.LeaseUntil > entered.At,
                "The held commit did not follow a healthy victim renewal.");
            Require(await IsHeldAsync(wedge.Backend, target.Partition), "The interrupted worker must still be holding its real PostgreSQL transaction and partition lock.");
            var visible = await store.ReadFirstRootAsync(Tenant, target.Reference, stop);
            Require(visible is { Revision: 2 } && (await OwnersAsync())[target.Partition] == original,
                "The independent observer saw part of the wedged transaction.");
            Log("wedged-commit", new { target, entered.At, wedge.Backend, wedge.SeenAt, wedge.LeaseUntil });
        }

        var bound = (scenario == "crash" ? CrashBoundMs : WedgeBoundMs) + ObservationAllowanceMs;
        var taken = await WaitForOwnersAsync(rows => rows.All(row => row.Owner == Survivor), start + bound);
        var deaths = (await ChangesAsync()).Where(row => row.Previous == Victim && row.Owner == Survivor).ToArray();
        CheckSet(deaths, moved, "post-loss gain", died: true);
        Require(deaths.All(row => row.LeaseUntil is { } until && row.At >= until), "A takeover ran before the victim lease expired.");
        Require(deaths.Max(row => row.At) - start <= bound, "The takeover exceeded the predeclared provider-clock bound.");
        foreach (var row in taken)
            Require(row.Epoch == split[row.Id].Epoch + (split[row.Id].Owner == Victim ? 1 : 0), "Takeover changed an unrelated epoch.");
        if (wedge is not null)
        {
            var escalation = Decode<WatchdogSample>((await WaitSignalAsync("watchdog")).Detail);
            Require(escalation.Reason.Contains("the commit call", StringComparison.Ordinal) && escalation.CancelMs >= 0 &&
                    escalation.EndMs >= escalation.CancelMs && escalation.EndMs <= WatchdogBoundMs + ObservationAllowanceMs,
                "The product watchdog did not cancel and then escalate the locked worker commit within its bound.");
            await WaitAsync(async () => !await BackendExistsAsync(peer.Backend, "anchor-victim"));
            Log("watchdog", escalation);
        }
        Log("takeover", new { start, epochBefore = original.Epoch, epochAfter = taken[target.Partition].Epoch, moved = Share,
            transaction = deaths[0].Transaction, firstMs = deaths.Min(row => row.At) - start, lastMs = deaths.Max(row => row.At) - start });

        // Give the stale epoch the CURRENT position. Matching position alone must never let it write.
        var current = (await OwnersAsync())[target.Partition];
        Require(await store.FenceAsync([new(current.Id, original.Epoch, current.Position, current.Position)], stop) is null,
            "The old owner epoch accepted a physical fence with the current position.");
        await SendAsync("approval-1", "replacement", Contract.AdmissionOutcome.Duplicate);
        if (scenario == "crash") await SendAsync(Second, "Grace", Contract.AdmissionOutcome.Accepted);
        var finished = await WaitImageAsync(store, reference, 3);
        var results = Json.Read<NativeState>(finished.State!).Results["approve"]!.AsArray().Select(item => item!.GetValue<string>());
        Require(finished.Status == InstanceStatus.Finished && results.SequenceEqual(["Ada", "Grace"]),
            "The exact approval did not settle on the survivor after takeover.");
        var complete = await foundation.TailAsync(scope, reference, cancellationToken: stop);
        Require(complete.Items.Select(item => item.Revision).Distinct().SequenceEqual([1L, 2L, 3L]) &&
            complete.Items.Take(prefix.Items.Count).SequenceEqual(prefix.Items) &&
            complete.Items.Count(item => PostgresPublicPreview.Decode(item) == "approved:" + Second) == 1,
            "The takeover must preserve the original prefix and one second approval.");
        var positions = await QueryAsync<long>($"""
            SELECT "Position" AS "Value" FROM "Journal" WHERE "Instance" = {target.Instance} ORDER BY "Position"
            """);
        Require(positions.SequenceEqual([1L, 2L, 3L]), "The exact journal does not have three gapless positions.");
        await SendAsync(Second, "replacement", Contract.AdmissionOutcome.Duplicate);

        await WriteSignalAsync("revive", Encode(peer));
        Log("revive", new { run, scenario, local });
        var restarted = Decode<Identity>((await WaitSignalAsync("victim-revived")).Detail);
        Require(IsRevival(peer, restarted), "The revived owner must connect again from the same victim host and boot.");
        var restored = await WaitForOwnersAsync(IsSplit);
        var recoveryGain = restored.Where(row => row.Owner == Victim).ToArray();
        foreach (var row in restored)
            Require(row.Epoch == taken[row.Id].Epoch + (row.Owner == Victim ? 1 : 0), "Revival changed an unrelated epoch.");
        var revival = (await ChangesAsync()).Where(row => row.At > deaths.Max(death => death.At)).ToArray();
        CheckSet(revival.Where(row => row.Previous == Survivor && row.Owner is null), recoveryGain, "revival give-up");
        CheckSet(revival.Where(row => row.Previous is null && row.Owner == Victim), recoveryGain, "revival gain", died: false);
        await SendAsync(Second, "another replacement", Contract.AdmissionOutcome.Duplicate);
        Require((await foundation.TailAsync(scope, reference, cancellationToken: stop)).Items.SequenceEqual(complete.Items),
            "Exact replay after revival wrote another journal entry.");
        await WriteSignalAsync("stop-revived", "complete");
        await WaitSignalAsync("revived-stopped");
        await owner.StopAsync(stop);
        Require(owner.LastError is null, "The survivor recorded a runtime error.");
        Log("passed", new { run, scenario, topology = local ? "local-two-process-smoke" : "distinct-machines", target,
            revived = restarted, positions, staleFenceRefused = true, snapshotReplayed = true });

        Task SendAsync(string id, string input, Contract.AdmissionOutcome expected) => PostgresMultiRootClient.Send(foundation,
            scope, reference, id, Json.Write(new CompositeSignal(frame, "approve", input)), expected, stop);
    }

    private async Task RunVictimAsync(bool revived)
    {
        var configuration = await WaitSignalAsync("configuration");
        Require(configuration.Detail == $"{run:N}/{scenario}/{local}", "The survivor initialized this database for a different run.");
        var expected = Decode<Identity>((await WaitSignalAsync(revived ? "revive" : "start-victim")).Detail);
        await using var anchor = await OpenAnchorAsync(revived ? "revived" : "victim");
        var identity = await ReadIdentityAsync(anchor, revived ? "revived" : "victim");
        Require(revived ? IsRevival(expected, identity) : CompatibleIdentity(expected, identity),
            "The owner role does not match the declared topology and binaries.");
        var wedge = !revived && scenario == "wedge" ? new CommitWedge(this) : null;
        var ownerOptions = wedge is null ? _options : new DbContextOptionsBuilder<KernelDb>(_options).AddInterceptors(wedge).Options;
        KernelDb OpenOwner() => PostgresPreviewHosting.CreateShippedContext(ownerOptions);
        var requests = PostgresPreviewHosting.CreateRequests(OpenOwner, Victim, WorkflowActor.WorkflowActor.ActorType);
        await using var owner = await StartOwnerAsync(OpenOwner, Victim, requests,
            wedge is null ? UnexpectedWatchdog : wedge.Escalate, stop);
        await WriteSignalAsync(revived ? "victim-revived" : "victim-started", Encode(identity));
        if (revived)
        {
            await WaitSignalAsync("stop-revived");
            await owner.StopAsync(stop);
            Require(owner.LastError is null, "The revived owner recorded a runtime error.");
            await WriteSignalAsync("revived-stopped", "complete");
        }
        else if (wedge is not null)
        {
            wedge.Arm(Decode<Target>((await WaitSignalAsync("arm")).Detail));
            await WriteSignalAsync("armed", "ready");
            await Task.Delay(Timeout.InfiniteTimeSpan, stop);
        }
        else
        {
            await WaitSignalAsync("crash");
            Process.GetCurrentProcess().Kill();
            throw new InvalidOperationException("The disposable victim did not terminate.");
        }
    }

    private async Task<Target> SelectTargetAsync(PostgresPreviewFoundation foundation, Contract.BoundScope scope, Partition[] split)
    {
        var workflow = new WorkflowRuntime(foundation, foundation);
        var admission = new PostgresRootAdmission(Open, TimeSpan.FromHours(1));
        for (var i = 0; i < 32; i++)
        {
            var receipt = await workflow.StartAsync(scope, $"k12-{i}", NativeExecutionModel.Start(DirectApprovalWorkflow.Definition, Modes.Durable),
                cancellationToken: stop);
            Require(receipt.Outcome == Contract.AdmissionOutcome.Accepted && receipt.Actor is not null, "Expected an admitted root.");
            var reference = receipt.Actor!.Value;
            var address = (await admission.ResolveAsync(Tenant, reference.Id, stop))!;
            var actor = new InstanceRef(address.Id);
            if (split[actor.Partition].Owner == Victim) return new(reference.Id, actor.Id, actor.Partition);
        }
        throw new InvalidOperationException("No initially victim-owned root was generated.");
    }

    private async Task<EfStore.RootImage> WaitImageAsync(EfStore store, Contract.ActorRef reference, long revision)
    {
        EfStore.RootImage? image = null;
        await WaitAsync(async () =>
        {
            image = await store.ReadFirstRootAsync(Tenant, reference.Id, stop);
            Require(image is null || (image.Revision <= revision && image.Status != InstanceStatus.Faulted), "Unexpected root image.");
            return image?.Revision == revision;
        });
        return image!;
    }

    private static Task<RuntimeNode> StartOwnerAsync(Func<KernelDb> open, string node, PostgresDirectRequests requests,
        Action<string> endProcess, CancellationToken token) => PostgresPreviewHosting.StartAsync(open, node,
        new WorkflowActor.WorkflowActor([new NativeExecutionModel(new WorkflowRegistry().Add(DirectApprovalWorkflow.Build()))]),
        endProcess, cancellationToken: token, requests: requests);

    private static void UnexpectedWatchdog(string message)
    {
        Log("unexpected-watchdog", message);
        Environment.Exit(1);
    }

    private static bool IsSplit(Partition[] rows) => rows.Count(row => row.Owner == Survivor) == Share && rows.Count(row => row.Owner == Victim) == Share;

    private async Task<Partition[]> WaitForOwnersAsync(Func<Partition[], bool> match, long? before = null)
    {
        Partition[] rows = [];
        await WaitAsync(async () =>
        {
            rows = await OwnersAsync();
            if (before is { } limit)
            {
                await using var db = Open();
                var now = await db.Database.SqlQueryRaw<long>($"SELECT {Clock} AS \"Value\"").SingleAsync(stop);
                Require(now <= limit, "The expected ownership set was not observed within the provider-clock bound.");
            }
            return match(rows);
        });
        return rows;
    }

    private async Task<Partition[]> OwnersAsync()
    {
        var rows = await QueryAsync<Partition>($"""SELECT "Id", "Owner", "Epoch", "Position" FROM "Partitions" ORDER BY "Id" """);
        Require(rows.Count == Partitions && rows.Select(row => row.Id).SequenceEqual(Enumerable.Range(0, Partitions)),
            "The database does not have the full set of 1,024 physical fence rows.");
        return [.. rows];
    }

    private Task<List<Transition>> ChangesAsync() => QueryAsync<Transition>($"SELECT * FROM k12_ownership_changes");

    private static void CheckSet(IEnumerable<Transition> changes, Partition[] expected, string phase, bool? died = null)
    {
        var material = changes.OrderBy(row => row.Partition).ToArray();
        Require(material.Select(row => row.Partition).SequenceEqual(expected.Select(row => row.Id)) &&
            material.All(row => row.Epoch == row.OldEpoch + (row.Owner is null ? 0 : 1) && (died is null || row.Died == died)) &&
            material.Select(row => row.Transaction).Distinct().Count() == 1,
            $"Expected exactly one PostgreSQL transaction for the full {phase} set.");
    }

    private Task<bool> IsHeldAsync(int backend, int partition) => QuerySingleAsync<bool>($"""
        SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_stat_activity WHERE pid = {backend} AND state = 'idle in transaction')
        AND NOT EXISTS (SELECT 1 FROM "Partitions" WHERE "Id" = {partition} FOR UPDATE SKIP LOCKED) AS "Value"
        """);

    private Task<bool> BackendExistsAsync(int backend, string tag) => QuerySingleAsync<bool>($"""
        SELECT EXISTS (SELECT 1 FROM pg_catalog.pg_stat_activity WHERE pid = {backend} AND application_name = {ApplicationName(tag)}) AS "Value"
        """);

    private async Task WaitAsync(Func<Task<bool>> condition)
    {
        while (!await condition()) await Task.Delay(100, stop);
    }

    private async Task<Signal> WaitSignalAsync(string name)
    {
        Signal? signal = null;
        await WaitAsync(async () =>
        {
            signal = (await QueryAsync<Signal>($"""SELECT * FROM k12_signals WHERE "Name" = {name} """)).SingleOrDefault();
            return signal is not null;
        });
        return signal!;
    }

    private async Task WriteSignalAsync(string name, string detail, CancellationToken? cancellationToken = null)
    {
        await using var db = Open();
        await db.Database.ExecuteSqlInterpolatedAsync($"""INSERT INTO k12_signals ("Name", "Detail") VALUES ({name}, {detail})""",
            cancellationToken ?? stop);
    }

    private async Task<List<T>> QueryAsync<T>(FormattableString sql)
    {
        await using var db = Open();
        return await db.Database.SqlQuery<T>(sql).ToListAsync(stop);
    }

    private async Task<T> QuerySingleAsync<T>(FormattableString sql) => (await QueryAsync<T>(sql)).Single();

    private string ApplicationName(string tag) => $"k12-{run:N}-{tag}";

    private async Task<NpgsqlConnection> OpenAnchorAsync(string tag)
    {
        var source = new NpgsqlConnectionStringBuilder(connection) { ApplicationName = ApplicationName("anchor-" + tag), Pooling = false };
        var anchor = new NpgsqlConnection(source.ConnectionString);
        try { await anchor.OpenAsync(stop); return anchor; }
        catch { await anchor.DisposeAsync(); throw; }
    }

    private async Task<Identity> ReadIdentityAsync(NpgsqlConnection anchor, string role)
    {
        using var command = new NpgsqlCommand("SELECT pg_backend_pid(), host(inet_client_addr())", anchor);
        await using var reader = await command.ExecuteReaderAsync(stop);
        await reader.ReadAsync(stop);
        Require(!reader.IsDBNull(1), "Use a TCP connection to the dedicated shared PostgreSQL database.");
        var (machine, boot) = HostIdentity();
        var binaries = new[]
        {
            typeof(PostgresNetworkOwnershipChecks), typeof(PostgresPreviewHosting), typeof(EfStore),
            typeof(RuntimeNode), typeof(Worker), typeof(WorkflowActor.WorkflowActor), typeof(NativeExecutionModel)
        }.Select(type => type.Assembly).Distinct();
        return new(role, Environment.ProcessId, reader.GetInt32(0), reader.GetString(1), Hash(machine), Hash(boot),
            $"{RuntimeInformation.OSDescription} / {RuntimeInformation.OSArchitecture}", string.Join('/', binaries.Select(assembly =>
            {
                using var binary = File.OpenRead(assembly.Location);
                return Convert.ToHexString(SHA256.HashData(binary));
            })));
    }

    private bool CompatibleIdentity(Identity first, Identity second) => first.Backend != second.Backend && first.Binaries == second.Binaries &&
        (local ? first.Machine == second.Machine && first.Boot == second.Boot && first.Process != second.Process
            : first.Machine != second.Machine && first.Boot != second.Boot);

    private static bool IsRevival(Identity first, Identity second) => first.Machine == second.Machine && first.Boot == second.Boot &&
        first.Binaries == second.Binaries && first.Backend != second.Backend;

    private string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{run:N}/{value.Trim()}")));

    private static (string Machine, string Boot) HostIdentity()
    {
        if (OperatingSystem.IsLinux())
            return (File.ReadAllText("/etc/machine-id"), File.ReadAllText("/proc/sys/kernel/random/boot_id"));
        if (OperatingSystem.IsMacOS())
        {
            var machine = Output("/usr/sbin/ioreg", "-rd1", "-c", "IOPlatformExpertDevice").Split('\n')
                .Single(line => line.Contains("\"IOPlatformUUID\"", StringComparison.Ordinal)).Split('"')[3];
            return (machine, Output("/usr/sbin/sysctl", "-n", "kern.bootsessionuuid"));
        }
        throw new PlatformNotSupportedException("The two-host qualification supports Linux and macOS owners.");

        static string Output(string executable, params string[] args)
        {
            var info = new ProcessStartInfo(executable) { RedirectStandardOutput = true };
            foreach (var arg in args) info.ArgumentList.Add(arg);
            using var child = Process.Start(info) ?? throw new InvalidOperationException("Cannot read the operating system host identity.");
            var text = child.StandardOutput.ReadToEnd();
            child.WaitForExit();
            Require(child.ExitCode == 0 && !string.IsNullOrWhiteSpace(text), "Cannot read the operating system host identity.");
            return text;
        }
    }

    private static string Encode<T>(T data) => JsonSerializer.Serialize(data);
    private static T Decode<T>(string data) => JsonSerializer.Deserialize<T>(data) ?? throw new InvalidOperationException("Missing fixture signal data.");
    private static void Log<T>(string kind, T data) => Console.WriteLine(Encode(new { kind, data }));
    private static void Require(bool pass, string error) { if (!pass) throw new InvalidOperationException(error); }

    private sealed class CommitWedge(PostgresNetworkOwnershipChecks fixture) : DbTransactionInterceptor
    {
        private readonly TaskCompletionSource _never = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private Target? _target;
        private long _entry;
        private long _cancel = -1;

        internal void Arm(Target target) => Volatile.Write(ref _target, target);

        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Volatile.Read(ref _target) is not { } target) return result;
            using var command = (NpgsqlCommand)transaction.Connection!.CreateCommand();
            command.Transaction = (NpgsqlTransaction)transaction;
            command.CommandText = """SELECT "Revision" FROM "Instances" WHERE "Id" = @actor AND xmin = pg_current_xact_id()::xid""";
            command.Parameters.AddWithValue("actor", target.Instance);
            if (await command.ExecuteScalarAsync(cancellationToken) is not 3L || Interlocked.Exchange(ref _target, null) is null)
                return result;
            Volatile.Write(ref _entry, Stopwatch.GetTimestamp());
            using var observeCancellation = cancellationToken.Register(() => Volatile.Write(ref _cancel, ElapsedMs()));
            var backend = command.Connection!.ProcessID;
            var sample = (await fixture.QueryAsync<WedgeSample>($"""
                SELECT {backend} AS "Backend", 3::bigint AS "Revision", "SeenAt", "LeaseUntil" FROM "Nodes" WHERE "Id" = {Victim}
                """)).Single();
            await fixture.WriteSignalAsync("intercepted", Encode(sample));
            // Never complete and deliberately ignore the owner's cancellation. Its genuine pre-COMMIT transaction holds the fence.
            await _never.Task;
            return result;
        }

        internal void Escalate(string reason)
        {
            var sample = new WatchdogSample(reason, Volatile.Read(ref _cancel), ElapsedMs());
            Log("watchdog-exit", sample);
            try
            {
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                fixture.WriteSignalAsync("watchdog", Encode(sample), budget.Token).GetAwaiter().GetResult();
            }
            finally { Environment.Exit(WatchdogExitCode); }
        }

        private long ElapsedMs() => (long)Stopwatch.GetElapsedTime(Volatile.Read(ref _entry)).TotalMilliseconds;
    }

    private const string SetupSql = """
        CREATE TABLE k12_signals (
            "Name" text PRIMARY KEY, "Detail" text NOT NULL,
            "At" bigint NOT NULL DEFAULT floor(extract(epoch from pg_catalog.clock_timestamp()) * 1000)::bigint);
        CREATE TABLE k12_ownership_changes (
            "Partition" int NOT NULL, "Previous" text, "OldEpoch" bigint NOT NULL, "Owner" text, "Epoch" bigint NOT NULL,
            "Died" bool NOT NULL, "LeaseUntil" bigint, "At" bigint NOT NULL, "Transaction" text NOT NULL);
        CREATE FUNCTION k12_observe_owner() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN
            INSERT INTO k12_ownership_changes VALUES (NEW."Id", OLD."Owner", OLD."Epoch", NEW."Owner", NEW."Epoch", NEW."Died",
                (SELECT "LeaseUntil" FROM "Nodes" WHERE "Id" = OLD."Owner"),
                floor(extract(epoch from pg_catalog.clock_timestamp()) * 1000)::bigint, pg_current_xact_id()::text);
            RETURN NEW;
        END $$;
        CREATE TRIGGER k12_observe_owner AFTER UPDATE OF "Owner" ON "Partitions"
            FOR EACH ROW WHEN (OLD."Owner" IS DISTINCT FROM NEW."Owner") EXECUTE FUNCTION k12_observe_owner();
        """;
}
