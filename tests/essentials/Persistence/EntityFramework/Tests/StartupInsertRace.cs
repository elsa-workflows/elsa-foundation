using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Forces the interleaving behind #2162, without leaving it to timing: several hosts that start together each read a
/// startup row, find it missing, and only then insert it. Every context built through <see cref="Configure"/> is held at
/// its first read of each named table until all <c>parties</c> of them have reached it, so by the time any of them
/// writes, every one of them has read "missing", and all but one insert must lose.
/// </summary>
/// <remarks>
/// It also records what EF logged while the race ran, so a test can assert that the loser logged nothing at error
/// level. The interceptor and the logger factory are shared by the whole process, and route to the race the calling
/// test began, because EF keeps one internal service provider per distinct set of them and stops at twenty; a race per
/// module would otherwise exhaust it. Call <see cref="Begin"/> from the test's own async method, so what it starts
/// flows to the contexts the test runs.
/// </remarks>
internal sealed class StartupInsertRace
{
    private static readonly AsyncLocal<StartupInsertRace?> Active = new();
    private static readonly HoldFirstReads Hold = new();
    private static readonly ILoggerFactory Loggers = LoggerFactory.Create(builder => builder.AddProvider(new RecordingProvider()).SetMinimumLevel(LogLevel.Trace));
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly int _parties;
    private readonly Dictionary<string, Rendezvous> _tables;
    private readonly ConcurrentDictionary<(Guid Context, string Table), byte> _held = new();
    private readonly ConcurrentQueue<(LogLevel Level, string Text)> _entries = new();

    private StartupInsertRace(int parties, string[] tables)
    {
        _parties = parties;
        _tables = tables.ToDictionary(table => table, _ => new Rendezvous(), StringComparer.Ordinal);
    }

    /// <summary>Starts a race between <paramref name="parties"/> contexts that each first read every one of <paramref name="tables"/>.</summary>
    public static StartupInsertRace Begin(int parties, params string[] tables) => Active.Value = new StartupInsertRace(parties, tables);

    /// <summary>What EF logged at error level or above while the race ran.</summary>
    public IReadOnlyList<string> Errors => [.. _entries.Where(entry => entry.Level >= LogLevel.Error).Select(entry => entry.Text)];

    /// <summary>How many contexts were held at their first read of <paramref name="table"/>.</summary>
    public int HeldAt(string table) => _held.Keys.Count(key => key.Table == table);

    /// <summary>Whether every context was held at its first read of every table, and so read before any of them wrote.</summary>
    public bool EveryContextWasHeldAtEveryTable => _tables.Keys.All(table => HeldAt(table) == _parties);

    /// <summary>Adds the hold and the log recording to a context's options.</summary>
    public void Configure(DbContextOptionsBuilder builder) => builder.UseLoggerFactory(Loggers).AddInterceptors(Hold);

    private async Task HoldAsync(Guid context, string commandText, CancellationToken cancellationToken)
    {
        if (!commandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            _tables.Keys.FirstOrDefault(table => commandText.Contains(table, StringComparison.Ordinal)) is not { } name ||
            !_held.TryAdd((context, name), 0))
            return;

        await _tables[name].ArriveAsync(_parties, cancellationToken);
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

    private sealed class HoldFirstReads : DbCommandInterceptor
    {
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (Active.Value is { } race && eventData.Context is { } context)
                await race.HoldAsync(context.ContextId.InstanceId, command.CommandText, cancellationToken);
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

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                Active.Value?._entries.Enqueue((logLevel, $"{logLevel} {category}: {formatter(state, exception)}"));
        }
    }
}
