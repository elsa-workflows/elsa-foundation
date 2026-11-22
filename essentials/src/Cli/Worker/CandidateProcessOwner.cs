using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

namespace Elsa.Cli.Worker;

/// <summary>
/// The private control protocol and supervisor used to own a candidate worker's process lifetime.
/// </summary>
/// <remarks>
/// The supervisor is deliberately separate from the candidate payload. It creates the Unix session/process
/// group (or waits for the frontend to assign its Windows job), then starts the payload only after the frontend
/// sends <see cref="Go"/>. It stays alive after the payload exits so an ordinary descendant cannot make the
/// Unix group identifier reusable before the frontend's cleanup has run.
/// </remarks>
public static class CandidateProcessOwner
{
    public const byte Ready = 1;
    public const byte Go = 2;
    public const byte Status = 3;

    public const int ReadyFrameLength = 1 + 16;
    public const int StatusFrameLength = 1 + sizeof(int);
    public const int ControlTimeoutSeconds = 30;

    /// <summary>Runs the worker's private supervisor mode. The frontend owns the corresponding cleanup.</summary>
    internal static Task<int> RunSupervisorAsync(string[] args, CancellationToken cancellationToken) =>
        CandidateProcessSupervisor.CreateDefault().RunAsync(args, cancellationToken);
}

/// <summary>Owns one candidate supervisor exchange over injected platform seams.</summary>
public sealed class CandidateProcessSupervisor
{
    private readonly ICandidateSupervisorPlatform platform;
    private readonly ICandidateSupervisorControlFactory controlFactory;
    private readonly ICandidateSupervisorPayloadLauncher payloadLauncher;
    private readonly ICandidateSupervisorStandardStreamsFactory standardStreamsFactory;
    private readonly ICandidateSupervisorNative native;
    private readonly TimeSpan controlTimeout;

    /// <summary>Creates a supervisor with explicit protocol, payload, stdio, platform, and native seams.</summary>
    public CandidateProcessSupervisor(ICandidateSupervisorPlatform platform,
        ICandidateSupervisorControlFactory controlFactory, ICandidateSupervisorPayloadLauncher payloadLauncher,
        ICandidateSupervisorStandardStreamsFactory standardStreamsFactory, ICandidateSupervisorNative native,
        TimeSpan? controlTimeout = null)
    {
        this.platform = platform ?? throw new ArgumentNullException(nameof(platform));
        this.controlFactory = controlFactory ?? throw new ArgumentNullException(nameof(controlFactory));
        this.payloadLauncher = payloadLauncher ?? throw new ArgumentNullException(nameof(payloadLauncher));
        this.standardStreamsFactory = standardStreamsFactory ?? throw new ArgumentNullException(nameof(standardStreamsFactory));
        this.native = native ?? throw new ArgumentNullException(nameof(native));
        this.controlTimeout = controlTimeout ?? TimeSpan.FromSeconds(CandidateProcessOwner.ControlTimeoutSeconds);
        if (this.controlTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(controlTimeout));
    }

    /// <summary>Creates the production supervisor while keeping the channel and stdio adapters private.</summary>
    internal static CandidateProcessSupervisor CreateDefault() => new(
        new RuntimePlatform(), new NamedPipeControlFactory(), new CandidateSupervisorPayloadLauncher(),
        new CandidateSupervisorStandardStreamsFactory(), new CandidateSupervisorNative());

    /// <summary>Runs one private supervisor exchange and returns its fixed process result.</summary>
    public Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (!TryReadArguments(args, out var pipeName, out var correlation, out var runtimeConfig, out var depsFile,
                out var payloadAssembly))
            return Task.FromResult(ToolExitCode.ResolutionFailure);

