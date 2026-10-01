using System.Reflection;
using Elsa.Workbench;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OpenIddict.Abstractions;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Every access and refresh token is a row of Workbench's OpenIddict store, and nothing else deletes one. The prune removes the
/// expired and redeemed entries, tokens and authorizations, once they are older than the configured age, on every node.
/// </summary>
public sealed class WorkbenchOpenIddictPruningTests : IAsyncLifetime
{
    private static readonly TimeSpan Interval = new WorkbenchOpenIddictPruningOptions().Interval;

    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("elsa-workbench-openiddict-prune-");
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly CapturingLogger _log = new();
    private ServiceProvider _provider = null!;
    private OpenIddictPruneScenario _scenario = null!;

    public async Task InitializeAsync()
    {
        _provider = CreateProvider(Durable());
        await WorkbenchOpenIddictTestHost.StartAsync(_provider);
        _scenario = new OpenIddictPruneScenario(_provider, _time);
    }

    public async Task DisposeAsync()
    {
        await _provider.DisposeAsync();
        SqliteConnection.ClearAllPools();
        _directory.Delete(recursive: true);
    }

    [Fact]
    public async Task A_prune_removes_expired_and_redeemed_entries_and_keeps_the_rest()
    {
        var entries = await _scenario.SeedAsync();

        await Service().PruneAsync(CancellationToken.None);

        Assert.Equal(entries.Kept, await _scenario.RemainingAsync(entries.All));
    }

    /// <summary>An entry is only pruned once it is older than the configured age: a fresh expired or redeemed one is left to be recognised.</summary>
    [Fact]
    public async Task A_longer_minimum_age_keeps_entries_that_would_otherwise_be_pruned()
    {
        await using var patient = CreateProvider(Durable(("MinimumAge", "60.00:00:00")));
        var entries = await _scenario.SeedAsync();

        await Service(patient).PruneAsync(CancellationToken.None);

        Assert.Equal(entries.All.Order(), await _scenario.RemainingAsync(entries.All));
    }

    [Fact]
    public async Task The_prune_runs_at_start_and_then_on_the_interval()
    {
        var first = await _scenario.SeedAsync();
        var service = Service();

        await service.StartAsync(CancellationToken.None);
        await WaitUntilAsync(async () => (await _scenario.RemainingAsync(first.All)).SequenceEqual(first.Kept));

        // Seeded after the start's prune, so only the prune the interval brings can remove it.
        var second = await _scenario.SeedAsync();
        _time.Advance(Interval);
        await WaitUntilAsync(async () => (await _scenario.RemainingAsync(second.All)).SequenceEqual(second.Kept));
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_disabled_prune_never_runs()
    {
        await using var disabled = CreateProvider(Durable(("Enabled", "false")));
        var entries = await _scenario.SeedAsync();
        var service = Service(disabled);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(30));
        _time.Advance(Interval * 2);

        Assert.Equal(entries.All.Order(), await _scenario.RemainingAsync(entries.All));
        await service.StopAsync(CancellationToken.None);
    }

    /// <summary>
    /// Every node prunes, none claims it, so two prunes of one store at once, as two nodes' would be, both finish and leave the store
    /// pruned: what one has already deleted is not an error to the other.
    /// </summary>
    [Fact]
    public async Task Nodes_pruning_the_same_store_at_once_leave_it_pruned()
    {
        await using var otherNode = CreateProvider(Durable());
        var entries = await _scenario.SeedAsync(extraExpired: 150);

        await Task.WhenAll(Service().PruneAsync(CancellationToken.None), Service(otherNode).PruneAsync(CancellationToken.None));
        await Service().PruneAsync(CancellationToken.None);

        Assert.Equal(entries.Kept, await _scenario.RemainingAsync(entries.All));
    }

