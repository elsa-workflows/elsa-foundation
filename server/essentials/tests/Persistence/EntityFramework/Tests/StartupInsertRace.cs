using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Forces the interleaving behind #2162, without leaving it to timing: several hosts that start together each read a
/// startup row, find it missing, and only then insert it (<c>EfInsertIfAbsent</c>). Every context built through
/// <see cref="Configure"/> is held at the command that inserts into each named table until all <c>parties</c> of them are
/// at it, so every one of them has read "missing" and then all of them send the insert at once, and all but one must
/// lose. The statement itself is left to the database, which is what the race is about.
/// </summary>
/// <remarks>
/// It also records what EF logged at error level or above while the race ran, so a test can assert that the loser logged
/// nothing. The interceptor and the logger factory are shared by the whole process, and route to the race the calling
/// test began, because EF keeps one internal service provider per distinct set of them and stops at twenty; a race per
/// module would otherwise exhaust it. Call <see cref="Begin"/> from the test's own async method, so what it starts
/// flows to the contexts the test runs. <see cref="Record"/> is the same without any hold, for a test whose contexts
/// race unaided.
/// </remarks>
internal sealed class StartupInsertRace
{
    private static readonly AsyncLocal<StartupInsertRace?> Active = new();
    private static readonly HoldInserts Hold = new();
    private static readonly ILoggerFactory Loggers = LoggerFactory.Create(builder => builder.AddProvider(new RecordingProvider()).SetMinimumLevel(LogLevel.Error));
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly int _parties;
    private readonly Dictionary<string, Rendezvous> _tables;
    private readonly ConcurrentDictionary<(Guid Context, string Table), byte> _held = new();
    private readonly ConcurrentQueue<string> _errors = new();

    private StartupInsertRace(int parties, string[] tables)
    {
        _parties = parties;
        _tables = tables.ToDictionary(table => table, _ => new Rendezvous(), StringComparer.Ordinal);
    }

    /// <summary>Starts a race between <paramref name="parties"/> contexts that each insert into every one of <paramref name="tables"/>, named exactly.</summary>
    public static StartupInsertRace Begin(int parties, params string[] tables) => Active.Value = new StartupInsertRace(parties, tables);

    /// <summary>Starts recording what EF logs for the contexts this test runs, and holds none of them.</summary>
    public static StartupInsertRace Record() => Begin(0);

    /// <summary>What EF logged at error level or above while the race ran.</summary>
    public IReadOnlyList<string> Errors => [.. _errors];

    /// <summary>How many contexts were held at their insert into <paramref name="table"/>.</summary>
    public int HeldAt(string table) => _held.Keys.Count(key => key.Table == table);

    /// <summary>Whether every context was held at its insert into every table, and so had read before any of them wrote.</summary>
    public bool EveryContextWasHeldBeforeItsInsert => _tables.Keys.All(table => HeldAt(table) == _parties);

    /// <summary>Adds the hold and the log recording to a context's options.</summary>
    public void Configure(DbContextOptionsBuilder builder) => builder.UseLoggerFactory(Loggers).AddInterceptors(Hold);

    private async Task HoldAsync(Guid context, DbCommand command, CancellationToken cancellationToken)
    {
        if (FinalizationInsert.TableOf(command) is not { } table || !_tables.TryGetValue(table, out var rendezvous) || !_held.TryAdd((context, table), 0))
            return;

        await rendezvous.ArriveAsync(_parties, cancellationToken);
    }

    private sealed class Rendezvous
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public async Task ArriveAsync(int parties, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _arrived) == parties)
                _released.TrySetResult();
            await _released.Task.WaitAsync(Patience, cancellationToken);
        }
    }

    private sealed class HoldInserts : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Active.Value is { } race && eventData.Context is { } context)
                await race.HoldAsync(context.ContextId.InstanceId, command, cancellationToken);
            return result;
        }
    }

    private sealed class RecordingProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new Recorder(categoryName);

        public void Dispose()
        {
        }

        private sealed class Recorder(string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (IsEnabled(logLevel))
                    Active.Value?._errors.Enqueue($"{logLevel} {category}: {formatter(state, exception)}");
            }
        }
    }
}
