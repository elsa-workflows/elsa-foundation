using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Elsa.Events.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Scheduling.Tests;

// Actual default memory source/journal/DI/pump; dispatch outcomes are controlled, not authored actor proof.
public sealed partial class DurableTimerHistoryPumpTests
{
    private static readonly DateTimeOffset At = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v4", 4, true, DurableTimerHistoryFacts.Kinds);

    [Theory]
    [InlineData(BookmarkResumeDispatchStatus.Dispatched, "TimerDispatched")]
    [InlineData(BookmarkResumeDispatchStatus.Duplicate, "TimerDispatched")]
    [InlineData(BookmarkResumeDispatchStatus.NotFound, "TimerRetired")]
    [InlineData(BookmarkResumeDispatchStatus.WorkflowExecutionMissing, "TimerRetired")]
    [InlineData(BookmarkResumeDispatchStatus.ExecutableMissing, "TimerRetired")]
    public async Task Default_pump_records_original_schedule_and_actual_terminal_outcome(BookmarkResumeDispatchStatus status, string terminal)
    {
        await using var h = new Harness(status); await h.Seed("run", head: true);
        await h.Schedule("run"); h.Clock.Advance(TimeSpan.FromMinutes(1)); await h.Pump.ExecuteAsync(CancellationToken.None);
        Assert.True(h.Log.Errors.Count == 0, string.Join("\n", h.Log.Errors)); Assert.Single(h.Dispatcher.Requests);
        await h.InScope(async services =>
        {
            var owner = (IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>();
            Assert.False(await owner.HasPendingHistoryAsync("run")); Assert.Null(await owner.FindAsync("run", "timer"));
            var page = await services.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("run"));
            Assert.Equal(new[] { "TimerScheduled", terminal }, page.Items.Select(item => item.Name));
            Assert.All(page.Items, item => Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, item.Authority));
            Assert.Equal(status.ToString(), page.Items[1].Metadata["history.reasonCode"]);
            Assert.Equal(page.Items[0].HistoryEventId, page.Items[1].Causation!.HistoryEventId);
            Assert.Equal(page.Items[0].HistoryEventId, Assert.Single(h.Dispatcher.Requests).HistoryCausation!.HistoryEventId);
            Assert.All(page.Items, item => Assert.DoesNotContain("secret", item.Metadata.Keys));
        });
    }

    [Fact]
    public async Task Future_timer_schedule_is_published_without_firing_or_retiring_the_live_timer()
    {
        await using var h = new Harness(); await h.Seed("run", head: true); await h.Schedule("run", future: true);
        await h.Pump.ExecuteAsync(CancellationToken.None);
        await h.InScope(async services =>
        {
            var owner = (IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>();
            Assert.NotNull(await owner.FindAsync("run", "timer")); Assert.False(await owner.HasPendingHistoryAsync("run"));
            Assert.Equal("TimerScheduled", Assert.Single((await services.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("run"))).Items).Name);
        });
        Assert.Empty(h.Dispatcher.Requests);
    }

    [Fact]
    public async Task Missing_native_head_keeps_both_source_facts_owed_until_later_publication()
    {
        await using var h = new Harness(); await h.Seed("run", head: false); await h.Schedule("run");
        await h.Pump.ExecuteAsync(CancellationToken.None);
        await h.InScope(async services =>
        {
            var owner = (IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>();
            var timer = Assert.Single((await owner.ListPendingHistoryAsync(new())).Items);
            Assert.True(timer.HistoryResponsibility!.Retired); Assert.Equal(2, timer.HistoryResponsibility.PendingEvents.Count);
            Assert.Null(await services.GetRequiredService<IRuntimeHistoryStore>().FindHeadAsync("run"));
        });
        await h.Head("run"); await h.Pump.ExecuteAsync(CancellationToken.None);
        await h.InScope(async services =>
        {
            Assert.False(await ((IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>()).HasPendingHistoryAsync("run"));
            Assert.Equal(2, (await services.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("run"))).Items.Count);
        });
        Assert.Single(h.Dispatcher.Requests);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failed_or_lost_observation_acknowledgement_leaves_source_owed_and_replays_without_duplicate_events(bool afterCommit)
    {
        await using var h = new Harness(); await h.Seed("run", head: true); await h.Schedule("run");
        h.Fault.Armed = true; h.Fault.AfterCommit = afterCommit;
        await h.Pump.ExecuteAsync(CancellationToken.None); Assert.Empty(h.Dispatcher.Requests);
        await h.InScope(async services => Assert.True(await ((IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>()).HasPendingHistoryAsync("run")));
        h.Fault.Armed = false; h.Clock.Advance(TimeSpan.FromSeconds(20)); await h.Pump.ExecuteAsync(CancellationToken.None);
        await h.InScope(async services =>
        {
            Assert.False(await ((IDurableTimerHistoryStore)services.GetRequiredService<IDurableTimerStore>()).HasPendingHistoryAsync("run"));
            Assert.Equal(new[] { "TimerScheduled", "TimerDispatched" },
                (await services.GetRequiredService<IRuntimeHistoryStore>().ReadPageAsync(new("run"))).Items.Select(item => item.Name));
        });
        Assert.Single(h.Dispatcher.Requests);
    }

    private sealed class Harness : IAsyncDisposable
    {
        internal FakeTimeProvider Clock { get; } = new(At);
        internal Dispatcher Dispatcher { get; }
        internal ObservationFault Fault { get; } = new();
        internal LogSink Log { get; } = new();
        internal List<int> DueQueryLimits { get; } = [];
        private readonly ServiceProvider provider;
        internal DurableTimerPumpTask Pump => Assert.Single(provider.GetServices<IRecurringTask>().OfType<DurableTimerPumpTask>());
        internal Harness(BookmarkResumeDispatchStatus status = BookmarkResumeDispatchStatus.Dispatched, bool claimed = true, int maxTimersPerTick = 100)
        {
            Dispatcher = new(status); var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(Log);
            services.AddSingleton(typeof(ILogger<>), typeof(CaptureLogger<>));
            services.AddPersistenceCore("scope"); services.AddSingleton<TimeProvider>(Clock);
            services.AddWorkflowRuntime(); services.AddWorkflowRuntimeHistory();
            services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "timer-pump-tests-controlled-key-at-least-32-bytes");
            services.AddSingleton<IDeferredEventPublisher>(new Events()); services.AddSingleton<IBookmarkResumeDispatcher>(Dispatcher);
            new WorkflowsRuntimeSchedulingFeature().ConfigureServices(services);
            services.Configure<Options.DurableTimerPumpOptions>(options => options.MaxTimersPerTick = maxTimersPerTick);
            var original = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IRuntimeHistoryObservationWriter));
            services.Remove(original);
            services.AddScoped<IRuntimeHistoryObservationWriter>(services =>
            {
                if (Fault.FactoryArmed) { Fault.FactoryReads++; throw new IOException("controlled optional history factory outage"); }
                return new Observer((IRuntimeHistoryObservationWriter)original.ImplementationFactory!(services), Fault);
            });
            if (!claimed)
            {
                var timer = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDurableTimerStore));
                services.Remove(timer);
                services.AddScoped<IDurableTimerStore>(services => new LegacyHistoryOwner((IDurableTimerHistoryStore)timer.ImplementationFactory!(services), DueQueryLimits));
            }
            provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }
        internal async Task InScope(Func<IServiceProvider, Task> action) =>
            await provider.GetRequiredService<IPersistenceScopeRunner>().RunAsync(async (_, operation, _) => await action(operation.ServiceProvider));
        internal async Task Seed(string run, bool head, bool captured = true)
        {
            await InScope(async services =>
            {
                _ = services.GetRequiredService<IRuntimeCheckpointCommitStore>();
                await services.GetRequiredService<IWorkflowExecutionStateStore>().SaveAsync(new(run, new("artifact", "definition", "version", "1", "hash"),
                    WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, "scope", new Dictionary<string, string>())
                    { HistoryCapturePolicy = captured ? Policy : null, HistoryCaptureEnabledAtAdmission = captured });
                await services.GetRequiredService<IRuntimeExecutionOwnershipService>().AcquireAsync(run);
            });
            if (head) await Head(run);
        }
        internal Task Head(string run) => InScope(async services =>
        {
            var commit = new RuntimeCheckpointCommit("initial-" + run, new("initial-" + run, "initial", run, At, [], new Dictionary<string, string>()),
                new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()) { HistoryBatches = [new(Policy, "initial-" + run, At, true, [])] };
            await services.GetRequiredService<IRuntimeCheckpointCommitStore>().CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        });
        internal Task Schedule(string run, bool future = false, string timerId = "timer") => InScope(async services =>
        {
            var timer = await services.GetRequiredService<IDurableTimerScheduler>().ScheduleAsync(new(timerId, run, "Timer", timerId, future ? At.AddHours(1) : At, At,
                Metadata: new Dictionary<string, string> { ["secret"] = "business-data" }));
            Assert.NotNull(timer.HistoryResponsibility);
        });
        public ValueTask DisposeAsync() => provider.DisposeAsync();
    }
    private sealed class Dispatcher(BookmarkResumeDispatchStatus status) : IBookmarkResumeDispatcher
    {
        internal List<BookmarkResumeDispatchRequest> Requests { get; } = [];
        internal bool HistorySupported { get; set; } = true;
        public bool CanRecordHistory(IRuntimeHistoryObservationWriter writer, IRuntimeHistoryStore reader) => HistorySupported && writer.CanRecordHistory(reader);
        public ValueTask<BookmarkResumeDispatchResult> DispatchAsync(BookmarkResumeDispatchRequest request,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (status != BookmarkResumeDispatchStatus.Dispatched)
                return ValueTask.FromResult(new BookmarkResumeDispatchResult(status, request.WorkflowExecutionId, reason: "controlled-terminal-outcome"));
            var metadata = new Dictionary<string, string>();
            var bookmark = new BookmarkState("bookmark", request.WorkflowExecutionId, "activity", "node", "resume", "Timer", "timer", null, metadata, At, null);
            var node = new ExecutableNode("node", "authored", "Delay", "1.0.0", "controlled", JsonSerializer.SerializeToElement(new { }),
                new Dictionary<string, RuntimeInputBinding>(), metadata);
            var resolution = new BookmarkResumeResolution(bookmark, node, new("resume", "node", "controlled", metadata), null);
            return ValueTask.FromResult(new BookmarkResumeDispatchResult(status, request.WorkflowExecutionId, bookmark, resolution,
                new("envelope", request.WorkflowExecutionId, WorkflowExecutionCommandDispatchStatus.Accepted, At),
                new(request.WorkflowExecutionId, "actor", "controlled", WorkflowExecutionActorStatus.Active, WorkflowExecutionActorCapabilities.InProcessMailbox, At)));
        }
    }
    internal sealed class LogSink { internal List<string> Errors { get; } = []; }
    private sealed class CaptureLogger<T>(LogSink sink) : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Warning;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        { if (level >= LogLevel.Warning) sink.Errors.Add(formatter(state, exception) + "\n" + exception); }
    }
    private sealed class ObservationFault { internal bool Armed; internal bool AfterCommit; internal string? Run; internal bool FactoryArmed; internal int FactoryReads; }
    private sealed class Observer(IRuntimeHistoryObservationWriter inner, ObservationFault fault) : IRuntimeHistoryObservationWriter
    {
        public bool CanRecordHistory(IRuntimeHistoryStore reader) => inner.CanRecordHistory(reader);
        public async ValueTask<RuntimeHistoryObservationResult> RecordAsync(RuntimeHistoryObservation observation, CancellationToken cancellationToken = default)
        {
            if (fault.Armed && (fault.Run is null || fault.Run == observation.Event.WorkflowExecutionId) && !fault.AfterCommit) throw new RuntimeHistoryUnavailableException(observation.Event.WorkflowExecutionId, "controlled-pump-write", new IOException("controlled write outage"));
            var result = await inner.RecordAsync(observation, cancellationToken);
            if (fault.Armed && (fault.Run is null || fault.Run == observation.Event.WorkflowExecutionId)) throw new RuntimeHistoryUnavailableException(observation.Event.WorkflowExecutionId, "controlled-pump-reply", new IOException("controlled lost reply"));
            return result;
        }
    }
    private sealed class Events : IDeferredEventPublisher
    { public Task Publish(IEvent notification, CancellationToken cancellationToken = default) => Task.CompletedTask; }
}
