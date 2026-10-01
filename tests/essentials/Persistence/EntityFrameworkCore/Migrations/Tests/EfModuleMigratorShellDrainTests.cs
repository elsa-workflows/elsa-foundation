using System.Collections.Concurrent;
using System.Data.Common;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore;
using Elsa.Studio.Preferences.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// A shell's <see cref="EfModuleMigrator{TContext}"/> stops with the shell's drain, while the shell's services are still
/// usable (#2236). The container's disposal marks its provider disposed before it reaches the migrator, so a backfill round
/// that was still running then took its next scope from a dead provider and warned that it had failed and could not
/// release its claim, on every shutdown that caught a round in flight.
/// </summary>
public sealed class EfModuleMigratorShellDrainTests : IAsyncLifetime
{
    private const string ShellName = "migrator-drain";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-migrator-drain-{Guid.NewGuid():N}.db");
    private readonly FakeTimeProvider _clock = new();
    private readonly ParkedRound _round = new();
    private readonly CapturedWarnings _warnings = new();
    private ServiceProvider _root = null!;

    public async Task InitializeAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(_warnings));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        // The backfill's rounds start when the test advances this clock, so one is parked in its scope when the test says.
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<ProviderDisposal>();
        // Only the backfill's rounds open the module's context while the test runs.
        services.Configure<EfSchemaFinalizationOptions>(options =>
        {
            options.EvaluationInterval = TimeSpan.FromDays(1);
            options.RefreshInterval = TimeSpan.FromDays(1);
        });
        services.ConfigureDbContext<StudioPreferencesSqliteDbContext>(builder => builder.AddInterceptors(_round));
        services.AddCShells(builder => builder
            .WithAssemblies(typeof(MigratorDrainFeature).Assembly)
            .AddShell(ShellName, shell => shell
                .WithFeature(MigratorDrainFeature.FeatureName, feature => feature.WithSetting(nameof(MigratorDrainFeature.ConnectionString), $"Data Source={_file}"))));
        _root = services.BuildServiceProvider();
        await Registry.GetOrActivateAsync(ShellName);
    }

    public async Task DisposeAsync()
    {
        _round.Release();
        await _root.DisposeAsync();
        SqliteConnection.ClearAllPools();
        foreach (var file in new[] { _file, _file + "-journal", _file + "-wal", _file + "-shm" })
            File.Delete(file);
    }

    [Fact]
    public async Task Draining_a_shell_stops_a_backfill_round_inside_its_scope_before_the_provider_is_disposed_and_warns_of_nothing()
    {
        var shell = await Registry.GetOrActivateAsync(ShellName);
        // Created after the migrator, so the container disposes it before the migrator: the point where the provider is
        // already marked disposed and the migrator has not yet stopped.
        var disposal = shell.ServiceProvider.GetRequiredService<ProviderDisposal>();
        disposal.Round = _round;
        _round.Arm();
        await ParkARoundAsync();

        var drain = await Registry.DrainAsync(shell);
        await drain.WaitAsync().WaitAsync(Patience);

        Assert.True(disposal.Disposed);
        Assert.True(_round.Stopped, "The drain did not stop the round that was running inside its scope.");
        Assert.False(disposal.RoundWasRunning, "The round was still running when the shell's provider was disposed.");
        Assert.Empty(_warnings.Persistence);
    }

    /// <summary>
    /// What <see cref="StudioPreferencesEntityFrameworkCoreFeature"/> composes, a real module's context, store and migrator, without
    /// the feature that module's API needs.
    /// </summary>
    [ShellFeature(FeatureName)]
    public sealed class MigratorDrainFeature : IShellFeature
    {
        public const string FeatureName = "MigratorDrain";

        public string? ConnectionString { get; set; }

        public void ConfigureServices(IServiceCollection services) =>
            services
                .AddStudioPreferencesEntityFrameworkCore(new StudioPreferencesEntityFrameworkCoreOptions { Provider = "Sqlite", ConnectionString = ConnectionString })
                .AddEfModuleMigrations<StudioPreferencesDbContext>("Sqlite");
    }

    private IShellRegistry Registry => _root.GetRequiredService<IShellRegistry>();

    /// <summary>Starts the backfill's first round and waits for it to be inside its scope.</summary>
    private async Task ParkARoundAsync()
    {
        // The loop registers its delay on the clock from a task of its own, so the clock is advanced until the round starts.
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!_round.Parked.IsCompleted)
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, "No backfill round started.");
            _clock.Advance(TimeSpan.FromSeconds(15));
            await Task.Delay(TimeSpan.FromMilliseconds(10));
        }
    }

    /// <summary>Holds the first connection opening after it is armed, which is the backfill round's first read, until released or cancelled.</summary>
    private sealed class ParkedRound : DbConnectionInterceptor
    {
        private readonly TaskCompletionSource _parked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private volatile bool _armed;
        private volatile bool _inRound;
        private volatile bool _stopped;

        public Task Parked => _parked.Task;

        /// <summary>Whether a round is parked in its scope now.</summary>
        public bool InRound => _inRound;

        /// <summary>Whether the park ended because the round's token was cancelled.</summary>
        public bool Stopped => _stopped;

        public void Arm() => _armed = true;

        public void Release() => _released.TrySetResult();

        public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection,
            ConnectionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (!_armed)
                return result;
            _armed = false;
            _inRound = true;
            _parked.TrySetResult();
            try
            {
                await _released.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                _stopped = true;
                throw;
            }
            finally
            {
                _inRound = false;
            }

            return result;
        }
    }

    /// <summary>
    /// What the shell's container finds when it disposes: whether the round was still in its scope. It then lets the round go, so
    /// a container that disposed it with the round still parked does not wait for it.
    /// </summary>
    private sealed class ProviderDisposal : IAsyncDisposable
    {
        public ParkedRound? Round { get; set; }

        public bool Disposed { get; private set; }

        public bool RoundWasRunning { get; private set; }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            RoundWasRunning = Round?.InRound == true;
            Round?.Release();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CapturedWarnings : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _warnings = new();

        /// <summary>The warnings the persistence layer logged, with the exception each carried.</summary>
        public IReadOnlyCollection<string> Persistence => _warnings;

        public ILogger CreateLogger(string categoryName) => new Sink(categoryName, _warnings);

        public void Dispose()
        {
        }

        private sealed class Sink(string category, ConcurrentQueue<string> warnings) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel >= LogLevel.Warning && category.StartsWith("Elsa.Persistence.EntityFramework", StringComparison.Ordinal))
                    warnings.Enqueue($"{category}: {formatter(state, exception)} {exception}");
            }
        }
    }
}
