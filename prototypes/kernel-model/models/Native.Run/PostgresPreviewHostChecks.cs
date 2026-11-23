using System.Data.Common;
using Kernel.EntityFramework;
using Kernel.Foundation.Postgres;
using Kernel.Runtime.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Native.Run;

internal static class PostgresPreviewHostChecks
{
    internal static async Task<int> RunAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(55));
        var stop = deadline.Token;
        var connection = Environment.GetEnvironmentVariable("KERNEL_PREVIEW_POSTGRES")
            ?? throw new InvalidOperationException("Use a dedicated PostgreSQL fixture database.");
        var actor = new ProbeActor("external-actor");
        foreach (var (name, type, timing, parameter) in new (string, ProbeActor, LoopOptions?, string)[]
        {
            ("bad\0id", actor, null, "node"),
            ("bad\ud800id", actor, null, "node"),
            ("preview", new(" "), null, "actorType"),
            ("preview", new("bad\0type"), null, "actorType"),
            ("preview", actor, new() { BalanceEvery = TimeSpan.Zero }, "options"),
            ("preview", actor, new() { PollInterval = TimeSpan.FromTicks(1) }, "options"),
            ("preview", actor, new() { Window = TimeSpan.Zero }, "options")
        })
        {
            try
            {
                await using var invalid = await PostgresPreviewHosting.StartAsync(
                    () => throw new InvalidOperationException("Invalid host configuration opened the database."),
                    name, type, Environment.FailFast, timing, stop);
                throw new InvalidOperationException("Invalid host configuration started a node.");
            }
            catch (ArgumentException error) when (error.ParamName == parameter)
            {
                Console.WriteLine($"ok invalid {parameter} refused before opening the database");
            }
        }

        var data = new DbContextOptionsBuilder<KernelDb>().UseNpgsql(connection).Options;
        var store = new EfStore(() => new(data), postgres: true);
        await store.CreateAsync(1);
        await store.InstallPreviewRootSchemaAsync(stop);
        var ingress = await store.OpenPreviewRootIngressAsync(actor.Type, TimeSpan.FromHours(1), stop);
        var reads = await store.OpenPreviewRootReadsAsync(cancellationToken: stop);
        var scope = FixedTenantScope.Bind("preview-host");
        var admitted = await ingress.SendAsync(scope, new Contract.Target.Start(new(actor.Type, "fixture")),
            "start-external", Contract.Kinds.Start, cancellationToken: stop);
        if (admitted is not { Outcome: Contract.AdmissionOutcome.Accepted, Actor: { } root })
            throw new InvalidOperationException("The public start must succeed before launching the host.");

        var hang = new StalledHeartbeat();
        var watched = new DbContextOptionsBuilder<KernelDb>(data).AddInterceptors(hang).Options;
        var escalated = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var runtime = await PostgresPreviewHosting.StartAsync(() => new(watched), "preview-host",
            actor, message => escalated.TrySetResult(message), new() { PollInterval = TimeSpan.FromMilliseconds(25) }, stop);
        try
        {
            while (await reads.GetAsync(scope, root, stop) is not { Header.Status: Contract.ActorStatus.Finished })
            {
                if (runtime.LastError is { } error) throw new InvalidOperationException("The host failed before serving the actor.", error);
                await Task.Delay(25, stop);
            }
            if (actor.Turns != 1 || (await reads.TailAsync(scope, root, cancellationToken: stop)).Items.Count == 0)
                throw new InvalidOperationException("The launcher must serve the public actor and journal its one terminal turn.");
            Console.WriteLine("ok the launcher serves a plain public actor with a terminal checkpoint and journal");

            hang.Arm();
            await hang.Entered.Task.WaitAsync(stop);
            var message = await escalated.Task.WaitAsync(stop);
            if (!message.Contains("heartbeat", StringComparison.Ordinal) || hang.Released.Task.IsCompleted)
                throw new InvalidOperationException("The watchdog did not escalate the stalled owner heartbeat.");
            Console.WriteLine("ok the host watchdog invokes the supplied escalation policy for an entered heartbeat");
        }
        finally
        {
            // This fixture records the process-termination policy and releases the synthetic call to leave normally.
            hang.Released.TrySetResult();
        }
        await runtime.StopAsync(stop);
        return 0;
    }

    private sealed class StalledHeartbeat : DbCommandInterceptor
    {
        private int _armed;
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("""INSERT INTO "Nodes" """, StringComparison.Ordinal) &&
                Interlocked.Exchange(ref _armed, 0) == 1)
            {
                Entered.TrySetResult();
                await Released.Task;
            }
            return result;
        }
    }

    private sealed class ProbeActor(string type) : Contract.IActorType
    {
        public string Type => type;
        public int Turns => Volatile.Read(ref _turns);
        private int _turns;

        public bool Withholds(Contract.ActorHeader header) => false;
        public ValueTask TurnAsync(Contract.IActorTurn turn, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _turns);
            turn.End(Contract.ActorStatus.Finished);
            return ValueTask.CompletedTask;
        }
        public ValueTask<object?> ExecuteJobAsync(Contract.IJob job, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Contract.RestorePlan RestoreWaits(Contract.ActorHeader header, string state) => throw new NotSupportedException();
        public long TerminalGrowth(Contract.ActorHeader header, string state) => 0;
        public bool ProtectsDeletion(Contract.ActorHeader header, string state, long now) => false;
    }
}
