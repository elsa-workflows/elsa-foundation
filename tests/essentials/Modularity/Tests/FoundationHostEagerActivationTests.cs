using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Attention.Core;
using Elsa.Foundation.Host.Health;
using Elsa.Foundation.Host.Shells;
using Elsa.Persistence.Schema;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// <c>Elsa.Foundation.Host</c>'s eager shell activation when an attempt fails: a database that is not up yet, a seeder that
/// raced a peer, an EF module's refusal. The shell is retried in the background on a capped, doubling, jittered delay until it
/// activates, with no restart; each failure is logged; <c>/health/ready</c> says that it is not active and why, in a code and
/// nothing that names the failure; and an Attention item, behind a permission, carries the detail while the shell is not
/// active, by whatever path it ends up active. The delays run on a fake clock, so the backoff is asserted exactly rather than
/// waited for.
/// </summary>
public sealed class FoundationHostEagerActivationTests : IAsyncDisposable
{
    private const string Shell = "default";
    private const string Secret = "Host=db;Password=hunter2";
    private static readonly TimeSpan Second = TimeSpan.FromSeconds(1);

    private readonly ObservableTimeProvider _time = new();
    private readonly ShellActivationTracker _tracker;
    private readonly ShellActivationAttentionContributor _attention;
    private readonly ScriptedShellRegistry _registry = new();
    private readonly CapturingLogger _logger = new();
    private readonly EagerShellActivationHostedService _service;
    private WebApplication? _app;

    public FoundationHostEagerActivationTests()
    {
        _tracker = new ShellActivationTracker(_time);
        _attention = new ShellActivationAttentionContributor(_tracker);
        _service = new EagerShellActivationHostedService(_registry, Configuration("00:00:01", "00:00:04"), _tracker, _logger.CreateLogger<EagerShellActivationHostedService>());
    }

    public async ValueTask DisposeAsync()
    {
        _registry.Hang?.TrySetResult();
        await _service.StopAsync(CancellationToken.None);
        _service.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public async Task A_shell_whose_first_activation_fails_is_retried_on_a_doubling_delay_capped_at_the_maximum_and_activates_without_a_restart()
    {
        _registry.Script(Fault(), Fault(), Fault(), Fault(), null);

        await _service.StartAsync(CancellationToken.None);

        Assert.Equal(1, _tracker.FailureOf(Shell)!.Attempts);
        AssertDelay(Second);
        foreach (var (attempts, step) in new[] { (2, 2 * Second), (3, 4 * Second), (4, 4 * Second) })
        {
            await AdvanceToNextAttemptAsync();
            await WaitUntilAsync(() => _tracker.FailureOf(Shell)?.Attempts == attempts);
            AssertDelay(step);
        }

        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _tracker.FailureOf(Shell) is null);
        Assert.Equal(5, _registry.Attempts);
        Assert.Empty(_tracker.Failing());
    }

