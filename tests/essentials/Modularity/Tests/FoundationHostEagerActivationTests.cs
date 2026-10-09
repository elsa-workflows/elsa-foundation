using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using CShells.DependencyInjection;
using CShells.Hosting;
using CShells.Lifecycle;
using Elsa.Attention.Core;
using Elsa.Foundation.Host.Health;
using Elsa.Foundation.Host.Shells;
using Elsa.Modularity.Api.Authorization;
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
    private readonly ServiceProvider _services;
    private readonly IConfiguration _configuration = Configuration("00:00:01", "00:00:04");
    private Exception? _expectedShutdownFailure;
    private WebApplication? _app;

    public FoundationHostEagerActivationTests()
    {
        _tracker = new ShellActivationTracker(_time);
        _attention = new ShellActivationAttentionContributor(_tracker);
        var services = new ServiceCollection();
        services.AddSingleton<IShellRegistry>(_registry);
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton(_tracker);
        services.AddSingleton(_configuration);
        services.AddSingleton(_logger.CreateLogger<EagerShellActivationHostedService>());
        services.AddShellActivationRunner();
        services.AddSingleton<EagerShellActivationHostedService>();
        _services = services.BuildServiceProvider();
        _service = _services.GetRequiredService<EagerShellActivationHostedService>();
    }

    public async ValueTask DisposeAsync()
    {
        _registry.Hang?.TrySetResult();
        try
        {
            await _service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (Exception exception) when (IsExpectedShutdownFailure(exception))
        {
            // A fault explicitly asserted by this test remains the retained shutdown result.
        }
        finally
        {
            try
            {
                await _services.DisposeAsync();
            }
            finally
            {
                if (_app is not null)
                    await _app.DisposeAsync();
            }
        }
    }

    private bool IsExpectedShutdownFailure(Exception exception) =>
        ReferenceEquals(exception, _expectedShutdownFailure) ||
        exception is AggregateException actual && _expectedShutdownFailure is AggregateException expected &&
        actual.Flatten().InnerExceptions.SequenceEqual(expected.Flatten().InnerExceptions);

    [Fact]
    public async Task Stopping_before_start_prevents_activation_subscription_and_retry_work()
    {
        await _service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Equal(0, _registry.Attempts);
        Assert.Equal(0, _registry.SubscriberCount);
        Assert.Equal(0, _time.Timers);
    }

    [Fact]
    public async Task Explicit_opt_out_creates_no_activation_work()
    {
        _configuration[EagerShellActivationHostedService.EnabledKey] = "false";

        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, _registry.Attempts);
        Assert.Equal(0, _time.Timers);
        Assert.Equal(0, _registry.SubscriberCount);
    }

    [Fact]
    public async Task A_not_current_return_has_a_safe_failure_classification_and_ordinary_retry()
    {
        _registry.Activation = (name, _) => Task.FromResult<IShell>(new StubShell(name));

        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        var failure = _tracker.FailureOf(Shell)!;
        Assert.Equal("NotCurrent", failure.FailureType);
        Assert.Null(failure.Refusal);
        Assert.Equal(1, failure.Attempts);
        AssertDelay(Second);
    }

    [Fact]
    public async Task Repeated_start_owns_one_failed_pass_one_subscription_and_one_retry_worker()
    {
        _registry.Script(Fault(), null);

        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, _registry.Attempts);
        Assert.Equal(1, _registry.SubscriberCount);
        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _tracker.FailureOf(Shell) is null);
        Assert.Equal(2, _registry.Attempts);
    }

    [Fact]
    public async Task Initial_attempts_are_serial_and_no_retry_timer_is_armed_until_the_later_target_finishes()
    {
        _configuration["CShells:Shells:later:Name"] = "later";
        _registry.Script(Fault(), null);
        _registry.DuringAttempt = () =>
        {
            Assert.Equal(0, _time.Timers);
            return Task.CompletedTask;
        };

        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal([Shell, "later"], _registry.Calls);
        Assert.Equal(1, _tracker.FailureOf(Shell)!.Attempts);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task An_initial_fatal_rethrows_the_original_and_skips_later_targets_without_retry_or_shutdown_fault(int kind)
    {
        Exception fatal = kind switch
        {
            0 => new OutOfMemoryException("fatal"),
            1 => new StackOverflowException("fatal"),
            2 => new AccessViolationException("fatal"),
            3 => new AppDomainUnloadedException("fatal"),
            _ => new BadImageFormatException("fatal")
        };
        _configuration["CShells:Shells:later:Name"] = "later";
        _registry.Script(fatal);

        var failure = await Record.ExceptionAsync(() => _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10)));

        Assert.Same(fatal, failure);
        Assert.Equal([Shell], _registry.Calls);
        Assert.Equal(0, _time.Timers);
        Assert.Empty(_tracker.Failing());
        await _service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Cancelling_the_callers_token_after_start_returns_does_not_end_owned_recovery()
    {
        _registry.Script(Fault(), null);
        using var startup = new CancellationTokenSource();
        await _service.StartAsync(startup.Token).WaitAsync(TimeSpan.FromSeconds(10));

        await startup.CancelAsync();
        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _tracker.FailureOf(Shell) is null);

        Assert.Equal(2, _registry.Attempts);
    }

    [Fact]
    public async Task An_uncancelled_operation_cancelled_exception_is_an_ordinary_retryable_failure()
    {
        _registry.Script(new OperationCanceledException("not requested"), null);

        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(nameof(OperationCanceledException), _tracker.FailureOf(Shell)!.FailureType);
        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _tracker.FailureOf(Shell) is null);

        Assert.Equal(2, _registry.Attempts);
    }

    [Fact]
    public async Task A_background_fatal_stops_only_its_target_and_shutdown_rethrows_its_original_instance()
    {
        const string other = "other";
        var fatal = new BadImageFormatException("fatal background");
        var attempts = new ConcurrentDictionary<string, int>();
        _configuration[$"CShells:Shells:{other}:Name"] = other;
        _registry.Activation = async (name, _) =>
        {
            if (attempts.AddOrUpdate(name, 1, (_, count) => count + 1) == 1)
                throw Fault();
            if (name == Shell)
                throw fatal;
            await _registry.BecomeActiveAsync(name, notify: false);
            return _registry.GetActive(name)!;
        };
        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => _time.Timers == 2);

        _time.Advance(Second);
        await WaitUntilAsync(() => attempts.GetValueOrDefault(Shell) == 2 && _tracker.FailureOf(other) is null);
        _expectedShutdownFailure = fatal;
        Assert.Same(fatal, await Record.ExceptionAsync(() => _service.StopAsync(CancellationToken.None)));
        _time.Advance(10 * Second);

        Assert.Equal(2, attempts[Shell]);
        Assert.Equal(2, attempts[other]);
    }

    [Fact]
    public async Task Cancellation_while_start_is_pending_prevents_later_initial_targets()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _configuration["CShells:Shells:later:Name"] = "later";
        _registry.Activation = async (_, token) =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Cancelled activation unexpectedly completed.");
        };
        using var startup = new CancellationTokenSource();
        var start = _service.StartAsync(startup.Token).WaitAsync(TimeSpan.FromSeconds(10));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await startup.CancelAsync();

        Assert.IsAssignableFrom<OperationCanceledException>(await Record.ExceptionAsync(() => start.WaitAsync(TimeSpan.FromSeconds(10))));
        Assert.Equal([Shell], _registry.Calls);
        Assert.Equal(0, _time.Timers);
    }

    [Fact]
    public async Task A_bounded_stop_leaves_the_same_join_until_ignoring_activation_completes()
    {
        _registry.Script(Fault(), null);
        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        _registry.Hang = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _registry.Attempts == 2);
        using var bounded = new CancellationTokenSource();
        var first = _service.StopAsync(bounded.Token);
        await bounded.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);

        var laterJoin = _service.StopAsync(CancellationToken.None);
        Assert.False(laterJoin.IsCompleted);
        _registry.Hang.TrySetResult();
        await laterJoin.WaitAsync(TimeSpan.FromSeconds(10));
        await _service.StopAsync(CancellationToken.None);
        _time.Advance(10 * Second);

        Assert.Equal(2, _registry.Attempts);
    }

    [Fact]
    public async Task Live_shutdown_joins_all_targets_then_selects_the_first_configured_fatal_even_when_another_finishes_first()
    {
        string[] names = [Shell, "later", "last"];
        var gates = names.ToDictionary(name => name, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        var attempts = new ConcurrentDictionary<string, int>();
        var firstFatal = new BadImageFormatException("first configured");
        var laterFatal = new AccessViolationException("first completed");
        foreach (var name in names.Skip(1))
            _configuration[$"CShells:Shells:{name}:Name"] = name;
        _registry.Activation = async (name, _) =>
        {
            if (attempts.AddOrUpdate(name, 1, (_, count) => count + 1) == 1)
                throw Fault();
            await gates[name].Task;
            if (name == Shell)
                throw firstFatal;
            if (name == "later")
                throw laterFatal;
            await _registry.BecomeActiveAsync(name, notify: false);
            return _registry.GetActive(name)!;
        };
        try
        {
            await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await WaitUntilAsync(() => _time.Timers == names.Length);
            _time.Advance(Second);
            await WaitUntilAsync(() => names.All(name => attempts.GetValueOrDefault(name) == 2));
            _expectedShutdownFailure = firstFatal;
            var stop = _service.StopAsync(CancellationToken.None);

            gates["later"].TrySetResult();
            gates[Shell].TrySetResult();
            Assert.False(stop.IsCompleted);
            gates["last"].TrySetResult();

            Assert.Same(firstFatal, await Record.ExceptionAsync(() => stop.WaitAsync(TimeSpan.FromSeconds(10))));
            Assert.Same(firstFatal, await Record.ExceptionAsync(() => _service.StopAsync(CancellationToken.None)));
            Assert.All(names, name => Assert.Equal(2, attempts[name]));
            Assert.Contains(_logger.Exceptions, exception => ReferenceEquals(exception, firstFatal));
            Assert.Contains(_logger.Exceptions, exception => ReferenceEquals(exception, laterFatal));
        }
        finally
        {
            foreach (var gate in gates.Values)
                gate.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public async Task Repeated_raw_active_suppressed_failures_rebase_the_next_visible_count_and_only_a_notification_clears_the_prior_row(bool notify, int expected)
    {
        _registry.Script(Fault());
        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        var original = _tracker.FailureOf(Shell)!;
        _registry.DuringAttempt = () => _registry.BecomeActiveAsync(Shell, notify);
        await AdvanceToNextAttemptAsync();
        await WaitUntilAsync(() => _time.Timers == 2);
        Assert.Null(_tracker.FailureOf(Shell));
        _time.Advance(2 * Second);
        await WaitUntilAsync(() => _time.Timers == 3);
        Assert.Null(_tracker.FailureOf(Shell));

        _registry.DuringAttempt = null;
        _registry.Deactivate(Shell);
        if (notify)
            Assert.Null(_tracker.FailureOf(Shell));
        else
            Assert.Same(original, _tracker.FailureOf(Shell));
        _time.Advance(2 * Second);
        await WaitUntilAsync(() => _registry.Attempts == 4 && _time.Timers == 4);

        Assert.Equal(expected, _tracker.FailureOf(Shell)!.Attempts);
        AssertDelay(expected * Second);
    }

    [Fact]
    public async Task A_suppressed_first_failure_without_notification_does_not_publish_a_row_or_inflate_the_next_failure()
    {
        _registry.Script(Fault());
        _registry.DuringAttempt = () => _registry.BecomeActiveAsync(Shell, notify: false);
        await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await WaitUntilAsync(() => _time.Timers == 1);
        Assert.Null(_tracker.FailureOf(Shell));

        _registry.DuringAttempt = null;
        _registry.Deactivate(Shell);
        Assert.Null(_tracker.FailureOf(Shell));
        _time.Advance(Second);
        await WaitUntilAsync(() => _tracker.FailureOf(Shell)?.Attempts == 1);

        Assert.Equal(2, _registry.Attempts);
        AssertDelay(Second);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_cancellation_callback_failure_does_not_skip_the_join_or_lose_an_independent_fatal(bool alsoFatal)
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackFailure = new InvalidOperationException("callback failure");
        var fatal = new BadImageFormatException("independent fatal");
        _registry.Activation = async (name, token) =>
        {
            if (_registry.Attempts == 1)
                throw Fault();
            using var registration = token.Register(() =>
            {
                cancelled.TrySetResult();
                throw callbackFailure;
            });
            entered.TrySetResult();
            await release.Task;
            if (alsoFatal)
                throw fatal;
            await _registry.BecomeActiveAsync(name, notify: false);
            return _registry.GetActive(name)!;
        };
        try
        {
            await _service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await AdvanceToNextAttemptAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var stop = _service.StopAsync(CancellationToken.None);
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(stop.IsCompleted);
            release.TrySetResult();
            var error = await Record.ExceptionAsync(() => stop.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.NotNull(error);
            _expectedShutdownFailure = error;
            IReadOnlyList<Exception> failures = error is AggregateException aggregate ? aggregate.Flatten().InnerExceptions : [error];

            Assert.Contains(failures, failure => ReferenceEquals(failure, callbackFailure));
            if (alsoFatal)
                Assert.Contains(failures, failure => ReferenceEquals(failure, fatal));
            else
                Assert.Single(failures);
            var repeated = await Record.ExceptionAsync(() => _service.StopAsync(CancellationToken.None));
            Assert.NotNull(repeated);
            Assert.True(IsExpectedShutdownFailure(repeated));
            Assert.Equal(2, _registry.Attempts);
        }
        finally
        {
            release.TrySetResult();
        }
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
        // Restated by the host, which compiles no feature in: tied to the feature's constant here, so a drift fails.
        Assert.Equal(ModuleManagementPermissionKeys.Read, ShellActivationAttentionContributor.RequiredPermission);
        Assert.Equal(ModuleManagementPermissionKeys.Read, _attention.Descriptor.RequiredPermission);
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

    /// <summary>
    /// An attempt can fail after the registry has already announced the shell active, a request having won the race for it: its
    /// failure must not be written then, or it would come back when the shell leaves the active state.
    /// </summary>
    [Fact]
    public async Task A_failure_that_arrives_after_the_shell_became_active_is_not_recorded()
    {
        _registry.Script(Fault());
        _registry.DuringAttempt = () => _registry.BecomeActiveAsync(Shell, notify: true);

        await _service.StartAsync(CancellationToken.None);
        _registry.Deactivate(Shell);

        Assert.Equal(1, _registry.Attempts);
        Assert.Null(_tracker.FailureOf(Shell));
        Assert.Empty(_tracker.Failing());
    }

    [Fact]
    public async Task Snapshot_external_satisfaction_skips_policy_for_a_gated_failed_attempt()
    {
        var failure = Fault();
        _registry.Script(failure);
        var attemptEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.DuringAttempt = async () =>
        {
            attemptEntered.TrySetResult();
            await releaseAttempt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };

        var shellServices = new ServiceCollection();
        shellServices.AddLogging();
        shellServices.AddSingleton<IConfiguration>(_configuration);
        shellServices.AddCShells(shells => shells.AddShell(Shell, _ => { }));

        ServiceProvider? shellProvider = null;
        IShellRegistry? concreteRegistry = null;
        EagerShellActivationHostedService? hostedService = null;
        IShell? concreteShell = null;
        var cleanupErrors = new List<Exception>();
        Exception? primaryError = null;

        async Task CaptureCleanupFailureAsync(Func<Task> cleanup)
        {
            try
            {
                await cleanup().WaitAsync(TimeSpan.FromSeconds(10));
            }
            catch (Exception exception)
            {
                cleanupErrors.Add(exception);
            }
        }

        try
        {
            shellProvider = shellServices.BuildServiceProvider();
            var actualRegistry = shellProvider.GetRequiredService<IShellRegistry>();
            concreteRegistry = actualRegistry;
            concreteShell = await actualRegistry.GetOrActivateAsync(Shell).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(ShellLifecycleState.Active, concreteShell.State);
            Assert.Same(concreteShell, actualRegistry.GetActive(Shell));

            var recordingRunner = new RecordingShellActivationRunner(
                _services.GetRequiredService<IShellActivationRunner>());
            var activeService = new EagerShellActivationHostedService(
                _registry,
                recordingRunner,
                _configuration,
                _tracker,
                _logger.CreateLogger<EagerShellActivationHostedService>());
            hostedService = activeService;

            var start = activeService.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
            await attemptEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await recordingRunner.RunStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var run = Assert.IsAssignableFrom<IShellActivationRun>(recordingRunner.LastRun);
            Assert.Equal(ShellActivationTargetStatus.Attempting, run.Snapshot.Single().Status);
            Assert.Null(_tracker.FailureOf(Shell));

            await _registry.BecomeActiveAsync(concreteShell, notify: false);
            var externallySatisfied = run.Snapshot.Single();
            Assert.Equal(ShellActivationTargetStatus.SatisfiedExternally, externallySatisfied.Status);
            Assert.Equal(1, externallySatisfied.AttemptCount);
            Assert.Equal(0, externallySatisfied.FailedAttemptCount);

            using var healthClient = await HealthClientAsync();
            var (activeStatus, activeBody) = await ReadyAsync(healthClient);
            Assert.Equal(HttpStatusCode.OK, activeStatus);
            Assert.True(Assert.Single(activeBody.GetProperty("shells").EnumerateArray()).GetProperty("active").GetBoolean());
            Assert.Empty((await _attention.EvaluateAsync(Context())).Items);

            releaseAttempt.TrySetResult();
            await start.WaitAsync(TimeSpan.FromSeconds(10));

            var failedAttempt = run.Snapshot.Single();
            Assert.Equal(ShellActivationTargetStatus.SatisfiedExternally, failedAttempt.Status);
            Assert.Equal(ShellActivationAttemptOutcome.ActivationFailed, failedAttempt.LastOutcome);
            Assert.Equal(1, failedAttempt.AttemptCount);
            Assert.Equal(1, failedAttempt.FailedAttemptCount);
            Assert.Null(failedAttempt.NextAttemptAt);
            Assert.Equal(0, recordingRunner.PolicyCalls);
            Assert.Equal(1, recordingRunner.ObserverCalls);
            Assert.Equal(1, _registry.Attempts);
            Assert.Equal(0, _time.Timers);
            Assert.Contains(_logger.Entries, entry =>
                entry.Level == LogLevel.Warning
                && entry.Message.Contains("Eager activation of shell 'default' failed", StringComparison.Ordinal));
            Assert.Contains(_logger.Exceptions, exception => ReferenceEquals(exception, failure));
            Assert.Null(_tracker.FailureOf(Shell));
            Assert.Empty(_tracker.Failing());
            Assert.Empty((await _attention.EvaluateAsync(Context())).Items);
            var (activeAfterFailureStatus, activeAfterFailureBody) = await ReadyAsync(healthClient);
            var activeShellAfterFailure = Assert.Single(activeAfterFailureBody.GetProperty("shells").EnumerateArray());
            Assert.Equal(HttpStatusCode.OK, activeAfterFailureStatus);
            Assert.True(activeShellAfterFailure.GetProperty("active").GetBoolean());
            Assert.Equal(JsonValueKind.Null, activeShellAfterFailure.GetProperty("reason").ValueKind);

            // Deliberately remove the active value without a lifecycle notification. No failed row was
            // created while the concrete shell was active, and external satisfaction stays terminal.
            _registry.Deactivate(Shell);
            var (inactiveStatus, inactiveBody) = await ReadyAsync(healthClient);
            var inactiveShell = Assert.Single(inactiveBody.GetProperty("shells").EnumerateArray());
            Assert.Equal(HttpStatusCode.ServiceUnavailable, inactiveStatus);
            Assert.Equal(ShellNotActiveReasonCodes.NotActivated, inactiveShell.GetProperty("reason").GetProperty("code").GetString());
            Assert.Null(_tracker.FailureOf(Shell));
            Assert.Empty(_tracker.Failing());
            Assert.Empty((await _attention.EvaluateAsync(Context())).Items);

            _time.Advance(TimeSpan.FromHours(1));
            Assert.Equal(ShellActivationTargetStatus.SatisfiedExternally, run.Snapshot.Single().Status);
            Assert.Equal(0, recordingRunner.PolicyCalls);
            Assert.Equal(1, recordingRunner.ObserverCalls);
            Assert.Equal(1, _registry.Attempts);
            Assert.Equal(0, _time.Timers);
        }
        catch (Exception exception)
        {
            primaryError = exception;
            throw;
        }
        finally
        {
            releaseAttempt.TrySetResult();
            if (hostedService is not null)
            {
                await CaptureCleanupFailureAsync(() => hostedService.StopAsync(CancellationToken.None));
                await CaptureCleanupFailureAsync(() =>
                {
                    hostedService.Dispose();
                    return Task.CompletedTask;
                });
            }

            _registry.Deactivate(Shell);
            if (concreteRegistry is not null && concreteShell is not null && concreteShell.State != ShellLifecycleState.Disposed)
            {
                await CaptureCleanupFailureAsync(async () =>
                {
                    var drain = await concreteRegistry.DrainAsync(concreteShell).WaitAsync(TimeSpan.FromSeconds(10));
                    await drain.WaitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                });
            }

            if (shellProvider is not null)
                await CaptureCleanupFailureAsync(() => shellProvider.DisposeAsync().AsTask());

            if (cleanupErrors.Count > 0)
            {
                if (primaryError is not null)
                    cleanupErrors.Insert(0, primaryError);
                throw new AggregateException("The external-settlement test failed during cleanup.", cleanupErrors);
            }
        }
    }

    [Fact]
    public async Task Observing_the_same_registry_twice_subscribes_the_tracker_once()
    {
        _tracker.Observe(_registry);
        _tracker.Observe(_registry);
        _registry.Script((Exception?)null);

        await _service.StartAsync(CancellationToken.None);
        await _service.StartAsync(CancellationToken.None);

        Assert.Equal(1, _registry.SubscriberCount);
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
        private readonly ConcurrentDictionary<string, IShell> _active = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<IShellLifecycleSubscriber> _subscribers = [];
        private Exception?[] _script = [];

        private int _attempts;
        public int Attempts => Volatile.Read(ref _attempts);
        public ConcurrentQueue<string> Calls { get; } = new();
        public Func<string, CancellationToken, Task<IShell>>? Activation { get; set; }

        public TaskCompletionSource? Hang { get; set; }

        /// <summary>Runs inside an attempt, before it answers: what else happens while an activation is in flight.</summary>
        public Func<Task>? DuringAttempt { get; set; }

        public int SubscriberCount => _subscribers.Count;

        public void Script(params Exception?[] script) => _script = script;

        public Task BecomeActiveAsync(string name, bool notify) => BecomeActiveAsync(new StubShell(name), notify);

        public async Task BecomeActiveAsync(IShell shell, bool notify)
        {
            _active[shell.Descriptor.Name] = shell;
            if (notify)
                foreach (var subscriber in _subscribers)
                    await subscriber.OnStateChangedAsync(shell, ShellLifecycleState.Initializing, ShellLifecycleState.Active);
        }

        public void Deactivate(string name) => _active.TryRemove(name, out _);

        public async Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default)
        {
            var attempt = Interlocked.Increment(ref _attempts);
            Calls.Enqueue(name);
            if (Activation is { } activation)
                return await activation(name, cancellationToken);
            var step = _script[Math.Min(attempt - 1, _script.Length - 1)];
            if (DuringAttempt is { } during)
                await during();
            if (Hang is { } hang)
                await hang.Task;
            if (step is not null)
                throw step;
            await BecomeActiveAsync(name, notify: false);
            return _active[name];
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
        public IReadOnlyCollection<IShell> GetActiveShells() => [.. _active.Values];
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

    private sealed class RecordingShellActivationRunner(IShellActivationRunner inner) : IShellActivationRunner
    {
        private int _policyCalls;
        private int _observerCalls;

        public TaskCompletionSource<IShellActivationRun> RunStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IShellActivationRun? LastRun { get; private set; }
        public int PolicyCalls => Volatile.Read(ref _policyCalls);
        public int ObserverCalls => Volatile.Read(ref _observerCalls);

        public IShellActivationRun Start(
            IReadOnlyList<string> shellNames,
            ShellActivationRetryPolicy? retryPolicy = null,
            IShellActivationAttemptObserver? observer = null,
            CancellationToken startupCancellationToken = default)
        {
            ShellActivationRetryPolicy? recordingPolicy = retryPolicy is null
                ? null
                : attempt =>
                {
                    Interlocked.Increment(ref _policyCalls);
                    return retryPolicy(attempt);
                };
            IShellActivationAttemptObserver? recordingObserver = observer is null
                ? null
                : new RecordingAttemptObserver(this, observer);
            var run = inner.Start(shellNames, recordingPolicy, recordingObserver, startupCancellationToken);
            LastRun = run;
            RunStarted.TrySetResult(run);
            return run;
        }

        private sealed class RecordingAttemptObserver(
            RecordingShellActivationRunner owner,
            IShellActivationAttemptObserver inner) : IShellActivationAttemptObserver
        {
            public ValueTask OnAttemptCompletedAsync(ShellActivationAttempt attempt, CancellationToken cancellationToken)
            {
                Interlocked.Increment(ref owner._observerCalls);
                return inner.OnAttemptCompletedAsync(attempt, cancellationToken);
            }
        }
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
