using System.Diagnostics;
using System.Text.Json;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>Candidate process ownership with stubbed OS handles and deterministic deadlines.</summary>
public sealed class CandidateProcessTests
{
    private static readonly HostLayout Host = new("/compiled/host", "Example.Host",
        "/compiled/host/Example.Host.runtimeconfig.json", "/compiled/host/Example.Host.deps.json");

    [Fact]
    public async Task Complete_exchange_sends_only_private_stdin_and_owns_all_streams()
    {
        using var fixture = new ProcessFixture();
        var result = await fixture.Runner.RunAsync(Host, fixture.Request);

        Assert.Equal(0, result.ExitCode);
        Assert.Null(result.Error);
        Assert.Equal(fixture.HostResponse.ToJsonString(), result.Tooling!.Value.GetRawText());
        Assert.Equal(CandidateWorkerProcess.Arguments(Host, fixture.WorkerAssembly), fixture.StartInfo!.ArgumentList);
        Assert.True(fixture.StartInfo.RedirectStandardInput);
        Assert.True(fixture.StartInfo.RedirectStandardOutput);
        Assert.True(fixture.StartInfo.RedirectStandardError);
        Assert.False(fixture.StartInfo.UseShellExecute);
        Assert.All(fixture.StartInfo.ArgumentList, value => Assert.DoesNotContain("private-process-canary", value));
        using var sent = JsonDocument.Parse(fixture.Handle.Input.ToArray());
        Assert.Equal(fixture.Request.Candidate!.CaptureId,
            sent.RootElement.GetProperty("candidate").GetProperty("captureId").GetString());
        fixture.AssertClosed();
    }

    [Fact]
    public async Task Stderr_is_drained_and_discarded_without_a_response_size_limit()
    {
        using var fixture = new ProcessFixture();
        var stderr = new GeneratedOutput(4 * 1024 * 1024 + 1);
        fixture.Handle.Error = stderr;
        var result = await fixture.Runner.RunAsync(Host, fixture.Request);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(4 * 1024 * 1024 + 1, stderr.BytesRead);
        fixture.AssertClosed();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public async Task Invalid_timeout_refuses_before_launch(int seconds)
    {
        using var fixture = new ProcessFixture();
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => fixture.Runner.RunAsync(Host, fixture.Request, seconds));
        Assert.Equal(2, refusal.ExitCode);
        Assert.Equal("candidate-request-invalid", refusal.Code);
        Assert.Equal(0, fixture.StartCount);
    }

    [Fact]
    public async Task Invalid_or_oversized_request_refuses_before_starting_a_child()
    {
        using var fixture = new ProcessFixture();
        foreach (var request in new[]
                 {
                     fixture.Request with { Restore = true },
                     fixture.Request with { HostDirectory = new string('x', 8 * 1024 * 1024) }
                 })
        {
            var refusal = await Assert.ThrowsAsync<CliRefusal>(() => fixture.Runner.RunAsync(Host, request));
            Assert.Equal(2, refusal.ExitCode);
            Assert.Contains(refusal.Code, new[] { "candidate-request-invalid", "candidate-request-too-large" });
            Assert.Equal(0, fixture.StartCount);
        }
    }

