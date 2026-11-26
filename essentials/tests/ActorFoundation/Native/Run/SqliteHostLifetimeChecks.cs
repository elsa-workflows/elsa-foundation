using System.Diagnostics;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Foundation.Sqlite;
using Kernel.Runtime;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Kernel.Native.Run;

internal static partial class SqliteHostLifetimeChecks
{
    private const string Model = "sqlite-host-lifetime";
    private static readonly InstanceRef Slow = InstanceRef.In(0, "slow"), Sibling = InstanceRef.In(0, "sibling");
    private static readonly LoopOptions Loops = new()
    {
        PollInterval = TimeSpan.FromMilliseconds(25),
        BalanceEvery = TimeSpan.FromMilliseconds(100),
        Window = TimeSpan.FromSeconds(2)
    };

    internal static async Task<int> RunAsync(bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(150));
        var token = budget.Token;
        var directory = Directory.CreateTempSubdirectory(Model);
        try
        {
            var path = Path.Combine(directory.FullName, "kernel.db");
            var store = Store(path, fenceInBatch);
            await store.CreateAsync(1);
            await using (var db = Open(path))
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TRIGGER fixture_release_after_checkpoint BEFORE UPDATE OF "Owner" ON "Partitions"
                    WHEN OLD."Owner" = 'first' AND NEW."Owner" IS NULL
                      AND NOT EXISTS (SELECT 1 FROM "Instances" WHERE "Id" = '0/slow' AND "Revision" = 1)
                    BEGIN SELECT RAISE(ABORT, 'ownership released before the checkpoint committed'); END
                    """, token);
            await AdmitStartAsync(store, Slow, token);
            await AdmitStartAsync(store, Sibling, token);
            var actor = new WaitingActor();
            await using var first = Service(path, "first", actor, fenceInBatch);
            await using var replacement = Service(path, "replacement", new WaitingActor(acceptStart: false), fenceInBatch);
            await first.StartAsync(token);
            await RequireRefusedAsync(replacement, token);
            await RunPeerAsync("refused", path, fenceInBatch, token);
            if (!OperatingSystem.IsWindows())
            {
                var alias = Path.Combine(directory.FullName, "alias.db");
                File.CreateSymbolicLink(alias, path);
                await RunPeerAsync("refused", alias, fenceInBatch, token);
            }

            Task? stopping = null;
            try
            {
                await Task.WhenAll(actor.SlowEntered.Task, actor.SiblingEntered.Task).WaitAsync(token);
                var (owner, epoch) = await FenceAsync(path, token);
                var lease = await LeaseAsync(path, "first", token);
                stopping = first.StopAsync(token);
                Require(owner == "first" && !stopping.IsCompleted && await ReadAsync(store, Slow, token) is null,
                    "stop waits while the entered turn owns its fence");
                actor.ReleaseSibling.TrySetResult();
                await WaitForRevisionAsync(store, Sibling, 1, token);
                Require(!stopping.IsCompleted && await ReadAsync(store, Slow, token) is null,
                    "an entered sibling commits while the slower turn is still waiting");

                using var joined = new CancellationTokenSource();
                var joining = first.StopAsync(joined.Token);
                joined.Cancel();
                await RequireCancellationAsync(joining, joined.Token, token);
                await Task.Delay(Defaults.Lease, token);
                Require(!stopping.IsCompleted && await FenceAsync(path, token) == ("first", epoch) &&
                    await LeaseAsync(path, "first", token) > lease,
                    "the owning stop renews its lease past the original deadline, even after a joining stop times out");

                actor.ReleaseSlow.TrySetResult();
                await stopping.WaitAsync(token);
                Require(await FenceAsync(path, token) == (null, epoch) &&
                    await ReadAsync(store, Slow, token) is { Revision: 1, State: "\"waiting\"" } && first.LastError is null,
                    "the entered turn commits before ownership is released at the same epoch");

                await replacement.StartAsync(token);
                Require(await store.AddAsync(new("complete", Slow, "complete"), token), "the continuation is admitted");
                await WaitForRevisionAsync(store, Slow, 2, token);
                var (nextOwner, nextEpoch) = await FenceAsync(path, token);
                Require(nextOwner == "replacement" && nextEpoch > epoch &&
                    await ReadAsync(store, Slow, token) is { Status: InstanceStatus.Finished },
                    "the replacement retries startup and continues from the saved state under a newer fence");
                await replacement.DisposeAsync();
                Require(await FenceAsync(path, token) == (null, nextEpoch) && replacement.LastError is null,
                    "disposing an idle host releases ownership cleanly");
            }
            finally
            {
                actor.ReleaseSlow.TrySetResult();
                actor.ReleaseSibling.TrySetResult();
                if (stopping is not null) await stopping.WaitAsync(token);
            }

            await RunPeerAsync("start", path, fenceInBatch, token);

            foreach (var mode in new[] { "kill", "dispose", "fault", "stuck" })
            {
                var deadlinePath = Path.Combine(directory.FullName, mode + ".db");
                await Store(deadlinePath, fenceInBatch).CreateAsync(1);
                await RunPeerAsync(mode, deadlinePath, fenceInBatch, token);
                await RunPeerAsync("recover", deadlinePath, fenceInBatch, token);
            }
            return 0;
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    internal static async Task<int> RunPeerAsync(string mode, string path, bool fenceInBatch)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var token = budget.Token;
        if (mode is "foundation-stuck" or "foundation-recover")
            return await RunFoundationPeerAsync(path, fenceInBatch, mode == "foundation-recover", token);
        if (mode == "foundation-definition-drift")
            return await CheckFoundationDefinitionDriftAsync(path, fenceInBatch, token);

        var actor = new WaitingActor(recovering: mode == "recover", blocks: mode == "stuck");
        var log = new LifetimeLog();
        var escalation = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var owner = mode == "recover" ? "successor" : "peer";
        await using var service = Service(path, owner, actor, fenceInBatch, log,
            mode == "dispose" ? TimeSpan.FromMilliseconds(400) : null, reason => escalation.TrySetResult(reason));
        if (mode == "refused")
        {
            await RequireRefusedAsync(service, token);
            try
            {
                await SqliteEmbeddedHosting.OpenFoundationAsync(path, "lifetime", cancellationToken: token);
            }
            catch (SqliteHostLockException)
            {
                Require(true, "a second process also cannot open ingress while the runtime holds the file");
                return 0;
            }
            throw new InvalidOperationException("The second process opened SQLite ingress while the runtime held its file.");
        }

        var store = Store(path, fenceInBatch);
        if (mode == "recover")
        {
            var prior = await FenceAsync(path, token);
            await service.StartAsync(token);
            await WaitForRevisionAsync(store, Slow, 1, token);
            var after = await FenceAsync(path, token);
            Require(after.Owner == owner && after.Epoch > prior.Epoch &&
                await ReadAsync(store, Slow, token) is { State: "\"replayed\"" },
                "a new process replays the pending delivery under a newer fence");
            await service.StopAsync(token);
            return 0;
        }

        await service.StartAsync(token);
        if (mode == "start")
        {
            await service.StopAsync(token);
            return 0;
        }

        await AdmitStartAsync(store, Slow, token);
        await actor.SlowEntered.Task.WaitAsync(token);
        var fence = await FenceAsync(path, token);
        using var application = new CancellationTokenSource();
        try
        {
            if (mode is "kill" or "stuck")
            {
                var stopping = service.StopAsync(application.Token);
                application.Cancel();
                await RequireCancellationAsync(stopping, application.Token, token);
                if (mode == "stuck")
                    Require(escalation.Task.IsCompletedSuccessfully,
                        "a stopped process whose actor stays blocked past the grace invokes its process policy");
            }
            else if (mode == "dispose")
            {
                // Dispose has its own deadline and must kill a stop already under way.
                var stopping = service.StopAsync(CancellationToken.None);
                await service.DisposeAsync().AsTask().WaitAsync(token);
                await stopping.WaitAsync(token);
            }
            else
            {
                var disposing = service.DisposeAsync().AsTask();
                await using var db = Open(path);
                await db.Database.ExecuteSqlRawAsync("""
                    UPDATE "Partitions" SET "Owner" = 'fence-thief', "Epoch" = "Epoch" + 1
                    """, token);
                await disposing.WaitAsync(token);
                await service.DisposeAsync().AsTask().WaitAsync(token);
                Require(service.LastError is ScopeOwnershipLostException && log.OwnershipErrors == 1 && log.Kills == 0,
                    "disposing after a lost fence records one Error and returns twice without throwing");
                await CheckAbandonedTurnAsync();
                return 0;
            }

            Require(await FenceAsync(path, token) == fence && fence.Owner == "peer" &&
                await ReadAsync(store, Slow, token) is null && log.Kills == 1 && service.LastError is null,
                $"the {mode} deadline warns once, leaves the fence to expire and discards the incomplete turn");
            if (mode != "stuck") Require(!escalation.Task.IsCompleted, "a prompt kill needs no process escalation");
            await CheckAbandonedTurnAsync();
        }
        finally
        {
            actor.ReleaseSlow.TrySetResult();
        }
        return 0;

        async Task CheckAbandonedTurnAsync()
        {
            actor.ReleaseSlow.TrySetResult();
            await actor.SlowReturned.Task.WaitAsync(token);
            await RunPeerAsync("refused", path, fenceInBatch, token);
            Require(await ReadAsync(store, Slow, token) is null, "returning from an abandoned turn leaves its checkpoint unwritten");
        }
    }

    private static SqliteEmbeddedHostedService Service(string path, string node, WaitingActor actor, bool fenceInBatch,
        ILogger<SqliteEmbeddedHostedService>? log = null, TimeSpan? disposeDeadline = null, Action<string>? endProcess = null) =>
        new(path, node, actor, endProcess ?? (reason => throw new InvalidOperationException(reason)),
            Loops with { DisposeDeadline = disposeDeadline ?? TimeSpan.FromSeconds(15) }, fenceInBatch, log);

    private static async Task RunPeerAsync(string mode, string path, bool fenceInBatch, CancellationToken token)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
        start.ArgumentList.Add(typeof(SqliteHostLifetimeChecks).Assembly.Location);
        start.ArgumentList.Add("sqlite-host-lifetime-peer");
        start.ArgumentList.Add(mode);
        start.ArgumentList.Add(path);
        start.ArgumentList.Add(fenceInBatch ? "in-batch" : "checked");
        if (mode == "refused" && !OperatingSystem.IsWindows())
            start.Environment["DOTNET_SYSTEM_IO_DISABLEFILELOCKING"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start the SQLite host probe.");
        try
        {
            await process.WaitForExitAsync(token);
            Require(process.ExitCode == 0, $"the {mode} host probe passes in a separate process");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    private static async Task RequireRefusedAsync(SqliteEmbeddedHostedService service, CancellationToken token)
    {
        try { await service.StartAsync(token).WaitAsync(token); }
        catch (SqliteHostLockException)
        {
            Console.WriteLine("ok a second host is refused while the SQLite file is locked");
            return;
        }
        throw new InvalidOperationException("A second host opened the SQLite file.");
    }

    private static async Task RequireCancellationAsync(Task operation, CancellationToken deadline, CancellationToken token)
    {
        try { await operation.WaitAsync(token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested && deadline.IsCancellationRequested) { return; }
        throw new InvalidOperationException("The deadline did not cancel the stop.");
    }

    private static KernelDb Open(string path) => new(new DbContextOptionsBuilder<KernelDb>()
        .UseSqlite(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()).Options);

    private static EfStore Store(string path, bool fenceInBatch) => new(() => Open(path), postgres: false, fenceInBatch);

    private static async Task AdmitStartAsync(EfStore store, InstanceRef actor, CancellationToken token) =>
        Require(await store.AddAsync(new("start-" + actor.Id, actor, Kinds.Start, Start: new(Model, Model)), token),
            $"the {actor.Id} turn is admitted");

    private static async Task<(string? Owner, long Epoch)> FenceAsync(string path, CancellationToken token)
    {
        await using var db = Open(path);
        var row = await db.Partitions.AsNoTracking().SingleAsync(token);
        return (row.Owner, row.Epoch);
    }

    private static async Task<long> LeaseAsync(string path, string owner, CancellationToken token)
    {
        await using var db = Open(path);
        return await db.Nodes.AsNoTracking().Where(n => n.Id == owner).Select(n => n.LeaseUntil).SingleAsync(token);
    }

    private static async Task<InstanceRow?> ReadAsync(EfStore store, InstanceRef actor, CancellationToken token) =>
        (await store.LoadAsync([actor.Id], token)).SingleOrDefault();

    private static async Task WaitForRevisionAsync(EfStore store, InstanceRef actor, long revision, CancellationToken token)
    {
        while ((await ReadAsync(store, actor, token))?.Revision != revision) await Task.Delay(25, token);
    }

    private static void Require(bool condition, string witness)
    {
        if (!condition) throw new InvalidOperationException(witness);
        Console.WriteLine($"ok {witness}");
    }

    private sealed class LifetimeLog : ILogger<SqliteEmbeddedHostedService>
    {
        private int _kills, _ownershipErrors;
        internal int Kills => Volatile.Read(ref _kills);
        internal int OwnershipErrors => Volatile.Read(ref _ownershipErrors);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? error, Func<TState, Exception?, string> format)
        {
            if (level == LogLevel.Warning && error is null)
                Interlocked.Increment(ref _kills);
            if (level == LogLevel.Error && error is ScopeOwnershipLostException)
                Interlocked.Increment(ref _ownershipErrors);
        }
    }

    private sealed class WaitingActor(bool acceptStart = true, bool recovering = false, bool blocks = false) : IActorType
    {
        internal TaskCompletionSource SlowEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SiblingEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource SlowReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSlow { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource ReleaseSibling { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Withholds(InstanceRow row, ITurnServices services) => false;
        public TurnOutcome? Command(TurnInput input) => null;
        public TurnOutcome Park(InstanceRow? row, string actor, IReadOnlyList<Delivery> taken, StartSpec? spec, string error) =>
            throw new InvalidOperationException(error);

        public ValueTask<object?> ExecuteJobAsync(string model, IJob job, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async ValueTask<TurnOutcome> TurnAsync(TurnInput input)
        {
            var turn = new DurableTurn(input);
            var message = turn.Inbox.Single();
            InstanceRow row;
            if (input.Row is null)
            {
                if (!acceptStart || message.Kind != Kinds.Start) throw new InvalidOperationException("Unexpected creator turn.");
                var sibling = turn.Actor == Sibling;
                (sibling ? SiblingEntered : SlowEntered).TrySetResult();
                var release = (sibling ? ReleaseSibling : ReleaseSlow).Task;
                if (blocks) release.GetAwaiter().GetResult();
                else if (!recovering) await release;
                row = new(input.Actor, Model, Model, InstanceStatus.Running, recovering ? "\"replayed\"" : "\"waiting\"",
                    turn.Revision, null);
            }
            else
            {
                if (message.Kind != "complete" || input.Row.State != "\"waiting\"")
                    throw new InvalidOperationException("The continuation requires the previous host's checkpoint.");
                row = input.Row with { Status = InstanceStatus.Finished, Revision = turn.Revision };
            }
            var outcome = turn.Seal(turn.Outcome(row.Status), row, row.State, Json.Write(new[] { message.Id }));
            if (turn.Actor == Slow) SlowReturned.TrySetResult();
            return outcome;
        }
    }
}
