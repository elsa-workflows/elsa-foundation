using System.Buffers.Binary;
using System.Diagnostics;
using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

public sealed class CandidateProcessOwnerTests
{
    [Fact]
    public async Task Supervisor_admits_only_the_private_owner_argument_shape()
    {
        var fixture = new SupervisorFixture();
        var invalid = new[]
        {
            Array.Empty<string>(),
            new[] { "wrong", "--candidate-owner" },
            new[] { "--candidate-inspection", "wrong", "pipe", Guid.NewGuid().ToString("N"), "runtime", "deps", "payload" },
            new[] { "--candidate-inspection", "--candidate-owner", "", Guid.NewGuid().ToString("N"), "runtime", "deps", "payload" },
            new[] { "--candidate-inspection", "--candidate-owner", "pipe", "invalid", "runtime", "deps", "payload" },
            new[] { "--candidate-inspection", "--candidate-owner", "pipe", Guid.NewGuid().ToString("N"), "", "deps", "payload" },
            new[] { "--candidate-inspection", "--candidate-owner", "pipe", Guid.NewGuid().ToString("N"), "runtime", "", "payload" },
            new[] { "--candidate-inspection", "--candidate-owner", "pipe", Guid.NewGuid().ToString("N"), "runtime", "deps", "" },
            new[] { "--candidate-inspection", "--candidate-owner", null!, Guid.NewGuid().ToString("N"), "runtime", "deps", "payload" }
        };

        foreach (var args in invalid)
            Assert.Equal(ToolExitCode.ResolutionFailure, await fixture.CreateSupervisor().RunAsync(args, CancellationToken.None));

        Assert.Equal(ToolExitCode.ResolutionFailure,
            await fixture.CreateSupervisor().RunAsync((string[]?)null!, CancellationToken.None));

        Assert.Equal(0, fixture.ControlFactory.CreateCount);
    }

