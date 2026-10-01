using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Cli.Worker;

namespace Elsa.Cli;

/// <summary>Owns one bounded candidate worker exchange and termination of its child process.</summary>
public sealed class CandidateWorkerProcess
{
    private const int RequestMaximumBytes = 8 * 1024 * 1024;
    private const int ResponseMaximumBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(5);
    private const string RequestInvalidMessage = "The candidate request is invalid.";
    private const string RequestTooLargeMessage = "The candidate request exceeds the supported size limit.";
    private const string ResponseInvalidMessage = "The candidate worker response is invalid.";
    private const string ResponseTooLargeMessage = "The candidate worker response exceeds the supported size limit.";
    private const string OperationFailedMessage = "Candidate inspection could not be completed.";

    private static readonly IReadOnlyDictionary<string, (int ExitCode, string Message)> FixedRefusals =
        new Dictionary<string, (int, string)>(StringComparer.Ordinal)
        {
            ["candidate-trust-required"] = (ToolExitCode.Refusal, "Candidate inspection requires explicit trust of host code."),
            ["candidate-selection-conflict"] = (ToolExitCode.Refusal, "The candidate selection conflicts with the host composition."),
            ["candidate-capture-invalid"] = (ToolExitCode.Refusal, "The captured candidate configuration is invalid."),
            ["candidate-request-invalid"] = (ToolExitCode.Refusal, RequestInvalidMessage),
            ["candidate-request-too-large"] = (ToolExitCode.Refusal, RequestTooLargeMessage),
            ["candidate-capability-unavailable"] = (ToolExitCode.ResolutionFailure, "The selected host does not support candidate inspection."),
            ["candidate-response-invalid"] = (ToolExitCode.ResolutionFailure, ResponseInvalidMessage),
            ["candidate-response-too-large"] = (ToolExitCode.ResolutionFailure, ResponseTooLargeMessage),
            ["candidate-inspection-timeout"] = (ToolExitCode.ResolutionFailure, "Candidate inspection did not finish before the timeout."),
            ["candidate-inspection-cancelled"] = (ToolExitCode.Refusal, "Candidate inspection was cancelled."),
            ["candidate-host-unavailable"] = (ToolExitCode.ResolutionFailure, "The selected host could not complete candidate inspection."),
            ["candidate-closure-changed"] = (ToolExitCode.ResolutionFailure, "The selected host package closure changed during inspection."),
            ["candidate-package-unavailable"] = (ToolExitCode.ResolutionFailure, "The selected host package closure could not be loaded."),
            ["candidate-inspection-failed"] = (ToolExitCode.ResolutionFailure, OperationFailedMessage),
            ["candidate-cleanup-failed"] = (ToolExitCode.ResolutionFailure, "Candidate inspection could not safely close its worker process."),
            ["candidate-environment-input-invalid"] = (ToolExitCode.Refusal, "The explicit environment input is invalid."),
            ["candidate-environment-input-too-large"] = (ToolExitCode.Refusal, "The explicit environment input exceeds the supported size limit."),
            ["candidate-environment-key-collision"] = (ToolExitCode.Refusal, "The explicit environment input contains colliding keys."),
            ["candidate-environment-prefix-unsupported"] = (ToolExitCode.Refusal, "The explicit environment input contains an unsupported service prefix."),
            ["candidate-environment-host-unenrolled"] = (ToolExitCode.ResolutionFailure, "The selected host is not enrolled for explicit environment inspection."),
            ["resource-selection-invalid"] = (ToolExitCode.Refusal, "The persistence resource selection is invalid."),
            ["resource-not-found"] = (ToolExitCode.Refusal, "A selected persistence resource was not found."),
            ["resource-definition-invalid"] = (ToolExitCode.Refusal, "A selected persistence resource definition is invalid."),
            ["resource-configurator-unsupported"] = (ToolExitCode.Refusal, "A selected persistence resource configurator is unsupported."),
            ["resource-required-feature-disabled"] = (ToolExitCode.Refusal, "A persistence resource requires a disabled feature."),
            ["resource-legacy-conflict"] = (ToolExitCode.Refusal, "Legacy persistence configuration conflicts with a selected resource."),
            ["resource-ownership-unresolved"] = (ToolExitCode.Refusal, "Persistence resource ownership could not be resolved."),
            ["resource-context-conflict"] = (ToolExitCode.Refusal, "Selected persistence resources have incompatible context settings.")
        };

