using Elsa.Tasks.Core;
using Elsa.Tasks.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Tasks.Tests;

[Collection(StartupTaskRuns.Name)]
public sealed class TaskManagerLifetimeTests
{
    [Fact]
    public async Task Activation_cancellation_does_not_stop_shell_tasks_and_a_cancelled_stop_waiter_does_not_abandon_stop()
    {
        using var activation = new CancellationTokenSource();
        var background = new BackgroundProbe(blockStop: true);
        var recurring = new RecurringProbe();
        await using var provider = CreateProvider(background, recurring);
        var manager = provider.GetRequiredService<ITaskManager>();

        try
        {
            await manager.StartExecutingRegisteredTasks(activation.Token);
            await background.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var recurringRun = recurring.RunScheduledAsync();
            await recurring.ExecutionStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            activation.Cancel();

            Assert.False(background.ExecutionToken.IsCancellationRequested);
            Assert.False(background.ExecutionEnded.Task.IsCompleted);
            Assert.False(recurring.ExecutionToken.IsCancellationRequested);
            Assert.False(recurring.ExecutionEnded.Task.IsCompleted);
            Assert.False(recurringRun.IsCompleted);

            using var firstWaiter = new CancellationTokenSource();
            var cancelledStopWaiter = manager.StopExecutingRegisteredTasks(firstWaiter.Token);
            await background.StopEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var sharedStopWaiter = manager.StopExecutingRegisteredTasks();

            firstWaiter.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledStopWaiter);
            Assert.False(sharedStopWaiter.IsCompleted);
            Assert.False(background.ExecutionToken.IsCancellationRequested);
            Assert.False(recurring.ExecutionToken.IsCancellationRequested);

            background.AllowStopToComplete();
            await sharedStopWaiter.WaitAsync(TimeSpan.FromSeconds(5));
            await background.ExecutionEnded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await recurring.ExecutionEnded.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await recurringRun.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(background.TokenWasCancelledWhenStopReturned);
            Assert.True(background.ExecutionToken.IsCancellationRequested);
            Assert.True(recurring.ExecutionToken.IsCancellationRequested);
            Assert.True(recurring.TokenWasCancelledWhenStopWasCalled);
        }
        finally
        {
            // Keep a failed assertion from leaving provider disposal blocked in the graceful-stop gate.
            background.AllowStopToComplete();
            await manager.StopExecutingRegisteredTasks().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task Caller_cancellation_during_startup_prevents_background_and_recurring_tasks_from_starting()
    {
        using var activation = new CancellationTokenSource();
        var startup = new BlockingStartupProbe();
        var background = new BackgroundProbe();
        var recurring = new RecurringProbe();
        await using var provider = CreateProvider(background, recurring, startup);
        var manager = provider.GetRequiredService<ITaskManager>();

        var start = manager.StartExecutingRegisteredTasks(activation.Token);
        await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        activation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(startup.CancellationObserved.Task.IsCompleted);
        Assert.Equal(0, background.StartCount);
        Assert.Equal(0, background.ExecuteCount);
        Assert.Equal(0, recurring.StartCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_stop_cancels_startup_and_unblocks_the_start_call(bool failCancellationCallback)
    {
        var startup = new BlockingStartupProbe(failCancellationCallback);
        var background = new BackgroundProbe();
        var recurring = new RecurringProbe();
        await using var provider = CreateProvider(background, recurring, startup);
        var manager = provider.GetRequiredService<ITaskManager>();
        using var startCancellation = new CancellationTokenSource();

        var start = manager.StartExecutingRegisteredTasks(startCancellation.Token);
        await startup.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var stop = manager.StopExecutingRegisteredTasks();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(TimeSpan.FromSeconds(5)));
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(startup.CancellationObserved.Task.IsCompleted);
            Assert.Equal(0, background.StartCount);
            Assert.Equal(0, recurring.StartCount);
        }
        finally
        {
            // The caller token is a cleanup path if the regression prevents Stop from cancelling startup.
            startCancellation.Cancel();
            try
            {
                await start.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (OperationCanceledException)
            {
            }

            await manager.StopExecutingRegisteredTasks().WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_or_cancelled_background_start_does_not_execute_the_task(bool cancelStart)
    {
        var background = new BackgroundProbe(
            startFailure: cancelStart
                ? Task.FromCanceled(new CancellationToken(canceled: true))
                : Task.FromException(new InvalidOperationException("Background start failed.")));
        await using var provider = CreateProvider(background);
        var manager = provider.GetRequiredService<ITaskManager>();

        var failure = await Record.ExceptionAsync(() => manager.StartExecutingRegisteredTasks());
        if (cancelStart)
            Assert.IsAssignableFrom<OperationCanceledException>(failure);
        else
            Assert.IsType<InvalidOperationException>(failure);
        await background.StartEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(background.StopEntered.Task.IsCompleted);
        Assert.Equal(0, background.ExecuteCount);

        await manager.StopExecutingRegisteredTasks().WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Faulted_background_execution_does_not_skip_lifetime_cancellation_or_recurring_cleanup()
    {
        var background = new BackgroundProbe(executionFailure: new IOException("Background execution failed."));
        var recurring = new RecurringProbe();
        await using var provider = CreateProvider(background, recurring);
        var manager = provider.GetRequiredService<ITaskManager>();

        await manager.StartExecutingRegisteredTasks();
        await background.ExecutionEnded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await manager.StopExecutingRegisteredTasks().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(background.StopEntered.Task.IsCompleted);
        Assert.True(background.ExecutionToken.IsCancellationRequested);
        Assert.Equal(1, recurring.StopCount);
    }

    private static ServiceProvider CreateProvider(
        BackgroundProbe? background = null,
        RecurringProbe? recurring = null,
        IStartupTask? startup = null)
    {
        var services = TaskServices.Compose(new UnusedLockProvider());
        if (background is not null)
            services.AddSingleton<IBackgroundTask>(background);
        if (recurring is not null)
            services.AddSingleton<IRecurringTask>(recurring);
        if (startup is not null)
            services.AddSingleton(startup);
        return services.BuildServiceProvider();
    }

    private sealed class BlockingStartupProbe(bool failCancellationCallback = false) : IStartupTask
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            using var registration = failCancellationCallback
                ? cancellationToken.Register(static () => throw new IOException("Startup cancellation callback failed."))
                : default;
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved.TrySetResult();
                throw;
            }
        }
    }

    private sealed class BackgroundProbe(bool blockStop = false, Task? startFailure = null, Exception? executionFailure = null) : IBackgroundTask
    {
        private readonly TaskCompletionSource _stopGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _stopRequested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource StartEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ExecutionEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StopEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartCount { get; private set; }
        public int ExecuteCount { get; private set; }
        public CancellationToken ExecutionToken { get; private set; }
        public bool TokenWasCancelledWhenStopReturned { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            StartEntered.TrySetResult();
            return startFailure ?? Task.CompletedTask;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            ExecuteCount++;
            ExecutionToken = cancellationToken;
            ExecutionStarted.TrySetResult();

            try
            {
                if (executionFailure is not null)
                    throw executionFailure;

                var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
                var completed = await Task.WhenAny(_stopRequested.Task, cancelled.Task);
                if (completed == cancelled.Task)
                    cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                ExecutionEnded.TrySetResult();
            }
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            StopEntered.TrySetResult();
            if (blockStop)
                await _stopGate.Task;

            TokenWasCancelledWhenStopReturned = ExecutionToken.IsCancellationRequested;
            _stopRequested.TrySetResult();
        }

        public void AllowStopToComplete() => _stopGate.TrySetResult();
    }

    private sealed class RecurringProbe : IRecurringTask
    {
        private Func<Task>? _scheduledAction;

        public TaskCompletionSource ExecutionStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ExecutionEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public CancellationToken ExecutionToken { get; private set; }
        public bool TokenWasCancelledWhenStopWasCalled { get; private set; }

        public ITaskSchedule GetSchedule() => new CapturingSchedule(action => _scheduledAction = action);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            StartCount++;
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            TokenWasCancelledWhenStopWasCalled = ExecutionToken.IsCancellationRequested;
            return Task.CompletedTask;
        }

        public async Task ExecuteAsync(CancellationToken cancellationToken)
        {
            ExecutionToken = cancellationToken;
            ExecutionStarted.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                ExecutionEnded.TrySetResult();
                throw;
            }
        }

        public Task RunScheduledAsync() => (_scheduledAction ?? throw new InvalidOperationException("The recurring task has not been scheduled."))();
    }

    private sealed class CapturingSchedule(Action<Func<Task>> capture) : ITaskSchedule
    {
        public IScheduledTaskExecution ScheduleExecution(Func<Task> action)
        {
            capture(action);
            return NoopScheduledTaskExecution.Instance;
        }
    }

    private sealed class NoopScheduledTaskExecution : IScheduledTaskExecution
    {
        public static NoopScheduledTaskExecution Instance { get; } = new();
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
