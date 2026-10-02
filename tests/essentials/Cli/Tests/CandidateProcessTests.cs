using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>Candidate process ownership with stubbed OS handles and deterministic deadlines.</summary>
public sealed class CandidateProcessTests
{
    // Handle branch inventory: READY opcode/correlation validation, STATUS validation, and each
    // control-channel stage are direct task tests; pre-READY cleanup covers both live and exited
    // supervisors; scope/group termination retry tests protect the post-success flag; wait tests
    // cover scope/group cancellation and errors; constructor guards and disposal tests prove input
    // validation and every owned close is attempted.
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
    public async Task Explicit_environment_exchange_uses_the_capture_request_and_keeps_private_input_off_arguments()
    {
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        ConfigureEnvironmentSuccess(fixture, environment.Capture);

        var result = await fixture.Runner.RunEnvironmentAsync(environment.Capture, []);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(CandidateWorkerProcess.Arguments(Host, fixture.WorkerAssembly), fixture.StartInfo!.ArgumentList);
        Assert.DoesNotContain(environment.EnvironmentPath, fixture.StartInfo.ArgumentList);
        using var sent = JsonDocument.Parse(fixture.Handle.Input.ToArray());
        Assert.Equal(WorkerCommands.InspectCandidateEnvironment,
            sent.RootElement.GetProperty("command").GetString());
        Assert.Equal(Convert.ToBase64String(environment.RawEnvironment),
            sent.RootElement.GetProperty("environmentInput").GetProperty("content").GetString());
        Assert.DoesNotContain(environment.EnvironmentPath, sent.RootElement.GetRawText(), StringComparison.Ordinal);
        fixture.AssertClosed();
    }

    [Fact]
    public async Task Precancelled_environment_request_disposes_the_capture_without_launching()
    {
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var refusal = await Assert.ThrowsAsync<CliRefusal>(() =>
            fixture.Runner.RunEnvironmentAsync(environment.Capture, [], cancellationToken: cancellation.Token));

        Assert.Equal("candidate-inspection-cancelled", refusal.Code);
        Assert.Equal(0, fixture.StartCount);
        Assert.False(environment.Capture.HasEnvironmentInput);
        var reused = await Assert.ThrowsAsync<CliRefusal>(() =>
            fixture.Runner.RunEnvironmentAsync(environment.Capture, []));
        Assert.Equal("candidate-capture-invalid", reused.Code);
    }

