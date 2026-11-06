using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Jobs.Core;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Jobs.Tests;

public class JobSchedulePumpTests
{
    private static readonly JobScope FirstScope = new("jobs-tests", "tenant-one");
    private static readonly JobScope SecondScope = new("jobs-tests", "tenant-two");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(250);

    [Theory]
    [InlineData(JobRecurrenceKind.Interval, "PT1M", 60)]
    [InlineData(JobRecurrenceKind.Interval, "00:01:00", 60)]
    [InlineData(JobRecurrenceKind.Cron, "* * * * *", 60)]
    [InlineData(JobRecurrenceKind.Cron, "*/10 * * * * *", 10)]
    public async Task Default_calculator_uses_the_supplied_utc_cursor_and_strictly_advances(JobRecurrenceKind kind, string expression, int seconds)
    {
        var services = new ServiceCollection();
        services.AddElsaJobs();
        await using var provider = services.BuildServiceProvider();
        var calculator = provider.GetRequiredService<IJobRecurrenceCalculator>();
        var after = new DateTimeOffset(2026, 11, 6, 12, 0, 0, TimeSpan.FromHours(2));
        var next = calculator.Next(new(kind, expression), after);
        Assert.Equal(after.ToUniversalTime().AddSeconds(seconds), next);
        Assert.Equal(TimeSpan.Zero, next!.Value.Offset);
    }

    [Theory]
    [InlineData(JobRecurrenceKind.Interval, "00:00:00")]
    [InlineData(JobRecurrenceKind.Interval, "negative-secret-canary")]
    [InlineData(JobRecurrenceKind.Cron, "invalid-secret-canary")]
    public async Task Invalid_recurrence_has_a_safe_jobs_error_with_original_cause(JobRecurrenceKind kind, string expression)
    {
        var services = new ServiceCollection(); services.AddElsaJobs();
        await using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<JobException>(() => provider.GetRequiredService<IJobRecurrenceCalculator>().Next(new(kind, expression), Now));
        Assert.Equal("jobs.recurrence.invalid", error.Code);
        Assert.NotNull(error.InnerException);
        Assert.DoesNotContain("secret-canary", error.Message);
    }

    [Fact]
    public async Task Unseeded_jitter_cron_has_a_safe_jobs_error_preserving_the_parser_cause()
    {
        var services = new ServiceCollection(); services.AddElsaJobs();
        await using var provider = services.BuildServiceProvider();
        var error = Assert.Throws<JobException>(() => provider.GetRequiredService<IJobRecurrenceCalculator>()
            .Next(new(JobRecurrenceKind.Cron, "H * * * *"), Now));
        Assert.Equal("jobs.recurrence.invalid", error.Code);
        Assert.DoesNotContain("H * * * *", error.Message);
        var format = Assert.IsType<FormatException>(error.InnerException);
        Assert.Equal("MissingSeedException", Assert.IsAssignableFrom<Exception>(format.InnerException).GetType().Name);
    }