    [Fact]
    public async Task Every_failed_attempt_is_logged_with_its_number_and_the_delay_to_the_next()
    {
        _registry.Script(Fault(), Fault(), null);

        await _service.StartAsync(CancellationToken.None);
        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _tracker.FailureOf(Shell)?.Attempts == 2);
        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _tracker.FailureOf(Shell) is null);

        var warnings = _logger.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message).ToArray();
        Assert.Equal(2, warnings.Length);
        Assert.Contains("attempt 1", warnings[0], StringComparison.Ordinal);
        Assert.Contains("attempt 2", warnings[1], StringComparison.Ordinal);
        Assert.All(warnings, warning => Assert.Contains("trying again in 00:00:", warning, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_shell_that_activates_at_the_first_attempt_is_never_recorded_and_starts_no_retry()
    {
        _registry.Script((Exception?)null);

        await _service.StartAsync(CancellationToken.None);

        Assert.Empty(_tracker.Failing());
        Assert.Equal(0, _time.Timers);
    }

    /// <summary>
    /// A refusal is a decision an operator resolves, not a fault: retrying sooner cannot change it, so it is checked back at the
    /// slowest interval and no earlier, and the shell activates at that check once the operator has acted.
    /// </summary>
    [Fact]
    public async Task An_EF_modules_refusal_is_checked_again_only_at_the_maximum_delay_and_activates_once_the_operator_has_acted()
    {
        _registry.Script(new PendingRefusal(), null);

        await _service.StartAsync(CancellationToken.None);

        var failure = _tracker.FailureOf(Shell)!;
        AssertDelay(4 * Second);
        Assert.Equal(("Orders", IEfModuleRefusal.PendingMigrationsCode), (failure.Refusal!.Module, failure.Refusal.Code));
        Assert.Equal(["20260930_One"], failure.Refusal.PendingMigrations);
        // The jitter takes at most a fifth off the cap, so three seconds of a four-second cap is always too soon.
        _time.Advance(3 * Second);
        await Task.Delay(50);
        Assert.Equal(1, _registry.Attempts);

        _time.Advance(Second);
        await WaitUntilAsync(() => _tracker.FailureOf(Shell) is null);
        Assert.Equal(2, _registry.Attempts);
    }

    [Fact]
    public async Task Stopping_the_host_ends_the_retries()
    {
        _registry.Script(Fault());

        await _service.StartAsync(CancellationToken.None);
        await _service.StopAsync(CancellationToken.None);
        _time.Advance(10 * Second);
        await Task.Delay(50);

        Assert.Equal(1, _registry.Attempts);
    }

    [Fact]
    public async Task Stopping_the_host_waits_for_an_activation_that_ignores_its_cancellation_no_longer_than_the_hosts_token_allows()
    {
        _registry.Script(Fault());
        await _service.StartAsync(CancellationToken.None);
        _registry.Hang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _registry.Attempts == 2);

        using var shutdown = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.StopAsync(shutdown.Token));
    }

    [Fact]
    public async Task Readiness_says_that_a_shell_is_not_active_and_how_it_is_retried_without_naming_the_failure()
    {
        _registry.Script(Fault(Secret));
        await _service.StartAsync(CancellationToken.None);
        using var client = await HealthClientAsync();

        var (status, body) = await ReadyAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        var reason = Assert.Single(body.GetProperty("shells").EnumerateArray()).GetProperty("reason");
        Assert.Equal(ShellNotActiveReasonCodes.ActivationFailed, reason.GetProperty("code").GetString());
        Assert.Equal(1, reason.GetProperty("attempts").GetInt32());
        Assert.Equal(_tracker.FailureOf(Shell)!.NextAttemptAt, reason.GetProperty("nextAttemptAt").GetDateTimeOffset());
        Assert.Equal(["attempts", "code", "nextAttemptAt"], reason.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(InvalidOperationException), body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_of_a_refusal_says_so_by_its_code_alone_and_names_neither_the_module_nor_its_migrations_nor_the_command()
    {
        _registry.Script(new PendingRefusal());
        await _service.StartAsync(CancellationToken.None);
        using var client = await HealthClientAsync();

        var (_, body) = await ReadyAsync(client);

        var reason = Assert.Single(body.GetProperty("shells").EnumerateArray()).GetProperty("reason");
        Assert.Equal(ShellNotActiveReasonCodes.ActivationRefused, reason.GetProperty("code").GetString());
        Assert.Equal(["attempts", "code", "nextAttemptAt"], reason.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        foreach (var detail in new[] { "Orders", "20260930_One", "persistence apply", nameof(PendingRefusal) })
            Assert.DoesNotContain(detail, body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_of_a_shell_no_attempt_has_failed_says_it_is_not_activated()
    {
        using var client = await HealthClientAsync();

        var (status, body) = await ReadyAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal(ShellNotActiveReasonCodes.NotActivated, Assert.Single(body.GetProperty("shells").EnumerateArray()).GetProperty("reason").GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_attention_item_stands_for_a_shell_that_is_not_active_and_goes_when_it_activates()
    {
        _registry.Script(Fault(Secret), null);
        await _service.StartAsync(CancellationToken.None);

        var item = Assert.Single((await _attention.EvaluateAsync(Context())).Items);

        Assert.Equal(($"shell-activation:{Shell}", AttentionSeverity.Warning, 1), (item.Id, item.Severity, item.Count));
        Assert.Equal([new AttentionCorrelation("shell", Shell)], item.Correlations);
        Assert.Contains(nameof(InvalidOperationException), item.Summary, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", item.Summary, StringComparison.Ordinal);
        Assert.Equal(AttentionSensitivity.Metadata, item.Sensitivity);

        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _tracker.FailureOf(Shell) is null);

        Assert.Empty((await _attention.EvaluateAsync(Context())).Items);
    }

    [Fact]
    public async Task An_attention_item_for_a_refusal_is_critical_and_names_the_module_and_its_migrations()
    {
        _registry.Script(new PendingRefusal());
        await _service.StartAsync(CancellationToken.None);

        var item = Assert.Single((await _attention.EvaluateAsync(Context())).Items);

        Assert.Equal(AttentionSeverity.Critical, item.Severity);
        Assert.Contains(new AttentionCorrelation("module", "Orders"), item.Correlations);
        Assert.Contains("20260930_One", item.Summary, StringComparison.Ordinal);
        Assert.Contains("operator", item.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void The_attention_contributor_asks_for_the_module_management_read_permission()
    {
        Assert.Equal("module-management.read", _attention.Descriptor.RequiredPermission);
    }

    /// <summary>
    /// The item must not say "not active" of a shell that is serving. A request, or a reload, can activate it between two
    /// retries, and then nothing but the registry's own notification, or its answer, says so.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_shell_a_request_activates_between_two_retries_is_no_longer_reported(bool registryNotifiesLifecycle)
    {
        _registry.Script(Fault());
        await _service.StartAsync(CancellationToken.None);
        Assert.Single((await _attention.EvaluateAsync(Context())).Items);

        await _registry.BecomeActiveAsync(Shell, notify: registryNotifiesLifecycle);

        Assert.Empty((await _attention.EvaluateAsync(Context())).Items);
        Assert.Empty(_tracker.Failing());
        Assert.Null(_tracker.FailureOf(Shell));
    }

    [Fact]
    public async Task A_shell_a_reload_activates_is_forgotten_even_though_the_retry_has_not_run()
    {
        _registry.Script(Fault());
        await _service.StartAsync(CancellationToken.None);

        await _registry.BecomeActiveAsync(Shell, notify: true);
        // The registry stops holding it active (a later reload fails, say): it was forgotten when it became active, so it is not back.
        _registry.Deactivate(Shell);

        Assert.Empty(_tracker.Failing());
    }

    [Theory]
    [InlineData(null, null, "00:00:01", "00:01:00")]
    [InlineData("00:00:02", "00:00:30", "00:00:02", "00:00:30")]
    [InlineData("nonsense", "-00:00:05", "00:00:01", "00:01:00")]
    [InlineData("00:02:00", "00:00:30", "00:02:00", "00:02:00")]
    public void Retry_options_fall_back_to_the_defaults_and_never_cap_below_the_first_delay(string? initial, string? max, string expectedInitial, string expectedMax)
    {
        var options = EagerShellActivationRetryOptions.Read(Configuration(initial, max));

        Assert.Equal((TimeSpan.Parse(expectedInitial), TimeSpan.Parse(expectedMax)), (options.InitialDelay, options.MaxDelay));
    }

    [Fact]
    public void The_delay_doubles_to_the_cap_and_does_not_overflow_for_a_shell_that_stays_down()
    {
        var options = new EagerShellActivationRetryOptions { InitialDelay = Second, MaxDelay = TimeSpan.FromMinutes(1) };

        Assert.Equal([1, 2, 4, 8, 16, 32, 60, 60], Enumerable.Range(1, 8).Select(failed => (int)options.DelayAfter(failed, refused: false).TotalSeconds));
        Assert.Equal(TimeSpan.FromMinutes(1), options.DelayAfter(int.MaxValue, refused: false));
    }

    [Fact]
    public void Jitter_only_takes_up_to_a_fifth_off_a_step_so_the_cap_still_holds_and_replicas_spread()
    {
        var options = new EagerShellActivationRetryOptions { InitialDelay = Second, MaxDelay = TimeSpan.FromSeconds(10) };

        Assert.Equal(10 * Second, EagerShellActivationRetryOptions.Jitter(10 * Second, 0));
        Assert.InRange(EagerShellActivationRetryOptions.Jitter(10 * Second, 1), TimeSpan.FromSeconds(7.999), TimeSpan.FromSeconds(8.001));
        var delays = Enumerable.Range(0, 200).Select(_ => options.NextDelay(failedAttempts: 40, refused: false)).ToArray();
        Assert.All(delays, delay => Assert.InRange(delay, TimeSpan.FromSeconds(8), 10 * Second));
        Assert.True(delays.Distinct().Count() > 1, "The retries of replicas that failed together must not all fall on the same instant.");
    }

    /// <summary>The delay the last failure recorded is the step less up to a fifth of it, and the clock the delay runs on is the one that stamped it.</summary>
    private void AssertDelay(TimeSpan step) => Assert.InRange(_tracker.FailureOf(Shell)!.RetryDelay, step * 0.8, step);

    /// <summary>
    /// Waits for the retry loop to be sleeping on its next delay, and runs it out. A timer is created the moment the loop starts
    /// to sleep, so advancing before then would move the clock past a delay that has not begun.
    /// </summary>
    private async Task AdvanceToNextAttemptAsync()
    {
        var failure = _tracker.FailureOf(Shell)!;
        await WaitUntilAsync(() => _time.Timers > _time.Consumed);
        _time.Consumed++;
        _time.Advance(failure.NextAttemptAt - _time.GetUtcNow());
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
        {
            Assert.False(timeout.IsCancellationRequested, "The retry loop did not reach the state the test waits for.");
            await Task.Delay(5);
        }
    }

    private static InvalidOperationException Fault(string message = "The database is not reachable yet.") => new(message);

    private static IConfiguration Configuration(string? initialDelay = null, string? maxDelay = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"CShells:Shells:{Shell}:Name"] = Shell,
            [$"{EagerShellActivationRetryOptions.SectionKey}:{nameof(EagerShellActivationRetryOptions.InitialDelay)}"] = initialDelay,
            [$"{EagerShellActivationRetryOptions.SectionKey}:{nameof(EagerShellActivationRetryOptions.MaxDelay)}"] = maxDelay
        }).Build();

    private static AttentionContributorContext Context() => new(new(new ClaimsPrincipal(), null), new(1), new Dictionary<string, string>());

    private async Task<HttpClient> HealthClientAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddConfiguration(Configuration());
        builder.Services.AddSingleton<IShellRegistry>(_registry);
        builder.Services.AddSingleton(_tracker);
        _app = builder.Build();
        _app.MapHostHealth();
        await _app.StartAsync();
        return _app.GetTestClient();
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body)> ReadyAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/health/ready");
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    /// <summary>A fake clock that counts the timers it is asked for, one per delay the retry loop sleeps.</summary>
    private sealed class ObservableTimeProvider : FakeTimeProvider
    {
        private int _timers;

        public int Timers => Volatile.Read(ref _timers);

        /// <summary>How many of those the test has run out.</summary>
        public int Consumed { get; set; }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Interlocked.Increment(ref _timers);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    /// <summary>
    /// Answers <see cref="GetOrActivateAsync"/> with what a test scripts, an exception to throw or <see langword="null"/> to succeed,
    /// the last entry repeating, or never when <see cref="Hang"/> is set. A shell becomes active when a test says a request or a
    /// reload activated it, with the lifecycle notification the real registry sends, or without it.
    /// </summary>
    private sealed class ScriptedShellRegistry : IShellRegistry
    {
        private readonly Dictionary<string, StubShell> _active = [];
        private readonly List<IShellLifecycleSubscriber> _subscribers = [];
        private Exception?[] _script = [];

        public int Attempts { get; private set; }

        public TaskCompletionSource? Hang { get; set; }

        public void Script(params Exception?[] script) => _script = script;

        public async Task BecomeActiveAsync(string name, bool notify)
        {
            var shell = _active[name] = new StubShell(name);
            if (notify)
                foreach (var subscriber in _subscribers)
                    await subscriber.OnStateChangedAsync(shell, ShellLifecycleState.Initializing, ShellLifecycleState.Active);
        }

        public void Deactivate(string name) => _active.Remove(name);

        public async Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default)
        {
            var step = _script[Math.Min(Attempts++, _script.Length - 1)];
            if (Hang is { } hang)
                await hang.Task;
            return step is null ? null! : throw step;
        }

        public void Subscribe(IShellLifecycleSubscriber subscriber) => _subscribers.Add(subscriber);
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) => _subscribers.Remove(subscriber);
        public IShell? GetActive(string name) => _active.GetValueOrDefault(name);

        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
    }

    /// <summary>An active generation of a shell, as far as the tracker reads one: its name and its state.</summary>
    private sealed class StubShell(string name) : IShell
    {
        public ShellDescriptor Descriptor { get; } = ShellDescriptor.Create(name, 1);
        public ShellLifecycleState State => ShellLifecycleState.Active;
        public IServiceProvider ServiceProvider => throw new NotSupportedException();
        public IShellScope BeginScope() => throw new NotSupportedException();
        public IDrainOperation? Drain => null;
    }

    /// <summary>An EF module's refusal as this host meets it: a type of the module's own, known here only by the shared interface.</summary>
    private sealed class PendingRefusal() : InvalidOperationException("EF module 'Orders' has pending migrations: 20260930_One."), IEfModuleRefusal
    {
        public string Module => "Orders";

        public string Code => IEfModuleRefusal.PendingMigrationsCode;

        public IReadOnlyList<string> PendingMigrations => ["20260930_One"];

        public string? Command => $"dotnet elsa persistence apply --host {IEfModuleRefusal.HostPlaceholder} --modules Orders";
    }
}
