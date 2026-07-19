using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Resumption.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Resumption.Tests;

public sealed class RuntimeResumptionPumpTaskTests
{
    [Fact]
    public async Task ExecuteAsync_InvokesSweepWithConfiguredBounds()
    {
        var service = new FakeResumptionService();
        var pump = CreatePump(service, new RuntimeResumptionOptions
        {
            OutboxBatchSize = 11,
            BacklogBatchSize = 12,
            RecoveryScanBatchSize = 13,
            MaxExecutionsPerSweep = 14,
            LeaseTimeout = TimeSpan.FromMinutes(7),
            HeartbeatTimeout = TimeSpan.FromMinutes(8)
        });

        await pump.ExecuteAsync(CancellationToken.None);

        var request = Assert.Single(service.Requests);
        Assert.Equal(11, request.OutboxBatchSize);
        Assert.Equal(12, request.BacklogBatchSize);
        Assert.Equal(13, request.RecoveryScanBatchSize);
        Assert.Equal(14, request.MaxExecutionsPerSweep);
        Assert.Equal(TimeSpan.FromMinutes(7), request.LeaseTimeout);
        Assert.Equal(TimeSpan.FromMinutes(8), request.HeartbeatTimeout);
        Assert.Empty(request.ExcludedWorkflowExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_FaultedDispatch_ParksExecutionThenReleasesAfterBackoff()
    {
        var time = new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var service = new FakeResumptionService();
        var options = new RuntimeResumptionOptions
        {
            SweepInterval = TimeSpan.FromSeconds(10),
            MaxBackoffInterval = TimeSpan.FromMinutes(5)
        };
        var pump = CreatePump(service, options, time);

        // Tick 1: wf-1 faults -> it gets parked.
        service.NextResult = ResultWith(new RuntimeResumptionDispatch("wf-1", RuntimeResumptionDispatchOutcome.Faulted, null, "boom"));
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Empty(service.Requests[0].ExcludedWorkflowExecutionIds); // first sweep excludes nothing

        // Tick 2: still within the backoff window -> wf-1 excluded.
        service.NextResult = EmptyResult();
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Contains("wf-1", service.Requests[1].ExcludedWorkflowExecutionIds);

        // Advance past the (first-failure) backoff window -> wf-1 eligible again.
        time.Advance(TimeSpan.FromSeconds(11));
        service.NextResult = EmptyResult();
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.DoesNotContain("wf-1", service.Requests[2].ExcludedWorkflowExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_RejectedDispatchAlsoParksExecution()
    {
        var service = new FakeResumptionService();
        var pump = CreatePump(service, new RuntimeResumptionOptions());

        service.NextResult = ResultWith(new RuntimeResumptionDispatch("wf-x", RuntimeResumptionDispatchOutcome.Rejected, "env-1", "rejected"));
        await pump.ExecuteAsync(CancellationToken.None);

        service.NextResult = EmptyResult();
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Contains("wf-x", service.Requests[1].ExcludedWorkflowExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulDispatch_ClearsPriorBackoff()
    {
        var service = new FakeResumptionService();
        var pump = CreatePump(service, new RuntimeResumptionOptions());

        service.NextResult = ResultWith(new RuntimeResumptionDispatch("wf-1", RuntimeResumptionDispatchOutcome.Faulted, null, "boom"));
        await pump.ExecuteAsync(CancellationToken.None);

        // A later successful re-drive of the same execution clears its backoff entry.
        service.NextResult = ResultWith(new RuntimeResumptionDispatch("wf-1", RuntimeResumptionDispatchOutcome.Accepted, "env", null));
        await pump.ExecuteAsync(CancellationToken.None);

        service.NextResult = EmptyResult();
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.DoesNotContain("wf-1", service.Requests[2].ExcludedWorkflowExecutionIds);
    }

    [Fact]
    public async Task ExecuteAsync_SweepThrows_IsSwallowedAndIntervalBacksOff()
    {
        var service = new FakeResumptionService { Throw = new InvalidOperationException("kaboom") };
        var options = new RuntimeResumptionOptions
        {
            SweepInterval = TimeSpan.FromSeconds(10),
            MaxBackoffInterval = TimeSpan.FromMinutes(5)
        };
        var pump = CreatePump(service, options);

        Assert.Equal(TimeSpan.FromSeconds(10), pump.CurrentSweepInterval);

        await pump.ExecuteAsync(CancellationToken.None); // does not throw
        var afterOne = pump.CurrentSweepInterval;
        await pump.ExecuteAsync(CancellationToken.None);
        var afterTwo = pump.CurrentSweepInterval;

        Assert.True(afterTwo > afterOne, "interval should widen on consecutive failures");
        Assert.True(afterTwo <= options.MaxBackoffInterval);

        // A clean sweep resets the interval.
        service.Throw = null;
        service.NextResult = EmptyResult();
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(10), pump.CurrentSweepInterval);
    }

    [Fact]
    public async Task ExecuteAsync_OutboxRecordingFailureLogsOnlySafeIdentities()
    {
        var service = new FakeResumptionService
        {
            Throw = new OutboxProcessingException(
                "outbox-safe",
                "intent-safe",
                new InvalidOperationException("provider-secret"),
                new InvalidOperationException("storage-secret"))
        };
        var logger = new RecordingLogger<RuntimeResumptionPumpTask>();
        var pump = new RuntimeResumptionPumpTask(
            service,
            Microsoft.Extensions.Options.Options.Create(new RuntimeResumptionOptions()),
            new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")),
            logger);

        await pump.ExecuteAsync(CancellationToken.None);

        var entry = Assert.Single(logger.Entries);
        Assert.Null(entry.Exception);
        Assert.Equal("RuntimePostCommitResultRecordingFailed", entry.EventId.Name);
        var serialized = string.Join(" ", entry.Fields.Select(pair => $"{pair.Key}={pair.Value}"));
        Assert.Contains("outbox-safe", serialized, StringComparison.Ordinal);
        Assert.Contains("intent-safe", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-secret", serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("storage-secret", serialized, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_IntervalBackoffIsCappedAtMax()
    {
        var service = new FakeResumptionService { Throw = new InvalidOperationException("nope") };
        var options = new RuntimeResumptionOptions
        {
            SweepInterval = TimeSpan.FromSeconds(10),
            MaxBackoffInterval = TimeSpan.FromMinutes(1)
        };
        var pump = CreatePump(service, options);

        for (var i = 0; i < 20; i++)
            await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromMinutes(1), pump.CurrentSweepInterval);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationPropagates()
    {
        var service = new FakeResumptionService();
        var pump = CreatePump(service, new RuntimeResumptionOptions());

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        service.Throw = new OperationCanceledException(cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump.ExecuteAsync(cts.Token));
    }

    [Fact]
    public void GetSchedule_ReturnsAdaptiveSchedule()
    {
        var pump = CreatePump(new FakeResumptionService(), new RuntimeResumptionOptions());

        var schedule = pump.GetSchedule();

        var adaptive = Assert.IsType<AdaptiveIntervalSchedule>(schedule);
        Assert.NotNull(adaptive.ScheduleExecution(() => Task.CompletedTask));
    }

    [Fact]
    public async Task StartAndStop_AreNoOps()
    {
        var pump = CreatePump(new FakeResumptionService(), new RuntimeResumptionOptions());
        await pump.StartAsync(CancellationToken.None);
        await pump.StopAsync(CancellationToken.None);
    }

    private static RuntimeResumptionPumpTask CreatePump(FakeResumptionService service, RuntimeResumptionOptions options, TimeProvider? timeProvider = null) =>
        new(service, Microsoft.Extensions.Options.Options.Create(options), timeProvider ?? new MutableTimeProvider(DateTimeOffset.Parse("2026-01-01T00:00:00Z")), NullLogger<RuntimeResumptionPumpTask>.Instance);

    private static RuntimeResumptionSweepResult EmptyResult() => new(0, 0, 0, []);

    private static RuntimeResumptionSweepResult ResultWith(params RuntimeResumptionDispatch[] dispatches) => new(0, 0, 0, dispatches);

    private sealed class FakeResumptionService : IRuntimeResumptionService
    {
        public List<RuntimeResumptionSweepRequest> Requests { get; } = [];
        public RuntimeResumptionSweepResult NextResult { get; set; } = new(0, 0, 0, []);
        public Exception? Throw { get; set; }

        public ValueTask<RuntimeResumptionSweepResult> SweepAsync(RuntimeResumptionSweepRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Throw is not null)
                throw Throw;
            return new ValueTask<RuntimeResumptionSweepResult>(NextResult);
        }
    }

    private sealed class MutableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var fields = state is IEnumerable<KeyValuePair<string, object?>> structured
                ? structured.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)
                : new Dictionary<string, object?> { ["Message"] = formatter(state, exception) };
            Entries.Add(new LogEntry(eventId, fields, exception));
        }
    }

    private sealed record LogEntry(
        EventId EventId,
        IReadOnlyDictionary<string, object?> Fields,
        Exception? Exception);
}
