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
/// raced a peer, an EF module's refusal. The shell is retried in the background on a capped, doubling delay until it
/// activates, with no restart; each failure is logged; <c>/health/ready</c> says why the shell is not active; and an Attention
/// item stands while it is not. The delays run on a fake clock, so the backoff is asserted exactly rather than waited for.
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
        _attention = new ShellActivationAttentionContributor(_tracker, _time);
        _service = new EagerShellActivationHostedService(_registry, Configuration("00:00:01", "00:00:04"), _tracker, _logger.CreateLogger<EagerShellActivationHostedService>(), _time);
    }

    public async ValueTask DisposeAsync()
    {
        await _service.StopAsync(CancellationToken.None);
        if (_app is not null)
            await _app.DisposeAsync();
    }

    [Fact]
    public async Task A_shell_whose_first_activation_fails_is_retried_on_a_doubling_delay_capped_at_the_maximum_and_activates_without_a_restart()
    {
        _registry.Script(Fault(), Fault(), Fault(), Fault(), null);

        await _service.StartAsync(CancellationToken.None);

        Assert.Equal((1, Second), (_tracker.FailureOf(Shell)!.Attempts, Delay()));
        foreach (var (attempts, expected) in new[] { (2, 2 * Second), (3, 4 * Second), (4, 4 * Second) })
        {
            await AdvanceToNextAttemptAsync();
            await WaitUntilAsync(() => _tracker.FailureOf(Shell)?.Attempts == attempts);
            Assert.Equal(expected, Delay());
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
        Assert.Contains("00:00:01", warnings[0], StringComparison.Ordinal);
        Assert.Contains("attempt 2", warnings[1], StringComparison.Ordinal);
        Assert.Contains("00:00:02", warnings[1], StringComparison.Ordinal);
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
        Assert.Equal(4 * Second, Delay());
        Assert.Equal(("Orders", IEfModuleRefusal.PendingMigrationsCode), (failure.Refusal!.Module, failure.Refusal.Code));
        Assert.Equal(["20260930_One"], failure.Refusal.PendingMigrations);
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
    public async Task Readiness_says_why_a_shell_is_not_active_without_carrying_the_failures_message()
    {
        _registry.Script(Fault(Secret));
        await _service.StartAsync(CancellationToken.None);
        using var client = await HealthClientAsync();

        var (status, body) = await ReadyAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        var shell = Assert.Single(body.GetProperty("shells").EnumerateArray());
        var reason = shell.GetProperty("reason");
        Assert.Equal("activation-failed", reason.GetProperty("code").GetString());
        Assert.Equal(nameof(InvalidOperationException), reason.GetProperty("failureType").GetString());
        Assert.Equal(1, reason.GetProperty("attempts").GetInt32());
        Assert.Equal(_tracker.FailureOf(Shell)!.NextAttemptAt, reason.GetProperty("nextAttemptAt").GetDateTimeOffset());
        Assert.False(reason.TryGetProperty("refusal", out _));
        Assert.DoesNotContain("hunter2", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_names_the_module_and_migrations_of_a_refusal_but_not_the_command_that_carries_the_hosts_directory()
    {
        _registry.Script(new PendingRefusal());
        await _service.StartAsync(CancellationToken.None);
        using var client = await HealthClientAsync();

        var (_, body) = await ReadyAsync(client);

        var reason = Assert.Single(body.GetProperty("shells").EnumerateArray()).GetProperty("reason");
        Assert.Equal("activation-refused", reason.GetProperty("code").GetString());
        var refusal = reason.GetProperty("refusal");
        Assert.Equal("Orders", refusal.GetProperty("module").GetString());
        Assert.Equal(IEfModuleRefusal.PendingMigrationsCode, refusal.GetProperty("code").GetString());
        Assert.Equal(["20260930_One"], refusal.GetProperty("pendingMigrations").EnumerateArray().Select(id => id.GetString()));
        Assert.DoesNotContain("persistence apply", body.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Readiness_of_a_shell_no_attempt_has_failed_says_it_is_not_activated()
    {
        using var client = await HealthClientAsync();

        var (status, body) = await ReadyAsync(client);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("not-activated", Assert.Single(body.GetProperty("shells").EnumerateArray()).GetProperty("reason").GetProperty("code").GetString());
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
    public async Task An_attention_item_for_a_refusal_is_critical_and_names_the_module()
    {
        _registry.Script(new PendingRefusal());
        await _service.StartAsync(CancellationToken.None);

        var item = Assert.Single((await _attention.EvaluateAsync(Context())).Items);

        Assert.Equal(AttentionSeverity.Critical, item.Severity);
        Assert.Contains(new AttentionCorrelation("module", "Orders"), item.Correlations);
        Assert.Contains("operator", item.Summary, StringComparison.Ordinal);
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

    private TimeSpan Delay() => _tracker.FailureOf(Shell)!.NextAttemptAt - _tracker.FailureOf(Shell)!.LastFailedAt;

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

    /// <summary>Answers <see cref="GetOrActivateAsync"/> with what a test scripts, an exception to throw or <see langword="null"/> to succeed; the last entry repeats.</summary>
    private sealed class ScriptedShellRegistry : IShellRegistry
    {
        private Exception?[] _script = [];

        public int Attempts { get; private set; }

        public void Script(params Exception?[] script) => _script = script;

        public Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default)
        {
            var step = _script[Math.Min(Attempts++, _script.Length - 1)];
            return step is null ? Task.FromResult<IShell>(null!) : Task.FromException<IShell>(step);
        }

        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IShell? GetActive(string name) => null;
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
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