    private readonly Func<ProcessStartInfo, ICandidateProcessHandle>? start;
    private readonly string workerAssembly;
    private readonly TimeProvider clock;

    public CandidateWorkerProcess(Func<ProcessStartInfo, ICandidateProcessHandle>? start = null,
        string? workerAssembly = null, TimeProvider? clock = null)
    {
        this.start = start;
        this.workerAssembly = workerAssembly ?? Path.Join(AppContext.BaseDirectory, WorkerProcess.WorkerAssemblyFileName);
        this.clock = clock ?? TimeProvider.System;
    }

    public static IReadOnlyList<string> Arguments(HostLayout host, string workerAssembly)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(workerAssembly);
        return [.. WorkerProcess.Arguments(host, workerAssembly), "--candidate-inspection"];
    }

    /// <summary>Runs a captured candidate without exposing child diagnostics or partial output.</summary>
    /// <exception cref="CliRefusal">The exchange, its bounded deadline or owned-process cleanup failed.</exception>
    public async Task<WorkerResponse> RunAsync(HostLayout host, WorkerRequest request, int timeoutSeconds = 60,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(request);

        if (timeoutSeconds is < 1 or > 300)
            throw FixedRefusal("candidate-request-invalid");
        if (cancellationToken.IsCancellationRequested)
            throw FixedRefusal("candidate-inspection-cancelled");

        var requestBytes = SerializeRequest(request);
        try
        {
            if (cancellationToken.IsCancellationRequested)
                throw FixedRefusal("candidate-inspection-cancelled");

            return await RunSerializedAsync(host, requestBytes, request.Candidate!, environmentInput: false,
                timeoutSeconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(requestBytes);
        }
    }

    /// <summary>Runs one capture-owned explicit-environment request without accepting an independent request.</summary>
    public async Task<WorkerResponse> RunEnvironmentAsync(CompositionInspectionCapture capture,
        IReadOnlyList<string> packageRoots, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(packageRoots);
        if (timeoutSeconds is < 1 or > 300)
            throw FixedRefusal("candidate-request-invalid");
        if (cancellationToken.IsCancellationRequested)
        {
            capture.Dispose();
            throw FixedRefusal("candidate-inspection-cancelled");
        }

        var request = capture.BeginEnvironmentInspection(packageRoots);
        if (cancellationToken.IsCancellationRequested)
        {
            capture.Dispose();
            throw FixedRefusal("candidate-inspection-cancelled");
        }

        var host = HostLayoutFrom(request);
        var requestBytes = SerializeEnvironmentRequest(request);
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                capture.Dispose();
                throw FixedRefusal("candidate-inspection-cancelled");
            }

            return await RunSerializedAsync(host, requestBytes, request.Candidate!, environmentInput: true,
                timeoutSeconds, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(requestBytes);
        }
    }

    private async Task<WorkerResponse> RunSerializedAsync(HostLayout host, byte[] requestBytes,
        WorkerCandidatePayload expectedCandidate, bool environmentInput, int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(requestBytes);
        ArgumentNullException.ThrowIfNull(expectedCandidate);

        ProcessStartInfo startInfo;
        try
        {
            startInfo = new ProcessStartInfo(DotnetMuxer.Path())
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in Arguments(host, workerAssembly))
                startInfo.ArgumentList.Add(argument);
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            throw FixedRefusal("candidate-inspection-failed");
        }

        long operationStarted;
        CancellationTokenSource deadline;
        try
        {
            operationStarted = clock.GetTimestamp();
            deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds), clock);
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            throw FixedRefusal("candidate-inspection-failed");
        }
        using var deadlineLease = deadline;
        ICandidateProcessHandle? handle = null;
        var streams = new List<Stream>(3);
        var exchangeTasks = new List<Task>(4);
        WorkerResponse? response = null;
        CliRefusal? refusalResult = null;
        ExceptionDispatchInfo? fatalFailure = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ownedHandle = StartProcess(startInfo);
            handle = ownedHandle;
            var cancelledDuringStart = cancellationToken.IsCancellationRequested;

            // Take ownership before observing cancellation or any stream property. Even a stream getter
            // that fails must leave the returned process in this method's cleanup path.
            Stream? input = null;
            Stream? output = null;
            Stream? error = null;
            Exception? streamFailure = null;
            Exception? fatalStreamFailure = null;
            TryGetStream(() => ownedHandle.StandardInput, streams, value => input = value,
                ref streamFailure, ref fatalStreamFailure);
            TryGetStream(() => ownedHandle.StandardOutput, streams, value => output = value,
                ref streamFailure, ref fatalStreamFailure);
            TryGetStream(() => ownedHandle.StandardError, streams, value => error = value,
                ref streamFailure, ref fatalStreamFailure);

            if (fatalStreamFailure is not null)
                throw fatalStreamFailure;
            if (cancelledDuringStart || cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            ThrowIfTimedOut(deadline.Token, operationStarted, timeoutSeconds);
            if (streamFailure is not null)
                throw streamFailure;
            if (input is null || output is null || error is null)
                throw new InvalidOperationException();

            response = await ExchangeAsync(ownedHandle, input, output, error, requestBytes, expectedCandidate,
                environmentInput, timeoutSeconds, operationStarted, deadline.Token, cancellationToken, exchangeTasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            refusalResult = FixedRefusal("candidate-inspection-cancelled");
        }
        catch (CandidateTimedOutException)
        {
            refusalResult = FixedRefusal("candidate-inspection-timeout");
        }
        catch (WorkerRefusal refusal)
        {
            refusalResult = ToCliRefusal(refusal);
        }
        catch (CliRefusal refusal)
        {
            refusalResult = ToCliRefusal(refusal);
        }
        catch (Exception failure) when (!IsNonFatal(failure))
        {
            fatalFailure = ExceptionDispatchInfo.Capture(failure);
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            refusalResult = FixedRefusal("candidate-inspection-cancelled");
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            refusalResult = FixedRefusal("candidate-inspection-failed");
        }

        CleanupResult cleanup = default;
        if (handle is not null)
            cleanup = await CleanupAsync(handle, streams, exchangeTasks).ConfigureAwait(false);

        // A process-trust exception remains visible after owned resources have been handled.
        fatalFailure?.Throw();
        cleanup.FatalFailure?.Throw();
        if (cleanup.Failed)
            throw FixedRefusal("candidate-cleanup-failed");
        if (refusalResult is not null)
            throw refusalResult;
        return response ?? throw FixedRefusal("candidate-inspection-failed");
    }

    private static HostLayout HostLayoutFrom(CandidateEnvironmentWorkerRequestV2 request)
    {
        if (request.HostDirectory is null || request.HostName is null || request.DepsFile is null)
            throw FixedRefusal("candidate-capture-invalid");

        return new HostLayout(request.HostDirectory, request.HostName,
            Path.Join(request.HostDirectory, request.HostName + ".runtimeconfig.json"), request.DepsFile);
    }

    private ICandidateProcessHandle StartProcess(ProcessStartInfo startInfo)
    {
        if (start is not null)
            return start(startInfo) ?? throw new InvalidOperationException();

        return CandidateProcessHandle.Start(startInfo);
    }

    private async Task<WorkerResponse> ExchangeAsync(ICandidateProcessHandle handle, Stream input, Stream output,
        Stream error, byte[] requestBytes, WorkerCandidatePayload expectedCandidate, bool environmentInput,
        int timeoutSeconds, long operationStarted,
        CancellationToken deadlineToken, CancellationToken cancellationToken, ICollection<Task> ownedTasks)
    {
        using var exchange = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineToken);
        var writeTask = WriteRequestAndCloseAsync(input, requestBytes, exchange.Token);
        var outputTask = ReadResponseAsync(output, exchange.Token);
        var errorTask = DrainErrorAsync(error, exchange.Token);
        var pumps = new List<Task> { writeTask, outputTask, errorTask };
        foreach (var pump in pumps)
            ownedTasks.Add(pump);
        Task waitTask;
        try
        {
            waitTask = handle.WaitForOperationExitAsync(exchange.Token) ?? throw new InvalidOperationException();
            pumps.Add(waitTask);
            ownedTasks.Add(waitTask);
        }
        catch
        {
            exchange.Cancel();
            ObserveFaults(pumps);
            throw;
        }

        ObserveFaults(pumps);
        var pending = new List<Task>(pumps);
        try
        {
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThrowIfTimedOut(deadlineToken, operationStarted, timeoutSeconds);
                var remaining = TimeSpan.FromSeconds(timeoutSeconds) - clock.GetElapsedTime(operationStarted);
                if (remaining <= TimeSpan.Zero)
                    throw new CandidateTimedOutException();

                Task completed;
                try
                {
                    completed = await Task.WhenAny(pending)
                        .WaitAsync(remaining, clock, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    throw new CandidateTimedOutException();
                }

                pending.Remove(completed);
                try
                {
                    await completed.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (deadlineToken.IsCancellationRequested)
                {
                    throw new CandidateTimedOutException();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfTimedOut(deadlineToken, operationStarted, timeoutSeconds);
            try
            {
                var responseBytes = await outputTask.ConfigureAwait(false);
                try
                {
                    var processExitCode = handle.ExitCode;
                    return environmentInput
                        ? WorkerContract.ParseCandidateEnvironmentWorkerResponse(responseBytes, expectedCandidate, processExitCode)
                        : WorkerContract.ParseCandidateWorkerResponse(responseBytes, expectedCandidate, processExitCode);
                }
                finally
                {
                    Array.Clear(responseBytes);
                }
            }
            catch (WorkerRefusal refusal)
            {
                throw ToCliRefusal(refusal);
            }
            catch (JsonException)
            {
                throw FixedRefusal("candidate-response-invalid");
            }
        }
        catch
        {
            exchange.Cancel();
            throw;
        }
    }

    private static async Task WriteRequestAndCloseAsync(Stream input, byte[] requestBytes, CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            await input.WriteAsync(requestBytes.AsMemory(), cancellationToken).ConfigureAwait(false);
            await input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await input.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static async Task<byte[]> ReadResponseAsync(Stream output, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var buffer = new byte[16 * 1024];
        using var response = new CandidateRequestBuffer(ResponseMaximumBytes);
        try
        {
            var total = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var readSize = Math.Min(buffer.Length, ResponseMaximumBytes + 1 - total);
                var read = await output.ReadAsync(buffer.AsMemory(0, readSize), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return response.ToArray();
                total += read;
                if (total > ResponseMaximumBytes)
                    throw FixedRefusal("candidate-response-too-large");
                await response.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    private static async Task DrainErrorAsync(Stream error, CancellationToken cancellationToken)
    {
        await Task.Yield();
        var buffer = new byte[16 * 1024];
        try
        {
            var completedReads = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var read = await error.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    return;
                if (++completedReads % 16 == 0)
                    await Task.Yield();
            }
        }
        finally
        {
            Array.Clear(buffer);
        }
    }

    private static byte[] SerializeRequestBytes<TRequest>(TRequest request)
    {
        using var buffer = new CandidateRequestBuffer(RequestMaximumBytes);
        try
        {
            System.Text.Json.JsonSerializer.Serialize(buffer, request, WorkerContract.Json);
            return buffer.ToArray();
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            if (buffer.LimitExceeded)
                throw FixedRefusal("candidate-request-too-large");
            if (failure is WorkerRefusal refusal)
                throw ToCliRefusal(refusal);
            throw FixedRefusal("candidate-request-invalid");
        }
    }

    private static void TryGetStream(Func<Stream> get, ICollection<Stream> streams, Action<Stream> assign,
        ref Exception? firstFailure, ref Exception? fatalFailure)
    {
        try
        {
            var stream = get() ?? throw new InvalidOperationException();
            assign(stream);
            if (!streams.Any(existing => ReferenceEquals(existing, stream)))
                streams.Add(stream);
        }
        catch (Exception failure) when (IsNonFatal(failure))
        {
            firstFailure ??= failure;
        }
        catch (Exception failure) when (!IsNonFatal(failure))
        {
            fatalFailure ??= failure;
        }
    }

    private async Task<CleanupResult> CleanupAsync(ICandidateProcessHandle handle, IReadOnlyCollection<Stream> streams,
        IReadOnlyCollection<Task> exchangeTasks)
    {
        var failed = false;
        ExceptionDispatchInfo? fatalFailure = null;
        var cleanupClock = clock;
        long started = 0;
        CancellationTokenSource? cleanupDeadline = null;
        try
        {
            started = cleanupClock.GetTimestamp();
            cleanupDeadline = new CancellationTokenSource(CleanupTimeout, cleanupClock);
        }
        // Timer providers can throw arbitrary exceptions. Record them and finish every owned cleanup
        // attempt before rethrowing fatal failures; a narrower catch could abandon the child or streams.
        catch (Exception failure)
        {
            RecordCleanupFailure(failure, ref failed, ref fatalFailure);
            cleanupClock = TimeProvider.System;
            try
            {
                started = cleanupClock.GetTimestamp();
                cleanupDeadline = new CancellationTokenSource(CleanupTimeout, cleanupClock);
            }
            catch (Exception fallbackFailure)
            {
                RecordCleanupFailure(fallbackFailure, ref failed, ref fatalFailure);
            }
        }

        TimeSpan Remaining()
        {
            if (cleanupDeadline is null)
                return TimeSpan.Zero;
            try
            {
                return CleanupTimeout - cleanupClock.GetElapsedTime(started);
            }
            catch (Exception failure)
            {
                RecordCleanupFailure(failure, ref failed, ref fatalFailure);
                return TimeSpan.Zero;
            }
        }

        try
        {
            // Payload exit does not release an owned job/group. Always request owned-scope termination.
            handle.KillTree();
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(failure, ref failed, ref fatalFailure);
        }

        try
        {
            var wait = handle.WaitForExitAsync(cleanupDeadline?.Token ?? CancellationToken.None)
                       ?? throw new InvalidOperationException();
            ObserveFaults([wait]);
            var remaining = Remaining();
            if (remaining <= TimeSpan.Zero || cleanupDeadline is null)
                failed = true;
            else
            {
                await wait.WaitAsync(remaining, cleanupClock, cleanupDeadline.Token).ConfigureAwait(false);
            }
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(failure, ref failed, ref fatalFailure);
        }

        try
        {
            if (!handle.HasExited)
                failed = true;
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(failure, ref failed, ref fatalFailure);
        }

        foreach (var stream in streams)
        {
            Task dispose;
            try
            {
                dispose = stream.DisposeAsync().AsTask();
                ObserveFaults([dispose]);
            }
            catch (Exception failure)
            {
                RecordCleanupFailure(failure, ref failed, ref fatalFailure);
                continue;
            }

            var remaining = Remaining();
            if (cleanupDeadline is null || remaining <= TimeSpan.Zero)
            {
                // Always initiate every close attempt, even after the shared deadline expires.
                // Do not await an uncooperative async disposer past the cleanup budget.
                failed = true;
                continue;
            }

            try
            {
                await dispose.WaitAsync(remaining, cleanupClock, cleanupDeadline.Token).ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                RecordCleanupFailure(failure, ref failed, ref fatalFailure);
            }
        }

        try
        {
            handle.Dispose();
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(failure, ref failed, ref fatalFailure);
        }

        if (exchangeTasks.Count > 0)
        {
            ObserveFaults(exchangeTasks);
            var allStopped = Task.WhenAll(exchangeTasks);
            var remaining = Remaining();
            if (!allStopped.IsCompleted && cleanupDeadline is not null && remaining > TimeSpan.Zero)
            {
                try
                {
                    await allStopped.WaitAsync(remaining, cleanupClock, cleanupDeadline.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Pump failures were already mapped by the exchange. Here only unfinished
                    // pumps indicate that owned stream/process cleanup did not quiesce in time.
                }
            }

            if (exchangeTasks.Any(task => !task.IsCompleted))
                failed = true;
            foreach (var task in exchangeTasks.Where(task => task.IsFaulted))
            {
                foreach (var taskFailure in task.Exception!.Flatten().InnerExceptions)
                {
                    if (!IsNonFatal(taskFailure))
                        fatalFailure ??= ExceptionDispatchInfo.Capture(taskFailure);
                }
            }
        }

        try
        {
            cleanupDeadline?.Dispose();
        }
        catch (Exception failure)
        {
            RecordCleanupFailure(failure, ref failed, ref fatalFailure);
        }
        return new CleanupResult(failed, fatalFailure);
    }

    private static void RecordCleanupFailure(Exception failure, ref bool failed, ref ExceptionDispatchInfo? fatalFailure)
    {
        if (IsNonFatal(failure))
            failed = true;
        else
            fatalFailure ??= ExceptionDispatchInfo.Capture(failure);
    }

    private static void ObserveFaults(IEnumerable<Task> tasks)
    {
        foreach (var task in tasks)
            _ = task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
    }

    private void ThrowIfTimedOut(CancellationToken deadlineToken, long started, int timeoutSeconds)
    {
        if (deadlineToken.IsCancellationRequested || clock.GetElapsedTime(started) >= TimeSpan.FromSeconds(timeoutSeconds))
            throw new CandidateTimedOutException();
    }

    private static byte[] SerializeRequest(WorkerRequest request)
    {
        try
        {
            WorkerContract.ValidateCandidateRequest(request);
        }
        catch (WorkerRefusal refusal)
        {
            throw ToCliRefusal(refusal);
        }
        return SerializeRequestBytes(request);
    }

    private static byte[] SerializeEnvironmentRequest(CandidateEnvironmentWorkerRequestV2 request)
    {
        try
        {
            WorkerContract.ValidateCandidateEnvironmentRequest(request);
        }
        catch (WorkerRefusal refusal)
        {
            throw ToCliRefusal(refusal);
        }
        catch (JsonException)
        {
            throw FixedRefusal("candidate-request-invalid");
        }
        return SerializeRequestBytes(request);
    }

    private static CliRefusal ToCliRefusal(WorkerRefusal refusal)
    {
        if (refusal.Code is null || !FixedRefusals.TryGetValue(refusal.Code, out var fixedValue))
            return FixedRefusal("candidate-response-invalid");
        return new CliRefusal(fixedValue.ExitCode, refusal.Code, fixedValue.Message);
    }

    private static CliRefusal ToCliRefusal(CliRefusal refusal)
    {
        if (!FixedRefusals.TryGetValue(refusal.Code, out var fixedValue))
            return FixedRefusal("candidate-inspection-failed");
        return new CliRefusal(fixedValue.ExitCode, refusal.Code, fixedValue.Message);
    }

    private static CliRefusal FixedRefusal(string code)
    {
        var fixedValue = FixedRefusals[code];
        return new CliRefusal(fixedValue.ExitCode, code, fixedValue.Message);
    }

    private static bool IsNonFatal(Exception failure) => failure is not (
        OutOfMemoryException or StackOverflowException or AccessViolationException or AppDomainUnloadedException or
        BadImageFormatException or CannotUnloadAppDomainException or ThreadAbortException);

    private sealed class CandidateTimedOutException : Exception { }

    private readonly record struct CleanupResult(bool Failed, ExceptionDispatchInfo? FatalFailure);

    private sealed class CandidateRequestBuffer(int maximumBytes) : MemoryStream(maximumBytes)
    {
        public bool LimitExceeded { get; private set; }

        public override long Position
        {
            get => base.Position;
            set
            {
                if (value > maximumBytes)
                    ThrowLimitExceeded();
                base.Position = value;
            }
        }

        public override void SetLength(long value)
        {
            if (value > maximumBytes)
                ThrowLimitExceeded();
            base.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            EnsureFits(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            EnsureFits(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            EnsureFits(1);
            base.WriteByte(value);
        }

        private void EnsureFits(int count)
        {
            if (count < 0 || Position > maximumBytes - count)
                ThrowLimitExceeded();
        }

        private void ThrowLimitExceeded()
        {
            LimitExceeded = true;
            throw new CandidateBufferLimitException();
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                if (TryGetBuffer(out var buffer))
                    buffer.AsSpan().Clear();
            }
            finally
            {
                base.Dispose(disposing);
            }
        }
    }

    private sealed class CandidateBufferLimitException : Exception { }

}

/// <summary>The mechanical process operations used by the candidate exchange.</summary>
public interface ICandidateProcessHandle : IDisposable
{
    Stream StandardInput { get; }
    Stream StandardOutput { get; }
    Stream StandardError { get; }
    bool HasExited { get; }
    int ExitCode { get; }
    /// <summary>Waits for payload completion separately from the final owned-scope cleanup wait.</summary>
    Task WaitForOperationExitAsync(CancellationToken cancellationToken) => WaitForExitAsync(cancellationToken);
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void KillTree();
}