    [Fact]
    public async Task An_interval_beyond_the_last_representable_instant_has_no_next_occurrence()
    {
        var services = new ServiceCollection(); services.AddElsaJobs();
        await using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetRequiredService<IJobRecurrenceCalculator>().Next(new(JobRecurrenceKind.Interval, "PT1S"), DateTimeOffset.MaxValue));
    }

    [Fact]
    public async Task Host_calculator_replacement_is_preserved_and_duplicate_selection_fails_both_startup_routes()
    {
        var custom = new CustomCalculator();
        var services = new ServiceCollection();
        services.AddSingleton<IJobRecurrenceCalculator>(custom);
        services.AddSingleton<IJobCommands>(new ScriptedJobTransport());
        services.AddSingleton<IJobQueries>(new ScriptedJobTransport());
        services.AddElsaJobs();
        await using (var provider = services.BuildServiceProvider())
            Assert.Same(custom, Assert.Single(provider.GetServices<IJobRecurrenceCalculator>()));
        services.AddSingleton<IJobRecurrenceCalculator>(new CustomCalculator());
        await using var duplicate = services.BuildServiceProvider();
        var hosted = Assert.Single(duplicate.GetServices<IHostedService>());
        var startup = Assert.Single(duplicate.GetServices<IStartupTask>());
        Assert.Same(hosted, startup);
        Assert.Equal("jobs.authority.invalid", (await Assert.ThrowsAsync<JobException>(() => hosted.StartAsync(CancellationToken.None))).Code);
        Assert.Equal("jobs.authority.invalid", (await Assert.ThrowsAsync<JobException>(() => startup.ExecuteAsync(CancellationToken.None))).Code);
    }

    [Fact]
    public async Task Producer_only_registration_keeps_its_calculator_without_starting_materialization_or_requiring_worker_scopes()
    {
        var services = new ServiceCollection(); services.AddElsaJobs();
        await using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredService<IJobRecurrenceCalculator>());
        Assert.Null(provider.GetService<JobsWorker>());
        Assert.Null(provider.GetService<JobSchedulePump>());
        Assert.Empty(provider.GetServices<IBackgroundTask>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public void Materialization_batch_requests_are_bounded_before_worker_startup(int count)
    {
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() => services.AddElsaJobs(options => options.MaximumOccurrencesPerMaterialization = count));
    }

    [Theory]
    [InlineData(JobCommandStatus.Applied)]
    [InlineData(JobCommandStatus.NoEligibleWork)]
    [InlineData(JobCommandStatus.Held)]
    public async Task Each_bounded_result_delays_before_fresh_scope_and_round_robin_materialization(JobCommandStatus status)
    {
        var firstTimer = NewSignal<bool>();
        await using var harness = Pump(observe: due => { if (due == Interval) firstTimer.TrySetResult(true); });
        var calls = 0;
        harness.Transport.Materialization = (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 2) harness.Stop.Cancel();
            return ValueTask.FromResult(new JobMaterializationResult(status, []));
        };
        harness.Start();
        await Task.WhenAny(firstTimer.Task, harness.Running!).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(firstTimer.Task.IsCompleted, "Every bounded result must install its poll delay before another call or loop exit.");
        Assert.Single(harness.Transport.Materializations);
        Assert.True(Assert.Single(harness.CommandScopes).Disposed);
        harness.Clock.Advance(Interval);
        await Wait(harness.Running!);
        var requests = harness.Transport.Materializations.ToArray();
        Assert.Equal(new[] { FirstScope, SecondScope }, requests.Select(request => request.Scope));
        Assert.All(requests, request => Assert.Equal(32, request.MaximumOccurrences));
        var scopes = harness.CommandScopes.ToArray();
        Assert.Equal(2, scopes.Length); Assert.NotSame(scopes[0], scopes[1]); Assert.All(scopes, scope => Assert.True(scope.Disposed));
        Assert.Empty(harness.Transport.Claims); Assert.Empty(harness.Transport.Admissions);
    }

    [Fact]
    public async Task A_materialization_call_cannot_overlap_another_even_while_poll_time_passes()
    {
        var entered = NewSignal<bool>();
        var release = NewSignal<bool>();
        var delayed = NewSignal<bool>();
        await using var harness = Pump(observe: due => { if (due == Interval) delayed.TrySetResult(true); });
        harness.Transport.Materialization = async (_, cancellationToken) =>
        {
            entered.TrySetResult(true);
            await release.Task.WaitAsync(cancellationToken);
            return new(JobCommandStatus.Applied, []);
        };
        harness.Start();
        await Wait(entered.Task);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Single(harness.Transport.Materializations);
        release.TrySetResult(true);
        await Wait(delayed.Task);
        Assert.Single(harness.Transport.Materializations);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Uncertain_materialization_resumes_provider_drain_without_client_side_job_submission(bool unavailableStatus)
    {
        var delayed = NewSignal<bool>();
        await using var harness = Pump(singleScope: true, observe: due => { if (due == Interval) delayed.TrySetResult(true); });
        var calls = 0;
        harness.Transport.Materialization = (_, _) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                if (unavailableStatus) return ValueTask.FromResult(new JobMaterializationResult(JobCommandStatus.Unavailable, []));
                throw new JobPersistenceException("materialize", new IOException("secret-materialization-canary"));
            }
            harness.Stop.Cancel();
            return ValueTask.FromResult(new JobMaterializationResult(JobCommandStatus.Applied,
                [new("schedule", 1, Now, "provider-owned-job", 0, null)]));
        };
        harness.Start();
        await Wait(delayed.Task);
        Assert.Single(harness.Transport.Materializations);
        harness.Clock.Advance(Interval);
        await Wait(harness.Running!);
        Assert.Equal(2, harness.Transport.Materializations.Count);
        Assert.Equal(harness.Transport.Materializations.First(), harness.Transport.Materializations.Last());
        Assert.Empty(harness.Transport.Admissions); Assert.Empty(harness.Transport.Claims);
        Assert.All(harness.Log.Messages, message => Assert.DoesNotContain("secret-materialization-canary", message));
        Assert.All(harness.Log.Errors, error => Assert.Null(error));
    }

    [Fact]
    public async Task Failure_in_one_trusted_scope_does_not_block_the_next_scope()
    {
        var delayed = NewSignal<bool>();
        await using var harness = Pump(observe: due => { if (due == Interval) delayed.TrySetResult(true); });
        harness.Transport.Materialization = (request, _) =>
        {
            if (request.Scope == FirstScope) throw new JobException("jobs.recurrence.unavailable", "The installed recurrence is temporarily unavailable.");
            harness.Stop.Cancel();
            return ValueTask.FromResult(new JobMaterializationResult(JobCommandStatus.Applied, []));
        };
        harness.Start(); await Wait(delayed.Task);
        harness.Clock.Advance(Interval); await Wait(harness.Running!);
        Assert.Equal(new[] { FirstScope, SecondScope }, harness.Transport.Materializations.Select(request => request.Scope));
        Assert.Empty(harness.Transport.Admissions);
    }

    [Fact]
    public async Task Shutdown_cancels_inflight_materialization_and_does_not_pull_another_batch()
    {
        var entered = NewSignal<bool>();
        var cancelled = NewSignal<bool>();
        await using var harness = Pump();
        harness.Transport.Materialization = async (_, cancellationToken) =>
        {
            entered.TrySetResult(true);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled.TrySetResult(true);
                throw;
            }
            return new(JobCommandStatus.Applied, []);
        };
        harness.Start(); await Wait(entered.Task);
        harness.Stop.Cancel();
        await Wait(cancelled.Task); await Wait(harness.Running!);
        Assert.Single(harness.Transport.Materializations);
        Assert.True(Assert.Single(harness.CommandScopes).Disposed);
    }

    [Fact]
    public async Task Materialization_io_timeout_is_bounded_and_retries_only_after_the_poll_delay()
    {
        var entered = NewSignal<bool>();
        var cancelled = NewSignal<bool>();
        var delayed = NewSignal<bool>();
        await using var harness = Pump(timeout: TimeSpan.FromSeconds(1), observe: due => { if (due == Interval) delayed.TrySetResult(true); });
        harness.Transport.Materialization = async (_, cancellationToken) =>
        {
            entered.TrySetResult(true);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                cancelled.TrySetResult(true);
                throw;
            }
            return new(JobCommandStatus.Applied, []);
        };
        harness.Start(); await Wait(entered.Task);
        harness.Clock.Advance(TimeSpan.FromSeconds(1));
        await Wait(cancelled.Task); await Wait(delayed.Task);
        Assert.Single(harness.Transport.Materializations);
        Assert.Single(harness.Log.Messages);
    }

    [Fact]
    public async Task Single_host_and_tasks_lifecycle_materializes_while_its_only_physical_handler_is_synchronously_occupied()
    {
        using var release = new ManualResetEventSlim();
        var entered = NewSignal<bool>();
        var materialized = NewSignal<bool>();
        var transport = new ScriptedJobTransport();
        var clock = new FakeTimeProvider(Now);
        var support = new JobHandlerSupport(new("blocking-report", "1"), new("text", 1), new("text-result", 1), "blocking-deployment");
        var request = new JobRequest(support.Handler, new(support.InputSchema, JsonSerializer.SerializeToElement("work")), support.ResultSchema, "default",
            new(new(3, TimeSpan.Zero, TimeSpan.Zero, 1, 0, false), TimeSpan.FromMinutes(1), TimeSpan.FromDays(30)), null, null);
        var claimNumber = 0;
        JobAttemptLease? lease = null;
        transport.Claim = (claim, _) =>
        {
            if (Interlocked.Increment(ref claimNumber) != 1) return ValueTask.FromResult(new JobClaimResult(JobCommandStatus.NoEligibleWork, null));
            lease = new(FirstScope, "physical-job", "physical-attempt", 1, claim.Owner, 1, 1, Now, Now.AddSeconds(30), Now.AddMinutes(1), support.ImplementationIdentity);
            return ValueTask.FromResult(new JobClaimResult(JobCommandStatus.Applied, lease));
        };
        transport.Find = (_, _, _) => ValueTask.FromResult<JobSnapshot?>(new(FirstScope, "physical-job", JobRequestDigest.Compute(request), request,
            JobState.AttemptActive, 1, Now, Now, lease, null, null, false, null));
        transport.Renewal = (token, _, _) => ValueTask.FromResult(new JobRenewal(JobCommandStatus.Applied, token with { Revision = token.Revision + 1 }, false, false));
        transport.Start = (_, _) => ValueTask.FromResult(new JobTransition(JobCommandStatus.Applied, null, null, null));
        transport.Completion = (_, _, _) => ValueTask.FromResult(new JobTransition(JobCommandStatus.Applied, null, null, null));
        transport.Materialization = async (_, cancellationToken) =>
        {
            await entered.Task.WaitAsync(cancellationToken);
            materialized.TrySetResult(true);
            return new(JobCommandStatus.Applied, []);
        };
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(clock);
        services.AddSingleton<IJobCommands>(transport); services.AddSingleton<IJobQueries>(transport);
        services.AddScoped(_ => new BlockingHandler(entered, release));
        services.AddJobHandler<BlockingHandler, string, string>(support);
        services.AddElsaJobs(options =>
        {
            options.EnableWorker = true; options.WorkerScopes.Add(FirstScope);
            options.MaximumConcurrentAttempts = 1; options.ShutdownGracePeriod = TimeSpan.Zero;
        });
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var worker = provider.GetRequiredService<JobsWorker>();
        try
        {
            await worker.StartAsync(CancellationToken.None);
            await ((IBackgroundTask)worker).StartAsync(CancellationToken.None);
            await Wait(entered.Task); await Wait(materialized.Task);
            Assert.Empty(transport.Completions);
            Assert.Single(transport.Claims);
            release.Set(); await Wait(transport.Completed.Task);
            await Wait(worker.StopAsync(CancellationToken.None));
            Assert.Single(transport.Materializations);
        }
        finally
        {
            release.Set();
            await worker.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static PumpHarness Pump(bool singleScope = false, TimeSpan? timeout = null, Action<TimeSpan>? observe = null)
    {
        var clock = new FakeTimeProvider(Now);
        var transport = new ScriptedJobTransport();
        var commandScopes = new ConcurrentQueue<ScopeMarker>();
        var log = new PumpLog();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(observe is null ? clock : new ObservingTimeProvider(clock, observe));
        services.AddScoped(_ => new ScopeMarker());
        services.AddScoped<IJobCommands>(provider =>
        {
            commandScopes.Enqueue(provider.GetRequiredService<ScopeMarker>());
            return transport;
        });
        services.AddSingleton<IJobQueries>(transport);
        services.AddSingleton<ILogger<JobSchedulePump>>(log);
        services.AddElsaJobs(options =>
        {
            options.EnableWorker = true; options.WorkerScopes.Add(FirstScope);
            if (!singleScope) options.WorkerScopes.Add(SecondScope);
            options.MaterializationInterval = Interval;
            options.MaterializationTimeout = timeout ?? TimeSpan.FromSeconds(30);
        });
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        return new(provider, provider.GetRequiredService<JobSchedulePump>(), transport, clock, commandScopes, log);
    }

    private static TaskCompletionSource<T> NewSignal<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static Task<T> Wait<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10));
    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));
    private sealed class CustomCalculator : IJobRecurrenceCalculator
    {
        public DateTimeOffset? Next(JobRecurrence recurrence, DateTimeOffset after) => after.AddMinutes(1);
    }
    private sealed class ScopeMarker : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    private sealed class PumpLog : ILogger<JobSchedulePump>
    {
        internal ConcurrentQueue<string> Messages { get; } = new();
        internal ConcurrentQueue<Exception?> Errors { get; } = new();
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Enqueue(formatter(state, exception)); Errors.Enqueue(exception);
        }
    }
    private sealed record PumpHarness(ServiceProvider Provider, JobSchedulePump Pump, ScriptedJobTransport Transport,
        FakeTimeProvider Clock, ConcurrentQueue<ScopeMarker> CommandScopes, PumpLog Log) : IAsyncDisposable
    {
        internal CancellationTokenSource Stop { get; } = new();
        internal Task? Running { get; private set; }
        internal void Start() => Running = Pump.RunAsync(Stop.Token);
        public async ValueTask DisposeAsync()
        {
            Stop.Cancel();
            if (Running is not null) await Running.WaitAsync(TimeSpan.FromSeconds(10));
            await Provider.DisposeAsync(); Stop.Dispose();
        }
    }
    private sealed class BlockingHandler(TaskCompletionSource<bool> entered, ManualResetEventSlim release) : IJobHandler<string, string>
    {
        public ValueTask<string> ExecuteAsync(string input, JobExecutionContext context, CancellationToken cancellationToken)
        {
            entered.TrySetResult(true);
            if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException("The synchronous test handler was not released.");
            return ValueTask.FromResult("done");
        }
    }
}