    [Fact]
    public async Task Precancelled_request_does_not_launch()
    {
        using var fixture = new ProcessFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() =>
            fixture.Runner.RunAsync(Host, fixture.Request, cancellationToken: cancellation.Token));
        Assert.Equal("candidate-inspection-cancelled", refusal.Code);
        Assert.Equal(2, refusal.ExitCode);
        Assert.Equal(0, fixture.StartCount);
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("stdout")]
    [InlineData("stderr")]
    [InlineData("wait")]
    public async Task Cancellation_at_each_exchange_stage_cleans_the_owned_handle(string stage)
    {
        using var fixture = new ProcessFixture();
        fixture.Block(stage);
        using var cancellation = new CancellationTokenSource();
        var pending = fixture.Runner.RunAsync(Host, fixture.Request, cancellationToken: cancellation.Token);
        await fixture.AwaitStageOrCompletion(pending);
        cancellation.Cancel();
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => pending);
        Assert.Equal("candidate-inspection-cancelled", refusal.Code);
        Assert.True(fixture.StageEntered.Task.IsCompletedSuccessfully);
        Assert.Equal(2, refusal.ExitCode);
        fixture.AssertClosed();
        Assert.Equal(1, fixture.Handle.KillCount);
    }

    [Fact]
    public async Task Deadline_bounds_a_stdout_stream_that_ignores_cancellation()
    {
        using var fixture = new ProcessFixture();
        var release = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handle.Output = new ControlledStream { Read = (_, _) => new ValueTask<int>(release.Task) };
        fixture.Handle.OnDispose = () => release.TrySetResult(0);
        fixture.Handle.Wait = token => fixture.Handle.Exited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token);
        var pending = fixture.Runner.RunAsync(Host, fixture.Request, 1);
        await fixture.AwaitLaunchOrCompletion(pending);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => pending);
        Assert.Equal("candidate-inspection-timeout", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        fixture.AssertClosed();
        Assert.Equal(1, fixture.Handle.KillCount);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("oversized")]
    public async Task Invalid_stdout_is_never_echoed_and_the_handle_is_closed(string kind)
    {
        using var fixture = new ProcessFixture();
        fixture.Handle.Output = kind == "malformed"
            ? new MemoryStream("private-process-canary"u8.ToArray())
            : new GeneratedOutput(4 * 1024 * 1024 + 1);
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => fixture.Runner.RunAsync(Host, fixture.Request));
        Assert.Equal(kind == "malformed" ? "candidate-response-invalid" : "candidate-response-too-large", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        Assert.DoesNotContain("private-process-canary", refusal.Message);
        fixture.AssertClosed();
    }

    [Fact]
    public async Task Disposal_failure_overrides_a_valid_response()
    {
        using var fixture = new ProcessFixture();
        fixture.Handle.OnDispose = () => throw new IOException("private-process-canary");
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => fixture.Runner.RunAsync(Host, fixture.Request));
        Assert.Equal("candidate-cleanup-failed", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        Assert.DoesNotContain("private-process-canary", refusal.Message);
    }

    [Fact]
    public async Task Cancellation_during_start_still_owns_the_returned_handle()
    {
        using var fixture = new ProcessFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.OnStart = cancellation.Cancel;
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() =>
            fixture.Runner.RunAsync(Host, fixture.Request, cancellationToken: cancellation.Token));
        Assert.Equal("candidate-inspection-cancelled", refusal.Code);
        fixture.AssertClosed();
        Assert.Equal(1, fixture.Handle.KillCount);
    }

    [Fact]
    public async Task Required_arguments_are_guarded()
    {
        using var fixture = new ProcessFixture();
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Runner.RunAsync(null!, fixture.Request));
        await Assert.ThrowsAsync<ArgumentNullException>(() => fixture.Runner.RunAsync(Host, null!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_start_does_not_echo_the_launcher_exception(bool returnsNull)
    {
        using var fixture = new ProcessFixture();
        var launches = 0;
        var runner = new CandidateWorkerProcess(_ =>
        {
            launches++;
            if (returnsNull) return null!;
            throw new IOException("private-process-canary");
        }, fixture.WorkerAssembly, fixture.Clock);
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => runner.RunAsync(Host, fixture.Request));
        Assert.Equal("candidate-inspection-failed", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        Assert.Equal(1, launches);
        Assert.DoesNotContain("private-process-canary", refusal.Message);
    }

    [Fact]
    public async Task Failure_to_observe_exit_still_disposes_owned_streams_and_handle()
    {
        using var fixture = new ProcessFixture();
        fixture.Handle.ObserveExit = () => throw new IOException("private-process-canary");
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => fixture.Runner.RunAsync(Host, fixture.Request));
        Assert.Equal("candidate-cleanup-failed", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        Assert.DoesNotContain("private-process-canary", refusal.Message);
        fixture.AssertClosed();
    }

    [Fact]
    public async Task Exit_code_failure_is_safe_after_proven_cleanup()
    {
        using var fixture = new ProcessFixture();
        fixture.Handle.ObserveExitCode = () => throw new IOException("private-process-canary");
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => fixture.Runner.RunAsync(Host, fixture.Request));
        Assert.Equal("candidate-inspection-failed", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        Assert.DoesNotContain("private-process-canary", refusal.Message);
        fixture.AssertClosed();
    }

    [Fact]
    public async Task Failure_to_terminate_overrides_cancellation_and_disposes_the_handle()
    {
        using var fixture = new ProcessFixture();
        fixture.Block("wait");
        fixture.Handle.OnKill = () => throw new IOException("private-process-canary");
        using var cancellation = new CancellationTokenSource();
        var pending = fixture.Runner.RunAsync(Host, fixture.Request, cancellationToken: cancellation.Token);
        await fixture.AwaitLaunchOrCompletion(pending);
        cancellation.Cancel();
        await Task.WhenAny(pending, fixture.Clock.CleanupTimerCreated.Task).WaitAsync(TimeSpan.FromSeconds(30));
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
        Assert.Equal("candidate-cleanup-failed", refusal.Code);
        Assert.DoesNotContain("private-process-canary", refusal.Message);
        Assert.Equal(1, fixture.Handle.DisposeCount);
        Assert.False(fixture.Handle.Input.CanWrite);
        Assert.False(fixture.Handle.Output.CanRead);
        Assert.False(fixture.Handle.Error.CanRead);
    }

    [Fact]
    public async Task Fatal_start_failures_are_not_reclassified_as_user_refusals()
    {
        using var fixture = new ProcessFixture();
        var runner = new CandidateWorkerProcess(_ => throw new OutOfMemoryException("fatal-sentinel"),
            fixture.WorkerAssembly, fixture.Clock);
        await Assert.ThrowsAsync<OutOfMemoryException>(() => runner.RunAsync(Host, fixture.Request));
    }

    [Fact]
    public async Task An_io_task_that_survives_disposal_cannot_count_as_successful_cleanup()
    {
        using var fixture = new ProcessFixture();
        var never = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handle.Output = new ControlledStream { Read = (_, _) => new ValueTask<int>(never.Task) };
        fixture.Handle.Wait = token => fixture.Handle.Exited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token);
        var pending = fixture.Runner.RunAsync(Host, fixture.Request, 1);
        try
        {
            await fixture.AwaitLaunchOrCompletion(pending);
            fixture.Clock.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(pending, fixture.Clock.CleanupTimerCreated.Task).WaitAsync(TimeSpan.FromSeconds(30));
            fixture.Clock.Advance(TimeSpan.FromSeconds(5));
            var refusal = await Assert.ThrowsAsync<CliRefusal>(() => pending.WaitAsync(TimeSpan.FromSeconds(30)));
            Assert.Equal("candidate-cleanup-failed", refusal.Code);
            fixture.AssertClosed();
        }
        finally
        {
            never.TrySetResult(0);
        }
    }

    [Fact]
    public async Task A_later_fatal_stream_getter_is_not_hidden_by_an_earlier_io_error()
    {
        using var fixture = new ProcessFixture();
        fixture.Handle.InputLookup = () => throw new IOException("private-process-canary");
        fixture.Handle.ErrorLookup = () => throw new OutOfMemoryException("fatal-sentinel");
        await Assert.ThrowsAsync<OutOfMemoryException>(() => fixture.Runner.RunAsync(Host, fixture.Request));
        Assert.Equal(1, fixture.Handle.DisposeCount);
        Assert.False(fixture.Handle.Output.CanRead);
    }

    private sealed class ProcessFixture : IDisposable
    {
        public WorkerRequest Request { get; } = CandidateWorkerRequestFixture.Create();
        public FakeHandle Handle { get; }
        public ManualClock Clock { get; } = new();
        public string WorkerAssembly { get; } = Path.Join(AppContext.BaseDirectory, WorkerProcess.WorkerAssemblyFileName);
        public CandidateWorkerProcess Runner { get; }
        public System.Text.Json.Nodes.JsonObject HostResponse { get; }
        public ProcessStartInfo? StartInfo { get; private set; }
        public int StartCount { get; private set; }
        public Action? OnStart { get; set; }
        public TaskCompletionSource StageEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProcessFixture()
        {
            HostResponse = CandidateHostResponseFixtures.Success(Request.Candidate!);
            Handle = new FakeHandle
            {
                Output = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new WorkerResponse
                {
                    Tooling = JsonSerializer.SerializeToElement(HostResponse)
                }, WorkerContract.Json))
            };
            Runner = new CandidateWorkerProcess(info =>
            {
                StartInfo = info;
                StartCount++;
                OnStart?.Invoke();
                started.TrySetResult();
                return Handle;
            }, WorkerAssembly, Clock);
        }

        public async Task AwaitLaunchOrCompletion(Task pending) =>
            await Task.WhenAny(pending, started.Task).WaitAsync(TimeSpan.FromSeconds(30));

        public async Task AwaitStageOrCompletion(Task pending) =>
            await Task.WhenAny(pending, StageEntered.Task).WaitAsync(TimeSpan.FromSeconds(30));

        public void Block(string stage)
        {
            Handle.Wait = token =>
            {
                if (stage == "wait") StageEntered.TrySetResult();
                return Handle.Exited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token);
            };
            if (stage == "write") Handle.Input = new ControlledStream
                { Write = (_, token) => { StageEntered.TrySetResult(); return new ValueTask(Task.Delay(Timeout.Infinite, token)); } };
            if (stage == "flush") Handle.Input = new ControlledStream
                { Flush = token => { StageEntered.TrySetResult(); return Task.Delay(Timeout.Infinite, token); } };
            if (stage == "stdout") Handle.Output = new ControlledStream
                { Read = async (_, token) => { StageEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0; } };
            if (stage == "stderr") Handle.Error = new ControlledStream
                { Read = async (_, token) => { StageEntered.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return 0; } };
        }

        public void AssertClosed()
        {
            Assert.True(Handle.Exited);
            Assert.Equal(1, Handle.DisposeCount);
            Assert.False(Handle.Input.CanWrite);
            Assert.False(Handle.Output.CanRead);
            Assert.False(Handle.Error.CanRead);
        }

        public void Dispose()
        {
            Handle.OnDispose = null;
            Handle.Input.Dispose();
            Handle.Output.Dispose();
            Handle.Error.Dispose();
        }
    }

    private sealed class FakeHandle : ICandidateProcessHandle
    {
        public ControlledStream Input { get; set; } = new();
        public Stream Output { get; set; } = new MemoryStream();
        public Stream Error { get; set; } = new MemoryStream();
        public bool Exited { get; set; }
        public int KillCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Func<CancellationToken, Task>? Wait { get; set; }
        public Action? OnDispose { get; set; }
        public Func<Stream>? InputLookup { get; set; }
        public Func<Stream>? ErrorLookup { get; set; }
        public Stream StandardInput => InputLookup?.Invoke() ?? Input;
        public Stream StandardOutput => Output;
        public Stream StandardError => ErrorLookup?.Invoke() ?? Error;
        public Func<bool>? ObserveExit { get; set; }
        public Func<int>? ObserveExitCode { get; set; }
        public Action? OnKill { get; set; }
        public bool HasExited => ObserveExit?.Invoke() ?? Exited;
        public int ExitCode => ObserveExitCode?.Invoke() ?? 0;
        public Task WaitForExitAsync(CancellationToken token)
        {
            if (Wait is not null) return Wait(token);
            Exited = true;
            return Task.CompletedTask;
        }
        public void KillTree() { KillCount++; OnKill?.Invoke(); Exited = true; }
        public void Dispose() { DisposeCount++; OnDispose?.Invoke(); }
    }

    private sealed class ControlledStream : MemoryStream
    {
        public new Func<Memory<byte>, CancellationToken, ValueTask<int>>? Read { get; init; }
        public new Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? Write { get; init; }
        public new Func<CancellationToken, Task>? Flush { get; init; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default) =>
            Read?.Invoke(buffer, token) ?? base.ReadAsync(buffer, token);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken token = default) =>
            Write?.Invoke(buffer, token) ?? base.WriteAsync(buffer, token);
        public override Task FlushAsync(CancellationToken token) => Flush?.Invoke(token) ?? base.FlushAsync(token);
    }

    private sealed class GeneratedOutput(int size) : MemoryStream
    {
        public int BytesRead { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            var length = Math.Min(buffer.Length, size - BytesRead);
            buffer.Span[..length].Fill((byte)'x');
            BytesRead += length;
            return ValueTask.FromResult(length);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private readonly object sync = new();
        private readonly List<ManualTimer> timers = [];
        private TimeSpan elapsed;
        public TaskCompletionSource CleanupTimerCreated { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (sync) return elapsed.Ticks; }
        public override DateTimeOffset GetUtcNow() { lock (sync) return DateTimeOffset.UnixEpoch + elapsed; }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (sync)
            {
                var timer = new ManualTimer(this, callback, state);
                timers.Add(timer);
                timer.Change(dueTime, period);
                if (dueTime == TimeSpan.FromSeconds(5)) CleanupTimerCreated.TrySetResult();
                return timer;
            }
        }
        public void Advance(TimeSpan duration)
        {
            ManualTimer[] due;
            lock (sync)
            {
                elapsed += duration;
                due = timers.Where(timer => timer.Due <= elapsed).ToArray();
                foreach (var timer in due) timer.Due = TimeSpan.MaxValue;
            }
            foreach (var timer in due) timer.Fire();
        }
        private sealed class ManualTimer(ManualClock owner, TimerCallback callback, object? state) : ITimer
        {
            public TimeSpan Due { get; set; } = TimeSpan.MaxValue;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (owner.sync) Due = dueTime == Timeout.InfiniteTimeSpan ? TimeSpan.MaxValue : owner.elapsed + dueTime;
                return true;
            }
            public void Fire() => callback(state);
            public void Dispose() { lock (owner.sync) Due = TimeSpan.MaxValue; }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
