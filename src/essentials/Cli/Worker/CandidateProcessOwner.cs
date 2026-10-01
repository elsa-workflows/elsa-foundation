using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
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

    private const int UnixSigKill = 9;
    private static readonly TimeSpan ControlTimeout = TimeSpan.FromSeconds(ControlTimeoutSeconds);

    /// <summary>Runs the worker's private supervisor mode. The frontend owns the corresponding cleanup.</summary>
    internal static async Task<int> RunSupervisorAsync(string[] args, CancellationToken cancellationToken)
    {
        if (!TryReadArguments(args, out var pipeName, out var correlation, out var runtimeConfig, out var depsFile,
                out var payloadAssembly))
            return ToolExitCode.ResolutionFailure;
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return ToolExitCode.ResolutionFailure;

        NamedPipeClientStream? control = null;
        Process? payload = null;
        Task? ownerClosed = null;
        Task<int>? payloadTask = null;
        Stream? parentOutput = null;
        Stream? parentError = null;
        var unixOwnerEstablished = false;
        var goSent = false;
        var statusSent = false;
        try
        {
            control = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await ConnectAsync(control, cancellationToken).ConfigureAwait(false);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                EstablishUnixOwner();
                unixOwnerEstablished = true;
            }

            await WriteReadyAsync(control, correlation, cancellationToken).ConfigureAwait(false);
            if (!await ReadGoAsync(control, cancellationToken).ConfigureAwait(false))
            {
                AbortUnixOwner(unixOwnerEstablished);
                return ToolExitCode.ResolutionFailure;
            }
            goSent = true;

            // Start watching before creating the payload. A frontend that disappears between GO and payload
            // creation must not leave a supervisor waiting forever with a newly-created owner scope.
            ownerClosed = WatchOwnerAsync(control, CancellationToken.None);
            Observe(ownerClosed);
            if (ownerClosed.IsCompleted)
            {
                AbortUnixOwner(unixOwnerEstablished);
                return ToolExitCode.ResolutionFailure;
            }

            payload = StartPayload(runtimeConfig, depsFile, payloadAssembly);
            parentOutput = Console.OpenStandardOutput();
            parentError = Console.OpenStandardError();
            payloadTask = ForwardPayloadAsync(payload, parentOutput, parentError, cancellationToken);
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
            await WriteStatusAsync(control, payloadExitCode, cancellationToken).ConfigureAwait(false);
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
            parentOutput?.Dispose();
            parentError?.Dispose();
            payload?.Dispose();
            control?.Dispose();
        }
    }

    private static bool TryReadArguments(string[] args, out string pipeName, out Guid correlation,
        out string runtimeConfig, out string depsFile, out string payloadAssembly)
    {
        pipeName = string.Empty;
        correlation = default;
        runtimeConfig = string.Empty;
        depsFile = string.Empty;
        payloadAssembly = string.Empty;
        if (args.Length != 7 || args[0] != "--candidate-inspection" || args[1] != "--candidate-owner" ||
            args[2].Length == 0 || args[4].Length == 0 || args[5].Length == 0 || args[6].Length == 0 ||
            !Guid.TryParseExact(args[3], "N", out correlation))
            return false;

        pipeName = args[2];
        runtimeConfig = args[4];
        depsFile = args[5];
        payloadAssembly = args[6];
        return true;
    }

    private static async Task ConnectAsync(NamedPipeClientStream control, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ControlTimeout);
        await control.ConnectAsync(deadline.Token).ConfigureAwait(false);
    }

    private static async Task WriteReadyAsync(Stream control, Guid correlation, CancellationToken cancellationToken)
    {
        var frame = new byte[ReadyFrameLength];
        frame[0] = Ready;
        correlation.TryWriteBytes(frame.AsSpan(1));
        await control.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await control.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> ReadGoAsync(Stream control, CancellationToken cancellationToken)
    {
        var value = await ReadByteAsync(control, cancellationToken, ControlTimeout).ConfigureAwait(false);
        return value == Go;
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

    private static Process StartPayload(string runtimeConfig, string depsFile, string payloadAssembly)
    {
        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || string.IsNullOrWhiteSpace(payloadAssembly))
            throw new InvalidOperationException();

        var start = new ProcessStartInfo(processPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add("--runtimeconfig");
        start.ArgumentList.Add(runtimeConfig);
        start.ArgumentList.Add("--depsfile");
        start.ArgumentList.Add(depsFile);
        start.ArgumentList.Add(payloadAssembly);
        start.ArgumentList.Add("--candidate-inspection");
        return Process.Start(start) ?? throw new InvalidOperationException();
    }

    private static async Task<int> ForwardPayloadAsync(Process payload, Stream parentOutput, Stream parentError,
        CancellationToken cancellationToken)
    {
        var input = CopyAndCloseAsync(Console.OpenStandardInput(), payload.StandardInput.BaseStream, cancellationToken);
        var output = CopyAndFlushAsync(payload.StandardOutput.BaseStream, parentOutput, cancellationToken);
        var error = CopyAndFlushAsync(payload.StandardError.BaseStream, parentError, cancellationToken);
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
            await destination.DisposeAsync().ConfigureAwait(false);
            await source.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void Observe(Task task) => _ = task.ContinueWith(fault => _ = fault.Exception,
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

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
        var frame = new byte[StatusFrameLength];
        frame[0] = Status;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(1), exitCode);
        await control.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await control.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RetainOwnerAfterFailureAsync(Stream? control, Task ownerClosed,
        bool unixOwnerEstablished, bool statusSent)
    {
        // Keep the anchor alive while the frontend still owns cleanup. STATUS requests scope termination,
        // which closes all inherited output handles before the frontend accepts any response.
        try
        {
            if (!statusSent && control is not null)
                await WriteStatusAsync(control, ToolExitCode.ResolutionFailure, CancellationToken.None).ConfigureAwait(false);
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

    private static async Task ObserveOwnerAfterAbortAsync(Task ownerTask)
    {
        try
        {
            await ownerTask.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The owner is already being torn down; observe broken-pipe/cancellation faults without logging.
        }
    }

    private static void EstablishUnixOwner()
    {
        if (Native.SetSessionId() < 0)
            throw new InvalidOperationException();
        if (Native.GetProcessGroupId() != Native.GetProcessId())
            throw new InvalidOperationException();
    }

    private static void AbortUnixOwner(bool unixOwnerEstablished)
    {
        if (!unixOwnerEstablished || (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()))
            return;
        var processId = Native.GetProcessId();
        if (processId > 1)
            _ = Native.Kill(-processId, UnixSigKill);
    }

    private static class Native
    {
        [DllImport("libc", EntryPoint = "setsid", SetLastError = true)]
        internal static extern int SetSessionId();

        [DllImport("libc", EntryPoint = "getpid", SetLastError = true)]
        internal static extern int GetProcessId();

        [DllImport("libc", EntryPoint = "getpgrp", SetLastError = true)]
        internal static extern int GetProcessGroupId();

        [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
        internal static extern int Kill(int processId, int signal);

    }
}
