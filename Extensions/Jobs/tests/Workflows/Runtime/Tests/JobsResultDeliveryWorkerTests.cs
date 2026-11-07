using System.Collections.Concurrent;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Jobs.Workflows.Runtime.Tests;

public sealed class JobsResultDeliveryWorkerTests
{
    [Fact]
    public async Task First_item_timeout_keeps_that_result_owed_while_healthy_same_page_result_is_acknowledged_before_any_poll_delay()
    {
        await using var harness = await Harness.CreateAsync();
        try
        {
            await harness.Worker.StartAsync(CancellationToken.None);
            await Wait(harness.FirstEntered.Task);
            harness.Clock.Advance(Harness.Timeout);
            var acknowledged = await Wait(harness.Transport.Acknowledged.Task);
            await Wait(harness.PollInstalled.Task);
            Assert.Equal("canonical-winner-job", acknowledged.JobId);
            Assert.Equal(JobDeliveryProofKind.ParentProvenTerminal, acknowledged.Proof.Kind);
            Assert.Equal(NativeFixture.Now.Add(Harness.Timeout), harness.Clock.GetUtcNow());
            Assert.Equal(1, harness.Pages.Count); Assert.Null(Assert.Single(harness.Pages).ContinuationToken);
            Assert.DoesNotContain(harness.Transport.Acknowledgements, item => item.JobId == Harness.BlockedJobId);
            Assert.True(harness.Transport.Delivery!.IsAcknowledged);
            // Its result remains pending on the next sweep; already-ACKed healthy work is excluded.
            harness.Clock.Advance(Harness.PollInterval);
            await Wait(harness.Reentered.Task);
            Assert.Equal(2, harness.Pages.Count); Assert.Single(harness.Transport.Acknowledgements);
            await Wait(harness.Worker.StopAsync(CancellationToken.None));
        }
        finally { await Wait(harness.Worker.StopAsync(CancellationToken.None)); }
    }

    [Fact]
    public async Task Stop_cancels_the_blocked_item_and_drains_without_waiting_for_its_timeout_or_starting_the_next_item()
    {
        await using var harness = await Harness.CreateAsync();
        try
        {
            await harness.Worker.StartAsync(CancellationToken.None);
            await Wait(harness.FirstEntered.Task);
            await Wait(harness.Worker.StopAsync(CancellationToken.None));
            await Wait(harness.FirstCancelled.Task);
            Assert.Equal(NativeFixture.Now, harness.Clock.GetUtcNow());
            Assert.Empty(harness.Transport.Acknowledgements); Assert.Single(harness.Pages);
        }
        finally { await Wait(harness.Worker.StopAsync(CancellationToken.None)); }
    }

    private static Task Wait(Task task) => task.WaitAsync(TimeSpan.FromSeconds(10));
    private static Task<T> Wait<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(10));

    private sealed class Harness(ServiceProvider provider, JobsResultDeliveryWorker worker, JobTransport transport,
        FakeTimeProvider clock, TaskCompletionSource firstEntered, TaskCompletionSource reentered, TaskCompletionSource firstCancelled,
        TaskCompletionSource pollInstalled, ConcurrentQueue<JobPageRequest> pages) : IAsyncDisposable
    {
        internal const string BlockedJobId = "unavailable-first-job";
        internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);
        internal static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
        internal JobsResultDeliveryWorker Worker { get; } = worker;
        internal JobTransport Transport { get; } = transport;
        internal FakeTimeProvider Clock { get; } = clock;
        internal TaskCompletionSource FirstEntered { get; } = firstEntered;
        internal TaskCompletionSource Reentered { get; } = reentered;
        internal TaskCompletionSource FirstCancelled { get; } = firstCancelled;
        internal TaskCompletionSource PollInstalled { get; } = pollInstalled;
        internal ConcurrentQueue<JobPageRequest> Pages { get; } = pages;

        internal static async Task<Harness> CreateAsync()
        {
            var (saved, admission, _) = await NativeFixture.SavedAsync(); var (transport, _) = NativeFixture.Selected(admission);
            var clock = new FakeTimeProvider(NativeFixture.Now);
            var first = Signal(); var repeat = Signal(); var cancelled = Signal(); var delayed = Signal();
            var pages = new ConcurrentQueue<JobPageRequest>(); var attempts = 0;
            var neverCompletes = Signal();
            transport.Find = async (_, jobId, cancellationToken) =>
            {
                if (jobId != BlockedJobId) return transport.Snapshot;
                if (Interlocked.Increment(ref attempts) == 1) first.TrySetResult(); else repeat.TrySetResult();
                try { await neverCompletes.Task.WaitAsync(cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { cancelled.TrySetResult(); throw; }
                throw new InvalidOperationException("The controlled blocked read cannot complete normally.");
            };
            transport.PendingDeliveries = (_, page, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested(); pages.Enqueue(page);
                var healthy = transport.Delivery!;
                JobDeliverySnapshot blocked = new(BlockedJobId, "blocked-result", healthy.Association, null);
                return ValueTask.FromResult(new JobPage<JobDeliverySnapshot>(healthy.IsAcknowledged ? [blocked]
                    : [blocked, new(healthy.JobId, healthy.ResultId, healthy.Association, healthy.Permission)], null));
            };
            var services = new ServiceCollection(); services.AddPersistenceCore(NativeFixture.Scope.PersistencePartition);
            services.AddSingleton<IJobCommands>(transport); services.AddSingleton<IJobQueries>(transport);
            services.AddSingleton<TimeProvider>(new ObservingClock(clock, due => { if (due == PollInterval) delayed.TrySetResult(); }));
            services.AddScoped<JobsNativeDeliveryInspector>(scope => new(new WorkflowStore(NativeFixture.Parent(WorkflowExecutionStatus.Cancelled)),
                null!, new ActivityStore(), new BookmarkStore(NativeFixture.Bookmark(saved)), scope.GetRequiredService<IPersistenceAccessContextAccessor>(),
                transport, [], scope.GetRequiredService<IPersistenceOperationScopeFactory>()));
            services.AddJobsWorkflowsRuntime(options =>
            {
                options.EnableResultDelivery = true; options.DeliveryScopes.Add(NativeFixture.Scope);
                options.OperationTimeout = Timeout; options.PollInterval = PollInterval;
            });
            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            return new(provider, provider.GetRequiredService<JobsResultDeliveryWorker>(), transport, clock, first, repeat, cancelled, delayed, pages);
        }

        public async ValueTask DisposeAsync() => await provider.DisposeAsync();
        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ObservingClock(FakeTimeProvider clock, Action<TimeSpan> observe) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();
        public override long GetTimestamp() => clock.GetTimestamp();
        public override long TimestampFrequency => clock.TimestampFrequency;
        public override TimeZoneInfo LocalTimeZone => clock.LocalTimeZone;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = clock.CreateTimer(callback, state, dueTime, period); observe(dueTime); return timer;
        }
    }
}