        return RunAsync(pipeName, correlation, runtimeConfig, depsFile, payloadAssembly, cancellationToken);
    }

    /// <summary>Runs one private supervisor exchange and returns its fixed process result.</summary>
    public Task<int> RunAsync(string pipeName, Guid correlation, string runtimeConfig, string depsFile,
        string payloadAssembly, CancellationToken cancellationToken) =>
        string.IsNullOrEmpty(pipeName) || string.IsNullOrEmpty(runtimeConfig) || string.IsNullOrEmpty(depsFile) ||
        string.IsNullOrEmpty(payloadAssembly)
            ? Task.FromResult(ToolExitCode.ResolutionFailure)
            : RunCoreAsync(pipeName, correlation, runtimeConfig, depsFile, payloadAssembly, cancellationToken);

    private static bool TryReadArguments(string[]? args, out string pipeName, out Guid correlation,
        out string runtimeConfig, out string depsFile, out string payloadAssembly)
    {
        pipeName = string.Empty;
        correlation = default;
        runtimeConfig = string.Empty;
        depsFile = string.Empty;
        payloadAssembly = string.Empty;
        if (args is null || args.Length != 7 || args[0] != "--candidate-inspection" ||
            args[1] != "--candidate-owner" || string.IsNullOrEmpty(args[2]) || string.IsNullOrEmpty(args[4]) ||
            string.IsNullOrEmpty(args[5]) || string.IsNullOrEmpty(args[6]) ||
            args[3] is null || !Guid.TryParseExact(args[3], "N", out correlation))
            return false;

        pipeName = args[2];
        runtimeConfig = args[4];
        depsFile = args[5];
        payloadAssembly = args[6];
        return true;
    }

    private async Task<int> RunCoreAsync(string pipeName, Guid correlation, string runtimeConfig, string depsFile,
        string payloadAssembly, CancellationToken cancellationToken)
    {
        if (!platform.IsSupported)
            return ToolExitCode.ResolutionFailure;

        ICandidateSupervisorControl? control = null;
        ICandidateSupervisorPayload? payload = null;
        ICandidateSupervisorStandardStreams? standardStreams = null;
        Task? ownerClosed = null;
        Task<int>? payloadTask = null;
        var unixOwnerEstablished = false;
        var goSent = false;
        var statusSent = false;
        try
        {
            control = controlFactory.Create(pipeName);
            await ConnectAsync(control, cancellationToken).ConfigureAwait(false);

            if (platform.IsUnix)
            {
                native.EstablishOwner();
                unixOwnerEstablished = true;
            }

            await WriteReadyAsync(control.Stream, correlation, cancellationToken).ConfigureAwait(false);
            if (!await ReadGoAsync(control.Stream, cancellationToken).ConfigureAwait(false))
            {
                AbortUnixOwner(unixOwnerEstablished);
                return ToolExitCode.ResolutionFailure;
            }
            goSent = true;

            // Start watching before creating the payload. A frontend that disappears between GO and payload
            // creation must not leave a supervisor waiting forever with a newly-created owner scope.
            ownerClosed = WatchOwnerAsync(control.Stream, CancellationToken.None);
            Observe(ownerClosed);
            if (ownerClosed.IsCompleted)
            {
                AbortUnixOwner(unixOwnerEstablished);
                return ToolExitCode.ResolutionFailure;
            }

            payload = payloadLauncher.Start(runtimeConfig, depsFile, payloadAssembly);
            standardStreams = standardStreamsFactory.Open();
            payloadTask = ForwardPayloadAsync(payload, standardStreams, cancellationToken);
            Observe(payloadTask);
            var completed = await Task.WhenAny(payloadTask, ownerClosed).ConfigureAwait(false);
            if (completed == ownerClosed)
            {
                AbortUnixOwner(unixOwnerEstablished);
                return ToolExitCode.ResolutionFailure;
            }

            var payloadExitCode = await payloadTask.ConfigureAwait(false);
            if (ownerClosed.IsCompleted)
            {
                AbortUnixOwner(unixOwnerEstablished);
                await ObserveOwnerAfterAbortAsync(ownerClosed).ConfigureAwait(false);
                return ToolExitCode.ResolutionFailure;
            }

            await WriteStatusAsync(control.Stream, payloadExitCode, cancellationToken).ConfigureAwait(false);
            statusSent = true;

            // STATUS authorizes frontend scope termination after all forwarding has flushed. Retain the
            // anchor until that termination or owner-channel loss; EOF aborts the complete Unix group.
            await ownerClosed.ConfigureAwait(false);
            AbortUnixOwner(unixOwnerEstablished);
            return payloadExitCode;
        }
        catch (Exception)
        {
            // Supervisor failures, including process-trust failures, never print exception text: its
            // stdout/stderr are the private candidate transport and the frontend maps this to a fixed refusal.
            if (goSent && ownerClosed is not null)
            {
                await RetainOwnerAfterFailureAsync(control, ownerClosed,
                        unixOwnerEstablished, statusSent).ConfigureAwait(false);
            }
            else
                AbortUnixOwner(unixOwnerEstablished);
            return ToolExitCode.ResolutionFailure;
        }
        finally
        {
            Exception? cleanupFailure = null;
            CandidateSupervisorCleanup.CaptureDispose(standardStreams, ref cleanupFailure);
            CandidateSupervisorCleanup.CaptureDispose(payload, ref cleanupFailure);
            CandidateSupervisorCleanup.CaptureDispose(control, ref cleanupFailure);
            if (cleanupFailure is not null)
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private async Task ConnectAsync(ICandidateSupervisorControl control, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(controlTimeout);
        await control.ConnectAsync(deadline.Token).ConfigureAwait(false);
    }

    private async Task<bool> ReadGoAsync(Stream control, CancellationToken cancellationToken)
    {
        var value = await ReadByteAsync(control, cancellationToken, controlTimeout).ConfigureAwait(false);
        return value == CandidateProcessOwner.Go;
    }

    private static async Task WriteReadyAsync(Stream control, Guid correlation, CancellationToken cancellationToken)
    {
        var frame = new byte[CandidateProcessOwner.ReadyFrameLength];
        frame[0] = CandidateProcessOwner.Ready;
        correlation.TryWriteBytes(frame.AsSpan(1));
        await control.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await control.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WatchOwnerAsync(Stream control, CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        // EOF loses the lease; no frontend-to-owner frame is valid after GO, so any byte also aborts.
        await control.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte> ReadByteAsync(Stream stream, CancellationToken cancellationToken, TimeSpan timeout)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        var buffer = new byte[1];
        var read = await stream.ReadAsync(buffer.AsMemory(), deadline.Token).ConfigureAwait(false);
        return read == 1 ? buffer[0] : (byte)0;
    }

    private async Task<int> ForwardPayloadAsync(ICandidateSupervisorPayload payload,
        ICandidateSupervisorStandardStreams standardStreams, CancellationToken cancellationToken)
    {
        var input = CopyAndCloseAsync(standardStreams.StandardInput, payload.StandardInput, cancellationToken);
        var output = CopyAndFlushAsync(payload.StandardOutput, standardStreams.StandardOutput, cancellationToken);
        var error = CopyAndFlushAsync(payload.StandardError, standardStreams.StandardError, cancellationToken);
        var wait = payload.WaitForExitAsync(cancellationToken);
        var pumps = Task.WhenAll(input, output, error, wait);
        Observe(pumps);
        await pumps.ConfigureAwait(false);
        return payload.ExitCode;
    }

    private static async Task CopyAndCloseAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(destination, 64 * 1024, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Exception? cleanupFailure = null;
            try { await destination.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { cleanupFailure ??= failure; }
            try { await source.DisposeAsync().ConfigureAwait(false); }
            catch (Exception failure) { cleanupFailure ??= failure; }
            if (cleanupFailure is not null)
                ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
        }
    }

    private static async Task CopyAndFlushAsync(Stream source, Stream destination, CancellationToken cancellationToken)
    {
        try
        {
            await source.CopyToAsync(destination, 64 * 1024, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteStatusAsync(Stream control, int exitCode, CancellationToken cancellationToken)
    {
        var frame = new byte[CandidateProcessOwner.StatusFrameLength];
        frame[0] = CandidateProcessOwner.Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(1), exitCode);
        await control.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await control.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task RetainOwnerAfterFailureAsync(ICandidateSupervisorControl? control, Task ownerClosed,
        bool unixOwnerEstablished, bool statusSent)
    {
        // Keep the anchor alive while the frontend still owns cleanup. STATUS requests scope termination,
        // which closes all inherited output handles before the frontend accepts any response.
        try
        {
            if (!statusSent && control is not null)
                await WriteStatusAsync(control.Stream, ToolExitCode.ResolutionFailure, CancellationToken.None)
                    .ConfigureAwait(false);
        }
        catch (Exception)
        {
            AbortUnixOwner(unixOwnerEstablished);
            return;
        }

        try
        {
            await ownerClosed.ConfigureAwait(false);
            AbortUnixOwner(unixOwnerEstablished);
        }
        catch (Exception)
        {
            // Broken control is loss of the frontend's cleanup authority. Kill the Unix scope immediately.
            AbortUnixOwner(unixOwnerEstablished);
        }
    }

    private void AbortUnixOwner(bool unixOwnerEstablished)
    {
        if (unixOwnerEstablished)
            native.AbortOwner();
    }

    private static async Task ObserveOwnerAfterAbortAsync(Task ownerTask)
    {
        try { await ownerTask.ConfigureAwait(false); }
        catch (Exception) { /* Observe broken-pipe/cancellation faults without logging. */ }
    }

    private static void Observe(Task task) => _ = task.ContinueWith(fault => _ = fault.Exception,
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private sealed class RuntimePlatform : ICandidateSupervisorPlatform
    {
        public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        public bool IsUnix => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
    }

    private sealed class NamedPipeControlFactory : ICandidateSupervisorControlFactory
    {
        public ICandidateSupervisorControl Create(string pipeName) => new NamedPipeControl(pipeName);
    }

    private sealed class NamedPipeControl : ICandidateSupervisorControl
    {
        private readonly NamedPipeClientStream pipe;

        public NamedPipeControl(string pipeName) => pipe = new NamedPipeClientStream(".", pipeName,
            PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        public Stream Stream => pipe;
        public Task ConnectAsync(CancellationToken cancellationToken) => pipe.ConnectAsync(cancellationToken);
        public void Dispose() => pipe.Dispose();
    }

    /// <summary>Starts a candidate payload with injectable process path and start operations.</summary>
    public sealed class CandidateSupervisorPayloadLauncher : ICandidateSupervisorPayloadLauncher
    {
        private readonly Func<string?> processPath;
        private readonly Func<ProcessStartInfo, Process?> start;

        /// <summary>Creates the production process launcher.</summary>
        public CandidateSupervisorPayloadLauncher()
            : this(() => Environment.ProcessPath, processStartInfo => Process.Start(processStartInfo))
        {
        }

        /// <summary>Creates a launcher with explicit host-process seams for branch-level tests.</summary>
        public CandidateSupervisorPayloadLauncher(Func<string?> processPath, Func<ProcessStartInfo, Process?> start)
        {
            this.processPath = processPath ?? throw new ArgumentNullException(nameof(processPath));
            this.start = start ?? throw new ArgumentNullException(nameof(start));
        }

        public ICandidateSupervisorPayload Start(string runtimeConfig, string depsFile, string payloadAssembly)
        {
            var executable = processPath();
            if (string.IsNullOrWhiteSpace(executable) || string.IsNullOrWhiteSpace(payloadAssembly))
                throw new InvalidOperationException();

            var startInfo = new ProcessStartInfo(executable)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("exec");
            startInfo.ArgumentList.Add("--runtimeconfig");
            startInfo.ArgumentList.Add(runtimeConfig);
            startInfo.ArgumentList.Add("--depsfile");
            startInfo.ArgumentList.Add(depsFile);
            startInfo.ArgumentList.Add(payloadAssembly);
            startInfo.ArgumentList.Add("--candidate-inspection");
            return new ProcessPayload(start(startInfo) ?? throw new InvalidOperationException());
        }
    }

    private sealed class ProcessPayload(Process process) : ICandidateSupervisorPayload
    {
        public Stream StandardInput => process.StandardInput.BaseStream;
        public Stream StandardOutput => process.StandardOutput.BaseStream;
        public Stream StandardError => process.StandardError.BaseStream;
        public int ExitCode => process.ExitCode;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => process.WaitForExitAsync(cancellationToken);
        public void Dispose() => process.Dispose();
    }

    /// <summary>Performs Unix session/process-group ownership with injectable native calls.</summary>
    public sealed class CandidateSupervisorNative : ICandidateSupervisorNative
    {
        private readonly Func<int> setSessionId;
        private readonly Func<int> getProcessId;
        private readonly Func<int> getProcessGroupId;
        private readonly Func<int, int, int> kill;

        /// <summary>Creates the production Unix ownership adapter.</summary>
        public CandidateSupervisorNative()
            : this(SetSessionId, GetProcessId, GetProcessGroupId, Kill)
        {
        }

        /// <summary>Creates an ownership adapter with explicit native calls for branch-level tests.</summary>
        public CandidateSupervisorNative(Func<int> setSessionId, Func<int> getProcessId,
            Func<int> getProcessGroupId, Func<int, int, int> kill)
        {
            this.setSessionId = setSessionId ?? throw new ArgumentNullException(nameof(setSessionId));
            this.getProcessId = getProcessId ?? throw new ArgumentNullException(nameof(getProcessId));
            this.getProcessGroupId = getProcessGroupId ?? throw new ArgumentNullException(nameof(getProcessGroupId));
            this.kill = kill ?? throw new ArgumentNullException(nameof(kill));
        }

        public void EstablishOwner()
        {
            if (setSessionId() < 0 || getProcessGroupId() != getProcessId())
                throw new InvalidOperationException();
        }

        public void AbortOwner()
        {
            var processId = getProcessId();
            if (processId > 1)
                _ = kill(-processId, 9);
        }

        [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
        private static extern int SetSessionId();
        [DllImport("libc", EntryPoint = "getpid", SetLastError = true)]
        private static extern int GetProcessId();
        [DllImport("libc", EntryPoint = "getpgrp", SetLastError = true)]
        private static extern int GetProcessGroupId();
        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        private static extern int Kill(int processId, int signal);
    }
}

/// <summary>Opens the supervisor's parent-side standard streams with injectable acquisition seams.</summary>
public sealed class CandidateSupervisorStandardStreamsFactory : ICandidateSupervisorStandardStreamsFactory
{
    private readonly Func<Stream> openInput;
    private readonly Func<Stream> openOutput;
    private readonly Func<Stream> openError;

    /// <summary>Creates the production console-backed standard-stream factory.</summary>
    public CandidateSupervisorStandardStreamsFactory()
        : this(Console.OpenStandardInput, Console.OpenStandardOutput, Console.OpenStandardError)
    {
    }

    /// <summary>Creates a factory with explicit acquisition seams for ownership branch tests.</summary>
    public CandidateSupervisorStandardStreamsFactory(Func<Stream> openInput, Func<Stream> openOutput,
        Func<Stream> openError)
    {
        this.openInput = openInput ?? throw new ArgumentNullException(nameof(openInput));
        this.openOutput = openOutput ?? throw new ArgumentNullException(nameof(openOutput));
        this.openError = openError ?? throw new ArgumentNullException(nameof(openError));
    }

    public ICandidateSupervisorStandardStreams Open()
    {
        Stream? input = null;
        Stream? output = null;
        Stream? error = null;
        try
        {
            input = openInput();
            output = openOutput();
            error = openError();
            return new CandidateSupervisorStandardStreams(input, output, error);
        }
        catch (Exception failure)
        {
            Exception? cleanupFailure = failure;
            CandidateSupervisorCleanup.CaptureDispose(error, ref cleanupFailure);
            CandidateSupervisorCleanup.CaptureDispose(output, ref cleanupFailure);
            CandidateSupervisorCleanup.CaptureDispose(input, ref cleanupFailure);
            ExceptionDispatchInfo.Capture(cleanupFailure!).Throw();
            throw;
        }
    }
}

/// <summary>Owns the three parent-side standard streams and always attempts every close.</summary>
public sealed class CandidateSupervisorStandardStreams : ICandidateSupervisorStandardStreams
{
    public CandidateSupervisorStandardStreams(Stream standardInput, Stream standardOutput, Stream standardError)
    {
        StandardInput = standardInput ?? throw new ArgumentNullException(nameof(standardInput));
        StandardOutput = standardOutput ?? throw new ArgumentNullException(nameof(standardOutput));
        StandardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
    }

    public Stream StandardInput { get; }
    public Stream StandardOutput { get; }
    public Stream StandardError { get; }

    public void Dispose()
    {
        Exception? failure = null;
        CandidateSupervisorCleanup.CaptureDispose(StandardInput, ref failure);
        CandidateSupervisorCleanup.CaptureDispose(StandardOutput, ref failure);
        CandidateSupervisorCleanup.CaptureDispose(StandardError, ref failure);
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}

internal static class CandidateSupervisorCleanup
{
    internal static void CaptureDispose(IDisposable? resource, ref Exception? failure)
    {
        if (resource is null)
            return;
        try { resource.Dispose(); }
        catch (Exception cleanupFailure) { failure ??= cleanupFailure; }
    }
}

/// <summary>Describes whether the worker can use its current process platform.</summary>
public interface ICandidateSupervisorPlatform
{
    bool IsSupported { get; }
    bool IsUnix { get; }
}

/// <summary>Creates one private control channel for a supervisor invocation.</summary>
public interface ICandidateSupervisorControlFactory
{
    ICandidateSupervisorControl Create(string pipeName);
}

/// <summary>The connected private control channel used by a supervisor.</summary>
public interface ICandidateSupervisorControl : IDisposable
{
    Stream Stream { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
}

/// <summary>Starts the selected payload with its host runtime configuration.</summary>
public interface ICandidateSupervisorPayloadLauncher
{
    ICandidateSupervisorPayload Start(string runtimeConfig, string depsFile, string payloadAssembly);
}

/// <summary>The process and redirected streams owned by a supervisor invocation.</summary>
public interface ICandidateSupervisorPayload : IDisposable
{
    Stream StandardInput { get; }
    Stream StandardOutput { get; }
    Stream StandardError { get; }
    int ExitCode { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

/// <summary>Opens the supervisor's parent-side standard streams.</summary>
public interface ICandidateSupervisorStandardStreamsFactory
{
    ICandidateSupervisorStandardStreams Open();
}

/// <summary>The parent-side standard streams forwarded from the selected payload.</summary>
public interface ICandidateSupervisorStandardStreams : IDisposable
{
    Stream StandardInput { get; }
    Stream StandardOutput { get; }
    Stream StandardError { get; }
}

/// <summary>Performs native session/process-group ownership operations.</summary>
public interface ICandidateSupervisorNative
{
    void EstablishOwner();
    void AbortOwner();
}