    /// <summary>A prune that cannot run, here against a store nobody has migrated, is reported and tried again, never thrown into the host.</summary>
    [Fact]
    public async Task A_failed_prune_is_logged_and_does_not_stop_the_host()
    {
        var unmigrated = Directory.CreateTempSubdirectory("elsa-workbench-openiddict-unmigrated-");
        try
        {
            await using var provider = CreateProvider(WorkbenchOpenIddictTestHost.DurableConfiguration(Path.Join(unmigrated.FullName, "tokens.db"), autoMigrate: false));

            await Service(provider).PruneAsync(CancellationToken.None);

            Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Pruning the OpenIddict tokens failed", StringComparison.Ordinal));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            unmigrated.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A prune call that hangs is cancelled when its timeout passes, and the prune goes on to the next call, so one hung call cannot
    /// hold every later prune of the node.
    /// </summary>
    [Fact]
    public async Task A_prune_call_that_hangs_is_cancelled_after_the_timeout_and_the_next_call_still_runs()
    {
        await using var node = CreateProvider(
            Durable(("Timeout", "00:01:00")),
            services => services.AddScoped(_ => DispatchProxy.Create<IOpenIddictTokenManager, HangingPrune>()));

        var entries = await _scenario.SeedAsync();

        var prune = Service(node).PruneAsync(CancellationToken.None);
        Assert.False(prune.IsCompleted);
        _time.Advance(TimeSpan.FromMinutes(1));
        await prune.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Contains(_log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Pruning the OpenIddict tokens did not finish in 00:01:00", StringComparison.Ordinal));
        // The tokens' prune hung and was cancelled, and the authorizations' ran in the same cycle: its entry is gone, and the token is not.
        Assert.Empty(await _scenario.RemainingAsync([entries.PrunedAuthorization]));
        Assert.Equal([entries.PrunedToken], await _scenario.RemainingAsync([entries.PrunedToken]));
    }

    [Theory]
    [InlineData("Interval", "00:00:00")]
    [InlineData("MinimumAge", "-1.00:00:00")]
    [InlineData("Timeout", "00:00:00")]
    public void A_setting_that_cannot_work_fails_the_host_at_start(string key, string value)
    {
        using var provider = CreateProvider(Durable((key, value)));

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<WorkbenchOpenIddictPruningOptions>>().Value);
    }

    [Fact]
    public void The_defaults_prune_every_hour_what_is_two_weeks_old()
    {
        var defaults = new WorkbenchOpenIddictPruningOptions();

        Assert.True(defaults.Enabled);
        Assert.Equal(TimeSpan.FromHours(1), defaults.Interval);
        Assert.Equal(TimeSpan.FromDays(14), defaults.MinimumAge);
        Assert.Equal(TimeSpan.FromMinutes(10), defaults.Timeout);
    }

    /// <summary>The durable store every node of the test shares, with the prune settings given.</summary>
    private IConfiguration Durable(params (string Key, string Value)[] prune) =>
        new ConfigurationBuilder()
            .AddConfiguration(WorkbenchOpenIddictTestHost.DurableConfiguration(Path.Join(_directory.FullName, "tokens.db"), autoMigrate: true))
            .AddInMemoryCollection(prune.Select(setting => KeyValuePair.Create<string, string?>($"{WorkbenchOpenIddictPruningOptions.SectionPath}:{setting.Key}", setting.Value)))
            .Build();

    /// <summary>A node: the store the host runs, and the prune registered beside it the way <c>Program.cs</c> registers it, on the test's clock and log.</summary>
    private ServiceProvider CreateProvider(IConfiguration configuration, Action<IServiceCollection>? replace = null) =>
        WorkbenchOpenIddictTestHost.CreateProvider(configuration, withMigrationPolicy: true, services =>
        {
            services.AddSingleton<TimeProvider>(_time);
            services.AddSingleton<ILoggerFactory>(_log);
            services.AddWorkbenchOpenIddictPruning(configuration);
            replace?.Invoke(services);
        });

    private WorkbenchOpenIddictPruningService Service(IServiceProvider? provider = null) =>
        (provider ?? _provider).GetServices<IHostedService>().OfType<WorkbenchOpenIddictPruningService>().Single();

    private static async Task WaitUntilAsync(Func<Task<bool>> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!await condition())
            await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
    }
}

/// <summary>A token manager whose prune never finishes until it is cancelled, and which answers nothing else.</summary>
public class HangingPrune : DispatchProxy
{
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name == nameof(IOpenIddictTokenManager.PruneAsync)
            ? new ValueTask<long>(HangAsync((CancellationToken)args![1]!))
            : throw new NotSupportedException(targetMethod?.Name);

    private static async Task<long> HangAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        return 0;
    }
}