    [Fact]
    public async Task Oversized_environment_request_refuses_before_launch()
    {
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();

        var refusal = await Assert.ThrowsAsync<CliRefusal>(() =>
            fixture.Runner.RunEnvironmentAsync(environment.Capture, [new string('p', 8 * 1024 * 1024)]));

        Assert.Equal("candidate-request-too-large", refusal.Code);
        Assert.Equal(0, fixture.StartCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_environment_request_transport_enforces_the_8MiB_boundary(bool oversized)
    {
        const int maximumBytes = 8 * 1024 * 1024;
        int baselineBytes;
        using (var baseline = new EnvironmentCaptureFixture())
        {
            var request = baseline.Capture.BeginEnvironmentInspection(["p"]);
            baselineBytes = JsonSerializer.SerializeToUtf8Bytes(request, WorkerContract.Json).Length;
        }

        var packageRootLength = checked(maximumBytes - baselineBytes + 1 + (oversized ? 1 : 0));
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        ConfigureEnvironmentSuccess(fixture, environment.Capture);

        if (oversized)
        {
            var refusal = await Assert.ThrowsAsync<CliRefusal>(() =>
                fixture.Runner.RunEnvironmentAsync(environment.Capture, [new string('p', packageRootLength)]));

            Assert.Equal("candidate-request-too-large", refusal.Code);
            Assert.Equal(0, fixture.StartCount);
            return;
        }

        var result = await fixture.Runner.RunEnvironmentAsync(environment.Capture, [new string('p', packageRootLength)]);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(maximumBytes, fixture.Handle.Input.ToArray().Length);
        fixture.AssertClosed();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_environment_response_transport_enforces_the_4MiB_boundary(bool oversized)
    {
        const int maximumBytes = 4 * 1024 * 1024;
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        var response = EnvironmentSuccessResponse(environment.Capture.Payload);
        fixture.SetOutputBytes(PadResponse(response, maximumBytes + (oversized ? 1 : 0)));

        if (oversized)
        {
            var refusal = await Assert.ThrowsAsync<CliRefusal>(() =>
                fixture.Runner.RunEnvironmentAsync(environment.Capture, []));

            Assert.Equal("candidate-response-too-large", refusal.Code);
            Assert.Equal(3, refusal.ExitCode);
        }
        else
        {
            var result = await fixture.Runner.RunEnvironmentAsync(environment.Capture, []);
            Assert.Equal(0, result.ExitCode);
        }

        fixture.AssertClosed();
    }

    [Fact]
    public async Task Explicit_environment_timeout_upper_endpoint_allows_launch()
    {
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        fixture.SetOutputBytes(EnvironmentSuccessResponse(environment.Capture.Payload));

        var result = await fixture.Runner.RunEnvironmentAsync(environment.Capture, [], timeoutSeconds: 300);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, fixture.StartCount);
        fixture.AssertClosed();
    }

    [Fact]
    public async Task Explicit_environment_drift_after_dispatch_is_refused_before_render()
    {
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        var response = EnvironmentSuccessResponse(environment.Capture.Payload);
        var mutated = 0;
        var offset = 0;
        fixture.Handle.Output = new ControlledStream
        {
            Read = (buffer, _) =>
            {
                if (Interlocked.Exchange(ref mutated, 1) == 0)
                    File.AppendAllText(environment.EnvironmentPath, " ");
                var read = Math.Min(buffer.Length, response.Length - offset);
                response.AsMemory(offset, read).CopyTo(buffer);
                offset += read;
                fixture.StageEntered.TrySetResult();
                return ValueTask.FromResult(read);
            }
        };

        // The fake child has completed the mechanical exchange. This is the command's final
        // capture recheck seam; a real Workbench child/host dispatch proof remains separate.
        _ = await fixture.Runner.RunEnvironmentAsync(environment.Capture, []);
        var refusal = Assert.Throws<CliRefusal>(() => environment.Capture.VerifyUnchanged());

        Assert.Equal("composition-input-changed", refusal.Code);
        Assert.False(environment.Capture.HasEnvironmentInput);
        Assert.DoesNotContain(environment.EnvironmentPath, refusal.ToString(), StringComparison.Ordinal);
        Assert.True(fixture.StageEntered.Task.IsCompletedSuccessfully);
        fixture.AssertClosed();
    }

    [Theory]
    [InlineData("write")]
    [InlineData("flush")]
    [InlineData("stdout")]
    [InlineData("stderr")]
    [InlineData("wait")]
    public async Task Explicit_environment_cancellation_at_each_exchange_stage_cleans_the_owned_handle(string stage)
    {
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        fixture.Block(stage);
        using var cancellation = new CancellationTokenSource();
        var pending = fixture.Runner.RunEnvironmentAsync(environment.Capture, [], cancellationToken: cancellation.Token);
        await fixture.AwaitStageOrCompletion(pending);

        cancellation.Cancel();
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => pending);

        Assert.Equal("candidate-inspection-cancelled", refusal.Code);
        Assert.Equal(2, refusal.ExitCode);
        fixture.AssertClosed();
        Assert.Equal(1, fixture.Handle.KillCount);
    }

    [Fact]
    public async Task Explicit_environment_deadline_bounds_a_stdout_stream_that_ignores_cancellation()
    {
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        fixture.Block("stdout");
        var pending = fixture.Runner.RunEnvironmentAsync(environment.Capture, [], timeoutSeconds: 1);
        await fixture.AwaitStageOrCompletion(pending);

        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        var refusal = await Assert.ThrowsAsync<CliRefusal>(() => pending);

        Assert.Equal("candidate-inspection-timeout", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        fixture.AssertClosed();
        Assert.Equal(1, fixture.Handle.KillCount);
    }

    [Fact]
    public async Task Explicit_environment_late_response_is_bounded_by_cleanup_after_timeout()
    {
        using var environment = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        var never = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var readCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handle.Output = new ControlledStream
        {
            Read = async (_, _) =>
            {
                fixture.StageEntered.TrySetResult();
                try { return await never.Task; }
                finally { readCompleted.TrySetResult(); }
            }
        };
        fixture.Handle.Wait = token => fixture.Handle.Exited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token);

        var pending = fixture.Runner.RunEnvironmentAsync(environment.Capture, [], timeoutSeconds: 1);
        try
        {
            await fixture.AwaitStageOrCompletion(pending);
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
            if (fixture.StageEntered.Task.IsCompletedSuccessfully)
                await readCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
    }

    [Fact]
    public async Task Explicit_environment_response_from_a_different_capture_is_refused()
    {
        using var expected = new EnvironmentCaptureFixture();
        using var other = new EnvironmentCaptureFixture();
        using var fixture = new ProcessFixture();
        ConfigureEnvironmentSuccess(fixture, other.Capture);

        var refusal = await Assert.ThrowsAsync<CliRefusal>(() =>
            fixture.Runner.RunEnvironmentAsync(expected.Capture, []));

        Assert.Equal("candidate-response-invalid", refusal.Code);
        Assert.Equal(3, refusal.ExitCode);
        fixture.AssertClosed();
    }

    [Fact]
    public async Task Exited_handle_does_not_skip_termination_of_its_owned_scope()
    {
        using var fixture = new ProcessFixture();
        fixture.Handle.Exited = true;
        await fixture.Runner.RunAsync(Host, fixture.Request);
        Assert.Equal(1, fixture.Handle.KillCount);
        fixture.AssertClosed();
    }

    [Fact]
    public async Task Payload_exit_does_not_release_a_retained_supervisor_before_cleanup()
    {
        using var fixture = new ProcessFixture();
        fixture.Handle.OperationWait = _ => Task.CompletedTask;
        fixture.Handle.Wait = _ => fixture.Handle.Exited ? Task.CompletedTask : throw new InvalidOperationException();
        var result = await fixture.Runner.RunAsync(Host, fixture.Request);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, fixture.Handle.KillCount);
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

    [Fact]
    public async Task Cancellation_observed_after_response_validation_still_refuses_and_cleans_up()
    {
        using var fixture = new ProcessFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Handle.ObserveExitCode = () =>
        {
            cancellation.Cancel();
            return 0;
        };

        var refusal = await Assert.ThrowsAsync<CliRefusal>(() =>
            fixture.Runner.RunAsync(Host, fixture.Request, cancellationToken: cancellation.Token));

        Assert.Equal("candidate-inspection-cancelled", refusal.Code);
        fixture.AssertClosed();
    }

    [Fact]
    public void Explicit_environment_option_rejects_repeated_occurrences_at_parse_boundary()
    {
        var omitted = ParseCli(InspectArguments());
        var single = ParseCli(InspectArguments("--environment-input", "one.json"));
        var repeated = ParseCli(InspectArguments(
            "--environment-input", "one.json", "--environment-input", "two.json"));

        Assert.Empty(omitted.Errors);
        Assert.Empty(single.Errors);
        Assert.Contains(repeated.Errors, error => error.Message.Contains(
            "--environment-input", StringComparison.Ordinal));
        var errors = string.Join(Environment.NewLine, repeated.Errors.Select(error => error.Message));
        Assert.DoesNotContain("one.json", errors, StringComparison.Ordinal);
        Assert.DoesNotContain("two.json", errors, StringComparison.Ordinal);
    }

    private static System.CommandLine.ParseResult ParseCli(string[] arguments)
    {
        var cliType = typeof(RegularFileOpener).Assembly.GetType("Elsa.Cli.ElsaCli", throwOnError: true)!;
        var build = cliType.GetMethod("Build", BindingFlags.Public | BindingFlags.Static)!;
        var root = (System.CommandLine.RootCommand)build.Invoke(null, null)!;
        return root.Parse(arguments);
    }

    private static string[] InspectArguments(params string[] environmentInput) =>
    [
        "composition", "inspect", "--host", "host", "--host-dir", "source", "--shell", "default",
        "--environment", "Production", "--composition", "candidate", "--trust-host-code",
        ..environmentInput
    ];

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
        fixture.Handle.Output = new ControlledStream
        {
            Read = (_, _) => { fixture.StageEntered.TrySetResult(); return new ValueTask<int>(release.Task); }
        };
        fixture.Handle.OnDispose = () => release.TrySetResult(0);
        fixture.Handle.Wait = token => fixture.Handle.Exited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token);
        var pending = fixture.Runner.RunAsync(Host, fixture.Request, 1);
        await fixture.AwaitStageOrCompletion(pending);
        Assert.True(fixture.StageEntered.Task.IsCompletedSuccessfully);
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
        var readCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Handle.Output = new ControlledStream
        {
            Read = async (_, _) =>
            {
                fixture.StageEntered.TrySetResult();
                try { return await never.Task; }
                finally { readCompleted.TrySetResult(); }
            }
        };
        fixture.Handle.Wait = token => fixture.Handle.Exited ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token);
        var pending = fixture.Runner.RunAsync(Host, fixture.Request, 1);
        try
        {
            await fixture.AwaitStageOrCompletion(pending);
            Assert.True(fixture.StageEntered.Task.IsCompletedSuccessfully);
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
            if (fixture.StageEntered.Task.IsCompletedSuccessfully)
                await readCompleted.Task.WaitAsync(TimeSpan.FromSeconds(30));
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

    [Fact]
    public async Task Handle_assigns_scope_before_go_and_terminates_it_after_status()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(321);
        using var control = HandleControl.ReadyThenStatus(correlation, 17);
        using var scope = new HandleScope();
        using var handle = new CandidateProcessHandle(process, control, correlation, scope);

        await handle.WaitForOperationExitAsync(CancellationToken.None);
        var exitCode = handle.ExitCode;
        await handle.WaitForExitAsync(CancellationToken.None);
        handle.KillTree();

        Assert.Equal(17, exitCode);
        Assert.Equal(1, scope.AssignCount);
        Assert.Equal(1, scope.TerminateCount);
        Assert.Equal(2, scope.ActiveReadCount);
        Assert.Equal(1, control.GoCount);
    }