    [Fact]
    public void Supervisor_requires_all_runtime_seams_and_a_positive_timeout()
    {
        var fixture = new SupervisorFixture();

        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor(null!, fixture.ControlFactory,
            fixture.PayloadLauncher, fixture.StdioFactory, fixture.Native));
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor(fixture.Platform, null!,
            fixture.PayloadLauncher, fixture.StdioFactory, fixture.Native));
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor(fixture.Platform,
            fixture.ControlFactory, null!, fixture.StdioFactory, fixture.Native));
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor(fixture.Platform,
            fixture.ControlFactory, fixture.PayloadLauncher, null!, fixture.Native));
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor(fixture.Platform,
            fixture.ControlFactory, fixture.PayloadLauncher, fixture.StdioFactory, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CandidateProcessSupervisor(fixture.Platform,
            fixture.ControlFactory, fixture.PayloadLauncher, fixture.StdioFactory, fixture.Native, TimeSpan.Zero));
    }

    [Fact]
    public async Task Supervisor_rejects_an_unsupported_platform_without_opening_control()
    {
        var fixture = new SupervisorFixture { Platform = new SupervisorPlatform(false, false) };

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(0, fixture.ControlFactory.CreateCount);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
    }

    [Theory]
    [InlineData("")]
    public async Task Supervisor_rejects_missing_arguments_without_opening_control(string missing)
    {
        var fixture = new SupervisorFixture();
        var result = await fixture.CreateSupervisor().RunAsync(missing, Guid.NewGuid(), "runtime", "deps", "payload",
            CancellationToken.None);

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(0, fixture.ControlFactory.CreateCount);
    }

    [Fact]
    public async Task Supervisor_rejects_a_failed_control_connection_before_native_ownership()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.ConnectFailure = new InvalidOperationException("connect");

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Control.DisposeCount);
        Assert.Equal(0, fixture.Native.EstablishCount);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
    }

    [Fact]
    public async Task Supervisor_rejects_native_owner_establishment_failure_without_ready_payload_or_abort()
    {
        var fixture = new SupervisorFixture();
        fixture.Native.EstablishFailure = new InvalidOperationException("establish");

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Empty(fixture.Control.Writes);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
        Assert.Equal(1, fixture.Native.EstablishCount);
        Assert.Equal(0, fixture.Native.AbortCount);
    }

    [Fact]
    public async Task Supervisor_honors_cancellation_while_connecting()
    {
        var fixture = new SupervisorFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Control.ConnectPending = true;
        fixture.Control.OnConnect = cancellation.Cancel;

        var result = await fixture.CreateSupervisor().RunAsync("pipe", Guid.NewGuid(), "runtime", "deps", "payload",
            cancellation.Token);

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(0, fixture.Native.EstablishCount);
    }

    [Fact]
    public async Task Supervisor_rejects_a_failed_ready_write_and_aborts_an_established_owner()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.ReadyWriteFailures = 1;

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.EstablishCount);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
    }

    [Fact]
    public async Task Supervisor_rejects_a_failed_ready_flush_and_aborts_an_established_owner()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.ReadyFlushFailures = 1;

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.EstablishCount);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public async Task Supervisor_rejects_eof_or_malformed_go_without_starting_payload(byte go)
    {
        var fixture = new SupervisorFixture();
        fixture.Control.InitialRead = go;

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.EstablishCount);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
        Assert.Single(fixture.Control.Writes);
        Assert.Equal(CandidateProcessOwner.Ready, fixture.Control.Writes[0][0]);
    }

    [Fact]
    public async Task Supervisor_rejects_a_failed_go_read_without_starting_payload()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.InitialReadFailure = new IOException("read");

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
    }

    [Fact]
    public async Task Supervisor_honors_cancellation_while_reading_go()
    {
        var fixture = new SupervisorFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Control.InitialReadPending = true;
        fixture.Control.OnInitialRead = cancellation.Cancel;

        var result = await fixture.CreateSupervisor().RunAsync("pipe", Guid.NewGuid(), "runtime", "deps", "payload",
            cancellation.Token);

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
    }

    [Fact]
    public async Task Supervisor_watches_owner_before_starting_payload_and_aborts_on_immediate_loss()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.OwnerAlreadyClosed = true;

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
        Assert.Equal(1, fixture.Native.AbortCount);
    }

    [Fact]
    public async Task Supervisor_aborts_on_a_malformed_post_go_owner_byte()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.OwnerAlreadyClosed = true;
        fixture.Control.OwnerCloseByte = 9;

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(0, fixture.PayloadLauncher.StartCount);
    }

    [Fact]
    public async Task Supervisor_does_not_use_native_ownership_on_a_supported_non_unix_platform()
    {
        var fixture = new SupervisorFixture { Platform = new SupervisorPlatform(true, false) };

        var result = await fixture.RunAsync();

        Assert.Equal(17, result);
        Assert.Equal(0, fixture.Native.EstablishCount);
        Assert.Equal(0, fixture.Native.AbortCount);
    }

    [Fact]
    public async Task Supervisor_does_not_use_native_ownership_when_non_unix_owner_is_lost()
    {
        var fixture = new SupervisorFixture { Platform = new SupervisorPlatform(true, false) };
        fixture.Control.OwnerAlreadyClosed = true;

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(0, fixture.Native.EstablishCount);
        Assert.Equal(0, fixture.Native.AbortCount);
    }

    [Fact]
    public async Task Supervisor_writes_failure_status_when_payload_start_fails_then_aborts_after_owner_close()
    {
        var fixture = new SupervisorFixture();
        fixture.PayloadLauncher.StartFailure = new InvalidOperationException("start");

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.PayloadLauncher.StartCount);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(2, fixture.Control.Writes.Count);
        Assert.Equal(ToolExitCode.ResolutionFailure, StatusCode(fixture.Control.Writes[1]));
    }

    [Fact]
    public async Task Supervisor_writes_failure_status_when_standard_stream_open_fails()
    {
        var fixture = new SupervisorFixture();
        fixture.StdioFactory.OpenFailure = new InvalidOperationException("stdio");

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Payload.DisposeCount);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(ToolExitCode.ResolutionFailure, StatusCode(fixture.Control.Writes[1]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    public async Task Supervisor_forwards_payload_and_status_after_all_streams_flush(int exitCode)
    {
        var fixture = new SupervisorFixture();
        fixture.Payload.ExitCode = exitCode;
        fixture.Payload.StandardOutput = new TrackingStream("payload-output", "out");
        fixture.Payload.StandardError = new TrackingStream("payload-error", "err");
        fixture.Stdio.StandardInput = new TrackingStream("input", "request");
        fixture.Stdio.StandardOutput = new TrackingStream("output");
        fixture.Stdio.StandardError = new TrackingStream("error");
        fixture.Control.BeforeStatusFlush = () => fixture.Payload.WaitCalled &&
            ((TrackingStream)fixture.Stdio.StandardOutput).FlushCount == 1 &&
            ((TrackingStream)fixture.Stdio.StandardError).FlushCount == 1;

        var result = await fixture.RunAsync();

        Assert.Equal(exitCode, result);
        Assert.Equal(1, fixture.PayloadLauncher.StartCount);
        Assert.Equal(1, fixture.Native.EstablishCount);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(2, fixture.Control.Writes.Count);
        Assert.Equal(CandidateProcessOwner.Ready, fixture.Control.Writes[0][0]);
        Assert.Equal(exitCode, StatusCode(fixture.Control.Writes[1]));
        Assert.True(fixture.Control.BeforeStatusFlushResult);
        Assert.Equal("out", Text(fixture.Stdio.StandardOutput));
        Assert.Equal("err", Text(fixture.Stdio.StandardError));
        Assert.True(((TrackingStream)fixture.Stdio.StandardInput).DisposeCount > 0);
        Assert.True(((TrackingStream)fixture.Payload.StandardInput).DisposeCount > 0);
    }

    [Fact]
    public async Task Supervisor_aborts_without_status_when_owner_is_lost_during_payload_wait()
    {
        var fixture = new SupervisorFixture();
        fixture.Payload.OnWait = fixture.Control.CompleteOwner;
        fixture.Payload.WaitTask = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        var run = fixture.RunAsync();

        var result = await run;

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Single(fixture.Control.Writes);
    }

    [Theory]
    [InlineData("output")]
    [InlineData("error")]
    [InlineData("input")]
    public async Task Supervisor_writes_failure_status_when_forwarding_fails(string stream)
    {
        var fixture = new SupervisorFixture();
        var failing = new ThrowingReadStream();
        if (stream == "output")
            fixture.Payload.StandardOutput = failing;
        else if (stream == "error")
            fixture.Payload.StandardError = failing;
        else
            fixture.Stdio.StandardInput = failing;

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(ToolExitCode.ResolutionFailure, StatusCode(fixture.Control.Writes[1]));
    }

    [Fact]
    public async Task Supervisor_closes_input_source_when_payload_input_close_fails()
    {
        var fixture = new SupervisorFixture();
        var source = new TrackingStream("input", "request");
        fixture.Stdio.StandardInput = source;
        fixture.Payload.StandardInput = new TrackingStream("payload-input")
        {
            DisposeFailure = new InvalidOperationException("payload input close")
        };

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(ToolExitCode.ResolutionFailure, StatusCode(fixture.Control.Writes[1]));
    }

    [Theory]
    [InlineData("output")]
    [InlineData("error")]
    public async Task Supervisor_writes_failure_status_and_closes_source_when_forward_destination_flush_fails(string stream)
    {
        var fixture = new SupervisorFixture();
        var source = new TrackingStream($"payload-{stream}", stream);
        var destination = new TrackingStream(stream) { FlushFailure = new IOException("destination flush") };
        if (stream == "output")
        {
            fixture.Payload.StandardOutput = source;
            fixture.Stdio.StandardOutput = destination;
        }
        else
        {
            fixture.Payload.StandardError = source;
            fixture.Stdio.StandardError = destination;
        }

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(ToolExitCode.ResolutionFailure, StatusCode(fixture.Control.Writes[1]));
    }

    [Fact]
    public async Task Supervisor_writes_failure_status_when_payload_wait_fails()
    {
        var fixture = new SupervisorFixture();
        fixture.Payload.WaitFailure = new InvalidOperationException("wait");

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(ToolExitCode.ResolutionFailure, StatusCode(fixture.Control.Writes[1]));
    }

    [Fact]
    public async Task Supervisor_retries_a_failed_failure_status_and_retains_owner_until_retry_flushes()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.StatusWriteFailures = 1;

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(2, fixture.Control.Writes.Count);
        Assert.Equal(ToolExitCode.ResolutionFailure, StatusCode(fixture.Control.Writes[1]));
    }

    [Fact]
    public async Task Supervisor_holds_owner_until_frontend_closes_after_status_flush()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.CloseOwnerOnStatus = false;
        var run = fixture.RunAsync();

        await fixture.Control.WaitForStatusAsync();
        Assert.False(run.IsCompleted);
        fixture.Control.CompleteOwner();

        Assert.Equal(17, await run);
        Assert.Equal(1, fixture.Native.AbortCount);
    }

    [Fact]
    public async Task Supervisor_converts_cancellation_into_failure_status_and_owner_abort()
    {
        var fixture = new SupervisorFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Payload.OnWait = cancellation.Cancel;
        fixture.Payload.WaitTask = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        var result = await fixture.CreateSupervisor().RunAsync("pipe", Guid.NewGuid(), "runtime", "deps", "payload",
            cancellation.Token);

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(ToolExitCode.ResolutionFailure, StatusCode(fixture.Control.Writes[1]));
    }

    [Fact]
    public async Task Supervisor_aborts_when_status_write_and_retry_both_fail()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.StatusWriteFailures = 2;
        fixture.Payload.WaitFailure = new InvalidOperationException("wait");

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Single(fixture.Control.Writes);
    }

    [Fact]
    public async Task Supervisor_aborts_when_status_flush_and_retry_both_fail()
    {
        var fixture = new SupervisorFixture();
        fixture.Control.StatusFlushFailures = 2;

        var result = await fixture.RunAsync();

        Assert.Equal(ToolExitCode.ResolutionFailure, result);
        Assert.Equal(1, fixture.Native.AbortCount);
        Assert.Equal(3, fixture.Control.Writes.Count);
    }

    [Fact]
    public async Task Supervisor_continues_all_owned_disposals_after_the_first_disposal_failure()
    {
        var fixture = new SupervisorFixture();
        fixture.Stdio.DisposeFailure = new InvalidOperationException("stdio dispose");
        fixture.Payload.DisposeFailure = new InvalidOperationException("payload dispose");
        fixture.Control.DisposeFailure = new InvalidOperationException("control dispose");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.RunAsync());

        Assert.Equal("stdio dispose", error.Message);
        Assert.Equal(1, fixture.Stdio.DisposeCount);
        Assert.Equal(1, fixture.Payload.DisposeCount);
        Assert.Equal(1, fixture.Control.DisposeCount);
    }

    [Fact]
    public void Native_owner_rejects_setsid_failure_and_process_group_mismatch()
    {
        var setSessionFailure = new CandidateProcessSupervisor.CandidateSupervisorNative(() => -1, () => 7,
            () => 7, (_, _) => 0);
        Assert.Throws<InvalidOperationException>(setSessionFailure.EstablishOwner);

        var groupMismatch = new CandidateProcessSupervisor.CandidateSupervisorNative(() => 0, () => 7,
            () => 8, (_, _) => 0);
        Assert.Throws<InvalidOperationException>(groupMismatch.EstablishOwner);
    }

    [Fact]
    public void Native_owner_kills_only_the_owned_process_group()
    {
        var calls = new List<(int ProcessId, int Signal)>();
        var native = new CandidateProcessSupervisor.CandidateSupervisorNative(() => 0, () => 42, () => 42,
            (processId, signal) => { calls.Add((processId, signal)); return 0; });

        native.EstablishOwner();
        native.AbortOwner();

        Assert.Equal([(-42, 9)], calls);
    }

    [Fact]
    public void Native_owner_does_not_kill_a_process_with_a_non_dangerous_pid_and_ignores_kill_result()
    {
        var calls = new List<(int ProcessId, int Signal)>();
        var native = new CandidateProcessSupervisor.CandidateSupervisorNative(() => 0, () => 1, () => 1,
            (processId, signal) => { calls.Add((processId, signal)); return -1; });

        native.EstablishOwner();
        native.AbortOwner();

        Assert.Empty(calls);

        var killFailure = new CandidateProcessSupervisor.CandidateSupervisorNative(() => 0, () => 42, () => 42,
            (processId, signal) => { calls.Add((processId, signal)); return -1; });
        killFailure.EstablishOwner();
        killFailure.AbortOwner();
        Assert.Equal([(-42, 9)], calls);
    }

    [Fact]
    public void Public_supervisor_helpers_reject_null_constructor_dependencies()
    {
        var launcher = new CandidateProcessSupervisor.CandidateSupervisorPayloadLauncher(() => "/host", _ => null);
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor.CandidateSupervisorPayloadLauncher(null!, _ => null));
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor.CandidateSupervisorPayloadLauncher(() => "/host", null!));

        var native = new CandidateProcessSupervisor.CandidateSupervisorNative(() => 0, () => 1, () => 1, (_, _) => 0);
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor.CandidateSupervisorNative(null!, () => 1, () => 1, (_, _) => 0));
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor.CandidateSupervisorNative(() => 0, null!, () => 1, (_, _) => 0));
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor.CandidateSupervisorNative(() => 0, () => 1, null!, (_, _) => 0));
        Assert.Throws<ArgumentNullException>(() => new CandidateProcessSupervisor.CandidateSupervisorNative(() => 0, () => 1, () => 1, null!));

        Assert.Throws<ArgumentNullException>(() => new CandidateSupervisorStandardStreamsFactory(null!, () => new MemoryStream(),
            () => new MemoryStream()));
        Assert.Throws<ArgumentNullException>(() => new CandidateSupervisorStandardStreamsFactory(() => new MemoryStream(), null!,
            () => new MemoryStream()));
        Assert.Throws<ArgumentNullException>(() => new CandidateSupervisorStandardStreamsFactory(() => new MemoryStream(),
            () => new MemoryStream(), null!));

        Assert.Throws<ArgumentNullException>(() => new CandidateSupervisorStandardStreams(null!, new MemoryStream(),
            new MemoryStream()));
        Assert.Throws<ArgumentNullException>(() => new CandidateSupervisorStandardStreams(new MemoryStream(), null!,
            new MemoryStream()));
        Assert.Throws<ArgumentNullException>(() => new CandidateSupervisorStandardStreams(new MemoryStream(), new MemoryStream(),
            null!));

        _ = launcher;
        _ = native;
    }

    [Fact]
    public void Payload_launcher_rejects_missing_process_path_and_null_process_start()
    {
        var missingPath = new CandidateProcessSupervisor.CandidateSupervisorPayloadLauncher(() => null,
            _ => throw new InvalidOperationException("start should not be called"));
        Assert.Throws<InvalidOperationException>(() => missingPath.Start("runtime", "deps", "payload"));

        var nullStart = new CandidateProcessSupervisor.CandidateSupervisorPayloadLauncher(() => "/host",
            _ => null);
        Assert.Throws<InvalidOperationException>(() => nullStart.Start("runtime", "deps", "payload"));
    }

    [Fact]
    public void Payload_launcher_builds_the_expected_host_arguments_and_redirections()
    {
        ProcessStartInfo? captured = null;
        var launcher = new CandidateProcessSupervisor.CandidateSupervisorPayloadLauncher(() => "/host",
            startInfo => { captured = startInfo; return null; });

        Assert.Throws<InvalidOperationException>(() => launcher.Start("runtime.json", "deps.json", "payload.dll"));

        Assert.NotNull(captured);
        var startInfo = captured!;
        Assert.Equal("/host", startInfo.FileName);
        Assert.Equal(["exec", "--runtimeconfig", "runtime.json", "--depsfile", "deps.json", "payload.dll",
            "--candidate-inspection"], startInfo.ArgumentList);
        Assert.True(startInfo.RedirectStandardInput);
        Assert.True(startInfo.RedirectStandardOutput);
        Assert.True(startInfo.RedirectStandardError);
        Assert.False(startInfo.UseShellExecute);
        Assert.True(startInfo.CreateNoWindow);
    }

    [Fact]
    public void Standard_stream_factory_closes_acquired_streams_when_later_acquisition_fails()
    {
        var input = new TrackingStream("input");
        var output = new TrackingStream("output");
        var factory = new CandidateSupervisorStandardStreamsFactory(() => input, () => output,
            () => throw new InvalidOperationException("error open"));

        var failure = Assert.Throws<InvalidOperationException>(factory.Open);

        Assert.Equal("error open", failure.Message);
        Assert.Equal(1, input.DisposeCount);
        Assert.Equal(1, output.DisposeCount);
    }

    [Fact]
    public void Standard_stream_disposal_attempts_all_streams_after_the_first_close_fails()
    {
        var input = new TrackingStream("input") { DisposeFailure = new InvalidOperationException("input close") };
        var output = new TrackingStream("output");
        var error = new TrackingStream("error") { DisposeFailure = new InvalidOperationException("error close") };
        var streams = new CandidateSupervisorStandardStreams(input, output, error);

        var failure = Assert.Throws<InvalidOperationException>(streams.Dispose);

        Assert.Equal("input close", failure.Message);
        Assert.Equal(1, input.DisposeCount);
        Assert.Equal(1, output.DisposeCount);
        Assert.Equal(1, error.DisposeCount);
    }

    private static int StatusCode(byte[] frame)
    {
        Assert.Equal(CandidateProcessOwner.Status, frame[0]);
        return BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(1));
    }

    private static string Text(Stream stream) => System.Text.Encoding.UTF8.GetString(((TrackingStream)stream).ToArray());

    private sealed class SupervisorFixture
    {
        public SupervisorPlatform Platform { get; set; } = new(true, true);
        public SupervisorControl Control { get; } = new();
        public SupervisorControlFactory ControlFactory { get; }
        public SupervisorPayloadLauncher PayloadLauncher { get; } = new();
        public SupervisorPayload Payload => PayloadLauncher.Payload;
        public SupervisorStdioFactory StdioFactory { get; } = new();
        public SupervisorStandardStreams Stdio => StdioFactory.Streams;
        public SupervisorNative Native { get; } = new();

        public SupervisorFixture() => ControlFactory = new(Control);

        public CandidateProcessSupervisor CreateSupervisor() => new(Platform, ControlFactory, PayloadLauncher,
            StdioFactory, Native, TimeSpan.FromMilliseconds(100));

        public Task<int> RunAsync() => CreateSupervisor().RunAsync("pipe", Guid.NewGuid(), "runtime", "deps", "payload",
            CancellationToken.None);
    }

    private sealed class SupervisorPlatform(bool supported, bool unix) : ICandidateSupervisorPlatform
    {
        public bool IsSupported { get; } = supported;
        public bool IsUnix { get; } = unix;
    }

    private sealed class SupervisorControlFactory(SupervisorControl control) : ICandidateSupervisorControlFactory
    {
        public int CreateCount { get; private set; }
        public ICandidateSupervisorControl Create(string pipeName)
        {
            CreateCount++;
            return control;
        }
    }

    private sealed class SupervisorControl : ICandidateSupervisorControl
    {
        private readonly SupervisorControlStream stream = new();

        public Stream Stream => stream;
        public List<byte[]> Writes => stream.Writes;
        public byte InitialRead { get => stream.InitialRead; set => stream.InitialRead = value; }
        public Exception? InitialReadFailure { get => stream.InitialReadFailure; set => stream.InitialReadFailure = value; }
        public bool InitialReadPending { get => stream.InitialReadPending; set => stream.InitialReadPending = value; }
        public Action? OnInitialRead { get => stream.OnInitialRead; set => stream.OnInitialRead = value; }
        public bool OwnerAlreadyClosed { get => stream.OwnerAlreadyClosed; set => stream.OwnerAlreadyClosed = value; }
        public byte OwnerCloseByte { get => stream.OwnerCloseByte; set => stream.OwnerCloseByte = value; }
        public int ReadyWriteFailures { get => stream.ReadyWriteFailures; set => stream.ReadyWriteFailures = value; }
        public int ReadyFlushFailures { get => stream.ReadyFlushFailures; set => stream.ReadyFlushFailures = value; }
        public int StatusWriteFailures { get => stream.StatusWriteFailures; set => stream.StatusWriteFailures = value; }
        public int StatusFlushFailures { get => stream.StatusFlushFailures; set => stream.StatusFlushFailures = value; }
        public bool CloseOwnerOnStatus { get => stream.CloseOwnerOnStatus; set => stream.CloseOwnerOnStatus = value; }
        public Func<bool>? BeforeStatusFlush { get => stream.BeforeStatusFlush; set => stream.BeforeStatusFlush = value; }
        public bool BeforeStatusFlushResult => stream.BeforeStatusFlushResult;
        public Exception? ConnectFailure { get; set; }
        public bool ConnectPending { get => stream.ConnectPending; set => stream.ConnectPending = value; }
        public Action? OnConnect { get => stream.OnConnect; set => stream.OnConnect = value; }
        public Exception? DisposeFailure { get; set; }
        public int DisposeCount { get; private set; }

        public Task ConnectAsync(CancellationToken cancellationToken)
        {
            OnConnect?.Invoke();
            if (ConnectFailure is not null)
                return Task.FromException(ConnectFailure);
            return ConnectPending ? stream.WaitForConnectAsync(cancellationToken) : Task.CompletedTask;
        }

        public void CompleteOwner() => stream.CompleteOwner();
        public Task WaitForStatusAsync() => stream.StatusWritten;

        public void Dispose()
        {
            DisposeCount++;
            if (DisposeFailure is not null)
                throw DisposeFailure;
        }
    }

    private sealed class SupervisorControlStream : Stream
    {
        private readonly TaskCompletionSource<object?> connectCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> initialReadCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<object?> statusWrittenCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<int> ownerCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int readCount;
        private byte[]? lastWrite;

        public byte InitialRead { get; set; } = CandidateProcessOwner.Go;
        public Exception? InitialReadFailure { get; set; }
        public bool InitialReadPending { get; set; }
        public Action? OnInitialRead { get; set; }
        public bool OwnerAlreadyClosed { get; set; }
        public byte OwnerCloseByte { get; set; }
        public bool ConnectPending { get; set; }
        public Action? OnConnect { get; set; }
        public int ReadyWriteFailures { get; set; }
        public int ReadyFlushFailures { get; set; }
        public int StatusWriteFailures { get; set; }
        public int StatusFlushFailures { get; set; }
        public bool CloseOwnerOnStatus { get; set; } = true;
        public Func<bool>? BeforeStatusFlush { get; set; }
        public bool BeforeStatusFlushResult { get; private set; }
        public List<byte[]> Writes { get; } = [];
        public Task StatusWritten => statusWrittenCompletion.Task;

        public void CompleteOwner() => ownerCompletion.TrySetResult(0);

        public Task WaitForConnectAsync(CancellationToken cancellationToken) => connectCompletion.Task.WaitAsync(cancellationToken);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref readCount) == 1)
            {
                OnInitialRead?.Invoke();
                if (InitialReadFailure is not null)
                    return ValueTask.FromException<int>(InitialReadFailure);
                if (InitialReadPending)
                    return WaitForInitialReadAsync(buffer, cancellationToken);
                if (buffer.Length == 0)
                    return ValueTask.FromResult(0);
                buffer.Span[0] = InitialRead;
                return ValueTask.FromResult(1);
            }

            if (OwnerAlreadyClosed)
            {
                if (buffer.Length > 0)
                    buffer.Span[0] = OwnerCloseByte;
                return ValueTask.FromResult(OwnerCloseByte == 0 ? 0 : 1);
            }
            return WaitForOwnerAsync(buffer, cancellationToken);
        }

        private async ValueTask<int> WaitForInitialReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            await initialReadCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (buffer.Length > 0)
                buffer.Span[0] = InitialRead;
            return buffer.Length == 0 ? 0 : 1;
        }

        private async ValueTask<int> WaitForOwnerAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var result = await ownerCompletion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            if (result == 0 && buffer.Length > 0)
                buffer.Span[0] = 0;
            return result;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (buffer.Length == 0)
                return ValueTask.CompletedTask;
            var frame = buffer.ToArray();
            lastWrite = frame;
            if (frame[0] == CandidateProcessOwner.Ready && ReadyWriteFailures > 0)
            {
                ReadyWriteFailures--;
                return ValueTask.FromException(new IOException("ready write"));
            }
            if (frame[0] == CandidateProcessOwner.Status && StatusWriteFailures > 0)
            {
                StatusWriteFailures--;
                return ValueTask.FromException(new IOException("status write"));
            }
            Writes.Add(frame);
            if (frame[0] == CandidateProcessOwner.Status)
                statusWrittenCompletion.TrySetResult(null);
            return ValueTask.CompletedTask;
        }

        public override Task FlushAsync(CancellationToken cancellationToken = default)
        {
            if (lastWrite is not null && lastWrite[0] == CandidateProcessOwner.Ready && ReadyFlushFailures > 0)
            {
                ReadyFlushFailures--;
                return Task.FromException(new IOException("ready flush"));
            }
            if (lastWrite is not null && lastWrite[0] == CandidateProcessOwner.Status && StatusFlushFailures > 0)
            {
                StatusFlushFailures--;
                return Task.FromException(new IOException("status flush"));
            }
            if (lastWrite is not null && lastWrite[0] == CandidateProcessOwner.Status)
                BeforeStatusFlushResult = BeforeStatusFlush?.Invoke() ?? true;
            if (lastWrite is not null && lastWrite[0] == CandidateProcessOwner.Status && CloseOwnerOnStatus)
                CompleteOwner();
            return Task.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                CompleteOwner();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => 0;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class SupervisorPayloadLauncher : ICandidateSupervisorPayloadLauncher
    {
        public int StartCount { get; private set; }
        public Exception? StartFailure { get; set; }
        public SupervisorPayload Payload { get; } = new();

        public ICandidateSupervisorPayload Start(string runtimeConfig, string depsFile, string payloadAssembly)
        {
            StartCount++;
            if (StartFailure is not null)
                throw StartFailure;
            return Payload;
        }
    }

    private sealed class SupervisorPayload : ICandidateSupervisorPayload
    {
        public Stream StandardInput { get; set; } = new TrackingStream("payload-input");
        public Stream StandardOutput { get; set; } = new TrackingStream("payload-output", "out");
        public Stream StandardError { get; set; } = new TrackingStream("payload-error", "err");
        public int ExitCode { get; set; } = 17;
        public Exception? WaitFailure { get; set; }
        public Action? OnWait { get; set; }
        public bool WaitCalled { get; private set; }
        public Task WaitTask { get; set; } = Task.CompletedTask;
        public int DisposeCount { get; private set; }
        public Exception? DisposeFailure { get; set; }

        public Task WaitForExitAsync(CancellationToken cancellationToken)
        {
            WaitCalled = true;
            OnWait?.Invoke();
            if (WaitFailure is not null)
                return Task.FromException(WaitFailure);
            return WaitTask == Task.CompletedTask ? Task.CompletedTask : WaitTask.WaitAsync(cancellationToken);
        }

        public void Dispose()
        {
            DisposeCount++;
            if (DisposeFailure is not null)
                throw DisposeFailure;
        }
    }

    private sealed class SupervisorStdioFactory : ICandidateSupervisorStandardStreamsFactory
    {
        public SupervisorStandardStreams Streams { get; } = new();
        public Exception? OpenFailure { get; set; }

        public ICandidateSupervisorStandardStreams Open() => OpenFailure is null
            ? Streams
            : throw OpenFailure;
    }

    private sealed class SupervisorStandardStreams : ICandidateSupervisorStandardStreams
    {
        public Stream StandardInput { get; set; } = new TrackingStream("input", "request");
        public Stream StandardOutput { get; set; } = new TrackingStream("output");
        public Stream StandardError { get; set; } = new TrackingStream("error");
        public int DisposeCount { get; private set; }
        public Exception? DisposeFailure { get; set; }

        public void Dispose()
        {
            DisposeCount++;
            if (DisposeFailure is not null)
                throw DisposeFailure;
        }
    }

    private sealed class SupervisorNative : ICandidateSupervisorNative
    {
        public int EstablishCount { get; private set; }
        public int AbortCount { get; private set; }
        public Exception? EstablishFailure { get; set; }
        public void EstablishOwner()
        {
            EstablishCount++;
            if (EstablishFailure is not null)
                throw EstablishFailure;
        }
        public void AbortOwner() => AbortCount++;
    }

    private class TrackingStream : MemoryStream
    {
        public TrackingStream(string name, string? content = null)
        {
            Name = name;
            if (content is not null)
            {
                Write(System.Text.Encoding.UTF8.GetBytes(content));
                Position = 0;
            }
        }

        public string Name { get; }
        public int FlushCount { get; private set; }
        public int DisposeCount { get; private set; }
        public Exception? DisposeFailure { get; set; }
        public Exception? FlushFailure { get; set; }

        public override void Flush() => FlushCount++;
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            FlushCount++;
            return FlushFailure is null ? Task.CompletedTask : Task.FromException(FlushFailure);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeCount++;
                if (DisposeFailure is not null)
                    throw DisposeFailure;
            }
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingReadStream : TrackingStream
    {
        public ThrowingReadStream() : base("failure") { }
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("copy"));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<int>(new IOException("copy"));
    }
}