    [Fact]
    public async Task Handle_does_not_block_cleanup_on_a_stalled_go_write()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(322);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, blockGo: true);
        using var scope = new HandleScope();
        using var handle = new CandidateProcessHandle(process, control, correlation, scope);

        var operation = handle.WaitForOperationExitAsync(CancellationToken.None);
        await control.GoWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var cleanup = Task.Run(handle.KillTree);
        await cleanup.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, scope.TerminateCount);

        handle.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation);
        Assert.Equal(1, scope.TerminateCount);
    }

    [Fact]
    public async Task Handle_rejects_ready_after_cleanup_without_sending_go()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(323);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, blockReady: true);
        using var handle = new CandidateProcessHandle(process, control, correlation, group: new HandleGroup());

        await control.ReadyReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handle.KillTree();
        control.ReleaseReadyRead();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handle.WaitForOperationExitAsync(CancellationToken.None));

        Assert.Equal(1, process.KillCount);
        Assert.Equal(0, control.GoCount);
    }

    [Fact]
    public async Task Handle_assign_failure_is_observed_without_authorizing_worker()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(324);
        using var control = HandleControl.ReadyThenStatus(correlation, 0);
        using var scope = new HandleScope { AssignFailure = new InvalidOperationException("assign") };
        using var handle = new CandidateProcessHandle(process, control, correlation, scope);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handle.WaitForOperationExitAsync(CancellationToken.None));

        Assert.Equal(1, scope.AssignCount);
        Assert.Equal(0, control.GoCount);
    }

    [Fact]
    public async Task Handle_uses_group_termination_and_wait_after_unix_authorization()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(325);
        using var control = HandleControl.ReadyThenStatus(correlation, 3);
        using var group = new HandleGroup();
        using var handle = new CandidateProcessHandle(process, control, correlation, group: group);

        await handle.WaitForOperationExitAsync(CancellationToken.None);
        Assert.Equal(3, handle.ExitCode);
        await handle.WaitForExitAsync(CancellationToken.None);

        Assert.Equal([325], group.TerminatedProcessIds);
        Assert.Equal([325], group.WaitedProcessGroupIds);
        Assert.Equal(1, control.GoCount);
    }

    [Fact]
    public async Task Handle_refuses_group_termination_after_anchor_exit()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(326);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, blockStatus: true);
        using var group = new HandleGroup();
        using var handle = new CandidateProcessHandle(process, control, correlation, group: group);

        await handle.StandardInput.WriteAsync(ReadOnlyMemory<byte>.Empty);
        await control.StatusReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        process.Exited = true;
        Assert.Throws<InvalidOperationException>(() => handle.KillTree());
        Assert.Empty(group.TerminatedProcessIds);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void Native_group_adapter_preserves_kill_failure(int nativeResult, bool fails)
    {
        var calls = new List<(int ProcessId, int Signal)>();
        var group = new CandidateNativeProcessGroup((processId, signal) =>
        {
            calls.Add((processId, signal));
            return nativeResult;
        });

        if (fails)
            Assert.Throws<InvalidOperationException>(() => group.Terminate(327));
        else
            group.Terminate(327);

        Assert.Equal([(-327, 9)], calls);
    }

    [Fact]
    public async Task Native_group_adapter_forwards_wait_to_the_supplied_observer()
    {
        var calls = new List<(int ProcessGroupId, CancellationToken Token)>();
        using var cancellation = new CancellationTokenSource();
        var observer = new CandidateUnixProcessGroup((processGroupId, token) =>
        {
            calls.Add((processGroupId, token));
            return [];
        });
        var group = new CandidateNativeProcessGroup((_, _) => 0, observer);

        await group.WaitForExitAsync(328, cancellation.Token);

        Assert.Equal([(328, cancellation.Token)], calls);
    }

    [Fact]
    public void Handle_constructor_rejects_null_process_or_control()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(328);
        using var control = HandleControl.ReadyThenStatus(correlation, 0);

        Assert.Throws<ArgumentNullException>(() =>
            new CandidateProcessHandle(null!, control, correlation));
        Assert.Throws<ArgumentNullException>(() =>
            new CandidateProcessHandle(process, null!, correlation));
    }

    [Theory]
    [InlineData("opcode")]
    [InlineData("correlation")]
    public async Task Handle_rejects_a_malformed_ready_frame_without_authorizing(string malformedPart)
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(329);
        using var control = HandleControl.ReadyThenStatus(correlation, 0,
            readyOpcode: malformedPart == "opcode" ? (byte)0 : CandidateProcessOwner.Ready,
            readyCorrelation: malformedPart == "correlation" ? Guid.NewGuid() : correlation);
        using var handle = new CandidateProcessHandle(process, control, correlation, group: new HandleGroup());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handle.WaitForOperationExitAsync(CancellationToken.None));

        Assert.Equal(0, control.GoCount);
    }

    [Fact]
    public async Task Handle_rejects_a_malformed_status_frame_after_authorization()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(330);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, statusOpcode: 0);
        using var group = new HandleGroup();
        using var handle = new CandidateProcessHandle(process, control, correlation, group: group);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handle.WaitForOperationExitAsync(CancellationToken.None));

        Assert.Equal(1, control.GoCount);
        Assert.Empty(group.TerminatedProcessIds);
    }

    [Theory]
    [InlineData("connect", 0)]
    [InlineData("ready-read", 0)]
    [InlineData("write", 0)]
    [InlineData("flush", 1)]
    [InlineData("status-read", 1)]
    public async Task Handle_surfaces_control_channel_failures_without_misreporting_authorization(
        string failureName, int expectedGoCount)
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(331);
        using var control = HandleControl.ReadyThenStatus(correlation, 0,
            failure: Enum.Parse<ControlFailure>(failureName.Replace('-', '_'), ignoreCase: true));
        using var handle = new CandidateProcessHandle(process, control, correlation, group: new HandleGroup());

        await Assert.ThrowsAsync<IOException>(() =>
            handle.WaitForOperationExitAsync(CancellationToken.None));

        Assert.Equal(expectedGoCount, control.GoCount);
    }

    [Fact]
    public async Task Handle_does_not_kill_an_already_exited_supervisor_before_ready()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(332);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, blockReady: true);
        using var handle = new CandidateProcessHandle(process, control, correlation, group: new HandleGroup());

        await control.ReadyReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        process.Exited = true;
        handle.KillTree();
        control.ReleaseReadyRead();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            handle.WaitForOperationExitAsync(CancellationToken.None));

        Assert.Equal(0, process.KillCount);
        Assert.Equal(0, control.GoCount);
    }

    [Fact]
    public async Task Scope_termination_can_be_retried_after_a_failed_request()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(333);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, blockStatus: true);
        using var scope = new HandleScope { TerminateFailure = new InvalidOperationException("terminate") };
        using var handle = new CandidateProcessHandle(process, control, correlation, scope);

        await handle.StandardInput.WriteAsync(ReadOnlyMemory<byte>.Empty);
        await control.StatusReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => handle.KillTree());
        handle.KillTree();

        Assert.Equal(2, scope.TerminateCount);
    }

    [Fact]
    public async Task Group_termination_can_be_retried_after_a_failed_request()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(334);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, blockStatus: true);
        using var group = new HandleGroup { TerminateFailure = new InvalidOperationException("terminate") };
        using var handle = new CandidateProcessHandle(process, control, correlation, group: group);

        await handle.StandardInput.WriteAsync(ReadOnlyMemory<byte>.Empty);
        await control.StatusReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Throws<InvalidOperationException>(() => handle.KillTree());
        handle.KillTree();

        Assert.Equal([334, 334], group.TerminatedProcessIds);
    }

    [Fact]
    public async Task Scope_wait_surfaces_cancellation_during_job_completion()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(335);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, blockStatus: true);
        using var scope = new HandleScope();
        using var handle = new CandidateProcessHandle(process, control, correlation, scope);
        await handle.StandardInput.WriteAsync(ReadOnlyMemory<byte>.Empty);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handle.WaitForExitAsync(cancellation.Token));
    }

    [Fact]
    public async Task Scope_wait_surfaces_an_error_during_job_completion()
    {
        var expected = new IOException("active-processes");
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(336);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, blockStatus: true);
        using var scope = new HandleScope { ActiveFailure = expected };
        using var handle = new CandidateProcessHandle(process, control, correlation, scope);
        await handle.StandardInput.WriteAsync(ReadOnlyMemory<byte>.Empty);

        var actual = await Assert.ThrowsAsync<IOException>(() => handle.WaitForExitAsync(CancellationToken.None));

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task Group_wait_surfaces_cancellation_and_errors_during_completion()
    {
        var correlation = Guid.NewGuid();
        using var process = new HandleProcess(337);
        using var control = HandleControl.ReadyThenStatus(correlation, 0, blockStatus: true);
        using var group = new HandleGroup();
        using var handle = new CandidateProcessHandle(process, control, correlation, group: group);
        await handle.StandardInput.WriteAsync(ReadOnlyMemory<byte>.Empty);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handle.WaitForExitAsync(cancellation.Token));

        var expected = new IOException("group-completion");
        group.WaitFailure = expected;
        process.Exited = false;
        await Assert.ThrowsAsync<IOException>(() => handle.WaitForExitAsync(CancellationToken.None));
    }

    [Fact]
    public void Handle_disposal_attempts_each_owned_close_when_earlier_closes_throw()
    {
        var correlation = Guid.NewGuid();
        var process = new HandleProcess(338) { DisposeFailure = new IOException("process") };
        var control = HandleControl.ReadyThenStatus(correlation, 0);
        control.DisposeFailure = new IOException("control");
        var scope = new HandleScope { DisposeFailure = new IOException("scope") };
        var handle = new CandidateProcessHandle(process, control, correlation, scope);

        var actual = Assert.Throws<IOException>(() => handle.Dispose());

        Assert.Equal("process", actual.Message);
        Assert.Equal(1, control.DisposeCount);
        Assert.Equal(1, scope.DisposeCount);
        Assert.Equal(1, process.DisposeCount);
    }

    [Fact]
    public async Task Handle_disposal_attempts_all_closes_after_cancel_callback_failure_and_is_idempotent()
    {
        var correlation = Guid.NewGuid();
        var process = new HandleProcess(339);
        var control = HandleControl.ReadyThenStatus(correlation, 0, blockReady: true, throwOnCancellation: true);
        var scope = new HandleScope();
        var handle = new CandidateProcessHandle(process, control, correlation, scope);
        await control.ReadyReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var actual = Assert.Throws<AggregateException>(() => handle.Dispose());
        handle.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handle.WaitForOperationExitAsync(CancellationToken.None));

        Assert.Single(actual.InnerExceptions);
        Assert.Equal("cancel-callback", actual.InnerExceptions[0].Message);
        Assert.Equal(1, control.DisposeCount);
        Assert.Equal(1, scope.DisposeCount);
        Assert.Equal(1, process.DisposeCount);
    }

    private enum ControlFailure
    {
        None,
        Connect,
        Ready_Read,
        Write,
        Flush,
        Status_Read
    }

    private sealed class HandleProcess(int id) : ICandidateProcess
    {
        public MemoryStream Input { get; } = new();
        public MemoryStream Output { get; } = new();
        public MemoryStream Error { get; } = new();
        public int Id { get; } = id;
        public bool Exited { get; set; }
        public int KillCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Exception? DisposeFailure { get; init; }
        public Stream StandardInput => Input;
        public Stream StandardOutput => Output;
        public Stream StandardError => Error;
        public bool HasExited => Exited;
        public Task WaitForExitAsync(CancellationToken cancellationToken) { Exited = true; return Task.CompletedTask; }
        public void Kill() { KillCount++; Exited = true; }
        public void Dispose()
        {
            DisposeCount++;
            Input.Dispose();
            Output.Dispose();
            Error.Dispose();
            if (DisposeFailure is not null) throw DisposeFailure;
        }
    }

    private sealed class HandleControl : ICandidateProcessControl
    {
        private readonly byte[] ready;
        private readonly byte[] status;
        private readonly bool blockReady;
        private readonly bool blockStatus;
        private readonly bool blockGo;
        private readonly bool throwOnCancellation;
        private readonly ControlFailure failure;
        private readonly TaskCompletionSource readyRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource statusRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource goRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReadyReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StatusReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource GoWriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int GoCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Exception? DisposeFailure { get; set; }

        private HandleControl(byte[] ready, byte[] status, bool blockReady, bool blockStatus, bool blockGo,
            bool throwOnCancellation, ControlFailure failure)
        {
            this.ready = ready;
            this.status = status;
            this.blockReady = blockReady;
            this.blockStatus = blockStatus;
            this.blockGo = blockGo;
            this.throwOnCancellation = throwOnCancellation;
            this.failure = failure;
        }

        public static HandleControl ReadyThenStatus(Guid correlation, int exitCode, bool blockReady = false,
            bool blockStatus = false, bool blockGo = false, bool throwOnCancellation = false,
            ControlFailure failure = ControlFailure.None,
            byte readyOpcode = CandidateProcessOwner.Ready, Guid? readyCorrelation = null,
            byte statusOpcode = CandidateProcessOwner.Status)
        {
            var ready = new byte[CandidateProcessOwner.ReadyFrameLength];
            ready[0] = readyOpcode;
            (readyCorrelation ?? correlation).TryWriteBytes(ready.AsSpan(1));
            var status = new byte[CandidateProcessOwner.StatusFrameLength];
            status[0] = statusOpcode;
            BitConverter.TryWriteBytes(status.AsSpan(1), exitCode);
            return new HandleControl(ready, status, blockReady, blockStatus, blockGo, throwOnCancellation, failure);
        }

        public Task WaitForConnectionAsync(CancellationToken cancellationToken)
        {
            if (throwOnCancellation)
                cancellationToken.Register(static () => throw new InvalidOperationException("cancel-callback"));
            return failure == ControlFailure.Connect
                ? Task.FromException(new IOException("connect"))
                : Task.CompletedTask;
        }

        public async Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var frame = buffer.Length == ready.Length ? ready : status;
            if (ReferenceEquals(frame, ready) && failure == ControlFailure.Ready_Read)
                throw new IOException("ready-read");
            if (ReferenceEquals(frame, status) && failure == ControlFailure.Status_Read)
                throw new IOException("status-read");
            if (ReferenceEquals(frame, ready) && blockReady)
            {
                ReadyReadStarted.TrySetResult();
                await readyRelease.Task.WaitAsync(cancellationToken);
            }
            else if (ReferenceEquals(frame, status) && blockStatus)
            {
                StatusReadStarted.TrySetResult();
                await statusRelease.Task.WaitAsync(cancellationToken);
            }
            frame.AsMemory().CopyTo(buffer);
        }
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
        {
            Assert.Equal([CandidateProcessOwner.Go], buffer.ToArray());
            if (failure == ControlFailure.Write)
                throw new IOException("write");
            GoCount++;
            GoWriteStarted.TrySetResult();
            if (blockGo)
                await goRelease.Task.WaitAsync(cancellationToken);
        }
        public Task FlushAsync(CancellationToken cancellationToken) =>
            failure == ControlFailure.Flush
                ? Task.FromException(new IOException("flush"))
                : Task.CompletedTask;
        public void ReleaseReadyRead() => readyRelease.TrySetResult();
        public void ReleaseGoWrite() => goRelease.TrySetResult();
        public void Dispose()
        {
            DisposeCount++;
            if (DisposeFailure is not null) throw DisposeFailure;
        }
    }

    private sealed class HandleScope : ICandidateProcessScope
    {
        public Exception? AssignFailure { get; init; }
        public Exception? TerminateFailure { get; init; }
        public Exception? ActiveFailure { get; init; }
        public Exception? DisposeFailure { get; init; }
        public int AssignCount { get; private set; }
        public int TerminateCount { get; private set; }
        public int ActiveReadCount { get; private set; }
        public int DisposeCount { get; private set; }
        public int ActiveProcesses
        {
            get
            {
                if (ActiveFailure is not null) throw ActiveFailure;
                return ActiveReadCount++ == 0 ? 1 : 0;
            }
        }
        public void Assign(ICandidateProcess process)
        {
            AssignCount++;
            if (AssignFailure is not null) throw AssignFailure;
        }
        public void Terminate()
        {
            TerminateCount++;
            if (TerminateCount == 1 && TerminateFailure is not null) throw TerminateFailure;
        }
        public void Dispose()
        {
            DisposeCount++;
            if (DisposeFailure is not null) throw DisposeFailure;
        }
    }

    private sealed class HandleGroup : ICandidateProcessGroup, IDisposable
    {
        public List<int> TerminatedProcessIds { get; } = [];
        public List<int> WaitedProcessGroupIds { get; } = [];
        public Exception? TerminateFailure { get; init; }
        public Exception? WaitFailure { get; set; }
        public void Terminate(int processId)
        {
            TerminatedProcessIds.Add(processId);
            if (TerminatedProcessIds.Count == 1 && TerminateFailure is not null) throw TerminateFailure;
        }
        public Task WaitForExitAsync(int processGroupId, CancellationToken cancellationToken)
        {
            WaitedProcessGroupIds.Add(processGroupId);
            cancellationToken.ThrowIfCancellationRequested();
            if (WaitFailure is not null) return Task.FromException(WaitFailure);
            return Task.CompletedTask;
        }
        public void Dispose() { }
    }

    private sealed class EnvironmentCaptureFixture : IDisposable
    {
        private readonly CompositionBridgeFixture source = new();

        public EnvironmentCaptureFixture()
        {
            source.WriteAcceptedComposition();
            Directory.CreateDirectory(source.CandidateDirectory);
            EnvironmentPath = Path.Join(source.CandidateDirectory, "environment.json");
            RawEnvironment = CandidateInspectionFixture.EnvironmentDocument(("Private", "private-environment-value"));
            File.WriteAllBytes(EnvironmentPath, RawEnvironment);
            Capture = CompositionInspectionCapture.OpenWithEnvironmentInput(Host, source.HostDirectory,
                "default", "Production", source.OutputPath, EnvironmentPath, source.CatalogPath, source.ReviewPath);
        }

        public string EnvironmentPath { get; }
        public byte[] RawEnvironment { get; }
        public CompositionInspectionCapture Capture { get; }

        public void Dispose()
        {
            Capture.Dispose();
            source.Dispose();
        }
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

        public void SetOutput(System.Text.Json.Nodes.JsonObject hostResponse)
        {
            SetOutputBytes(JsonSerializer.SerializeToUtf8Bytes(new WorkerResponse
            {
                Tooling = JsonSerializer.SerializeToElement(hostResponse)
            }, WorkerContract.Json));
        }

        public void SetOutputBytes(byte[] bytes)
        {
            Handle.Output.Dispose();
            Handle.Output = new MemoryStream(bytes);
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
        public Func<CancellationToken, Task>? OperationWait { get; set; }
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
        public Task WaitForOperationExitAsync(CancellationToken token) => OperationWait?.Invoke(token) ?? WaitForExitAsync(token);
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

    private static byte[] EnvironmentSuccessResponse(WorkerCandidatePayload candidate)
    {
        var hostResponse = CandidateHostResponseFixtures.Success(candidate);
        hostResponse["configurationResolution"]!["source"] = "captured-workbench-json-explicit-environment-v1";
        hostResponse["configurationResolution"]!["externalInputs"] = "supplied-intended";
        return JsonSerializer.SerializeToUtf8Bytes(new WorkerResponse
        {
            Tooling = JsonSerializer.SerializeToElement(hostResponse)
        }, WorkerContract.Json);
    }

    private static void ConfigureEnvironmentSuccess(ProcessFixture fixture, CompositionInspectionCapture capture) =>
        fixture.SetOutputBytes(EnvironmentSuccessResponse(capture.Payload));

    private static byte[] PadResponse(byte[] response, int targetBytes)
    {
        Assert.True(response.Length <= targetBytes);
        var padded = new byte[targetBytes];
        response.CopyTo(padded, 0);
        padded.AsSpan(response.Length).Fill((byte)' ');
        return padded;
    }
}
