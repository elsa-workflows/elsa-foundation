using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Elsa.Cli.Worker;
using Microsoft.Win32.SafeHandles;

namespace Elsa.Cli;

/// <summary>Keeps a candidate supervisor and its OS termination scope until frontend cleanup.</summary>
public sealed class CandidateProcessHandle : ICandidateProcessHandle
{
    private readonly ICandidateProcess process;
    private readonly ICandidateProcessControl control;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ICandidateProcessScope? scope;
    private readonly ICandidateProcessGroup group;
    private readonly Task authorize;
    private readonly Task<int> operationExit;
    private readonly object ownershipSync = new();
    private int groupReady;
    private bool scopeAssigned;
    private bool terminating;
    private bool terminationRequested;
    private int disposed;

    /// <summary>
    /// Creates a candidate handle over explicit process, control-channel, and ownership seams.
    /// </summary>
    public CandidateProcessHandle(ICandidateProcess process, ICandidateProcessControl control, Guid correlation,
        ICandidateProcessScope? scope = null, ICandidateProcessGroup? group = null)
    {
        this.process = process ?? throw new ArgumentNullException(nameof(process));
        this.control = control ?? throw new ArgumentNullException(nameof(control));
        this.scope = scope;
        this.group = group ?? new CandidateNativeProcessGroup();
        authorize = AuthorizeAsync(correlation);
        operationExit = ReadOperationExitAsync();
        // Observe both tasks even if startup or a stream getter fails before exchange takes ownership.
        Observe(authorize);
        Observe(operationExit);
    }

    public static ICandidateProcessHandle Start(ProcessStartInfo startInfo)
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException();

        var correlation = Guid.NewGuid();
        // Keep the Unix socket pathname within macOS's limit even under its longer per-user temp path.
        var pipeName = "ec" + correlation.ToString("N");
        var control = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        WindowsJob? scope = null;
        Process? process = null;
        try
        {
            scope = OperatingSystem.IsWindows() ? WindowsJob.Create() : null;
            var payloadAssembly = startInfo.ArgumentList[5];
            startInfo.ArgumentList[5] = Path.Join(AppContext.BaseDirectory, WorkerProcess.WorkerAssemblyFileName);
            // Only transport identity and the already-required loader paths are added to arguments.
            startInfo.ArgumentList.Add("--candidate-owner");
            startInfo.ArgumentList.Add(pipeName);
            startInfo.ArgumentList.Add(correlation.ToString("N"));
            startInfo.ArgumentList.Add(startInfo.ArgumentList[2]);
            startInfo.ArgumentList.Add(startInfo.ArgumentList[4]);
            startInfo.ArgumentList.Add(payloadAssembly);
            process = Process.Start(startInfo) ?? throw new InvalidOperationException();
            return new CandidateProcessHandle(new ProcessAdapter(process), new NamedPipeControl(control), correlation, scope);
        }
        catch
        {
            // No GO has been sent: the supervisor cannot yet have spawned the payload.
            try { if (process is not null && !process.HasExited) process.Kill(); }
            finally
            {
                process?.Dispose();
                scope?.Dispose();
                control.Dispose();
            }
            throw;
        }
    }

    public Stream StandardInput => new AuthorizedInput(process.StandardInput, authorize);
    public Stream StandardOutput => process.StandardOutput;
    public Stream StandardError => process.StandardError;
    public bool HasExited => process.HasExited;
    public int ExitCode => operationExit.GetAwaiter().GetResult();
    public Task WaitForOperationExitAsync(CancellationToken cancellationToken) => operationExit.WaitAsync(cancellationToken);

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        // OS termination requests are asynchronous. Supervisor exit does not establish payload/descendant
        // completion: verify the existing job/group under the caller's same bounded cleanup deadline.
        while (scope is not null && scope.ActiveProcesses != 0)
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        if (groupReady != 0)
            await group.WaitForExitAsync(process.Id, cancellationToken).ConfigureAwait(false);
    }

    public void KillTree()
    {
        lock (ownershipSync)
        {
            // Serialize termination with authorization: cleanup before READY must prevent a late GO.
            terminating = true;
            if (terminationRequested)
                return;
            if (scope is not null && scopeAssigned)
                scope.Terminate();
            else if (groupReady != 0)
            {
                // Unexpected anchor loss is not successful cleanup; never target a possibly reused PGID.
                if (process.HasExited)
                    throw new InvalidOperationException();
                // The retained live supervisor anchors this PGID. No exited-PID tree scan is used.
                group.Terminate(process.Id);
            }
            else if (!process.HasExited)
                process.Kill(); // Before READY/GO there is no payload or descendant.
            terminationRequested = true;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        try { lifetime.Cancel(); }
        finally
        {
            try { control.Dispose(); }
            finally
            {
                try { scope?.Dispose(); }
                finally
                {
                    try { process.Dispose(); }
                    finally { lifetime.Dispose(); }
                }
            }
        }
    }

    private async Task AuthorizeAsync(Guid correlation)
    {
        await control.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
        var ready = new byte[CandidateProcessOwner.ReadyFrameLength];
        await control.ReadExactlyAsync(ready, lifetime.Token).ConfigureAwait(false);
        if (ready[0] != CandidateProcessOwner.Ready || new Guid(ready.AsSpan(1)) != correlation)
            throw new InvalidOperationException();

        Task goWrite;
        lock (ownershipSync)
        {
            if (terminating)
                throw new OperationCanceledException();
            if (scope is not null)
            {
                scope.Assign(process);
                scopeAssigned = true;
            }
            else
                groupReady = 1;
            // Linearize GO with cleanup by starting the write while the ownership state is locked.
            // Await outside the lock so a stalled pipe cannot hold up the bounded cleanup path.
            goWrite = control.WriteAsync(new byte[] { CandidateProcessOwner.Go }, lifetime.Token).AsTask();
        }
        await goWrite.ConfigureAwait(false);
        await control.FlushAsync(lifetime.Token).ConfigureAwait(false);
    }

    private async Task<int> ReadOperationExitAsync()
    {
        await authorize.ConfigureAwait(false);
        var status = new byte[CandidateProcessOwner.StatusFrameLength];
        await control.ReadExactlyAsync(status, lifetime.Token).ConfigureAwait(false);
        if (status[0] != CandidateProcessOwner.Status)
            throw new InvalidOperationException();
        var exitCode = BinaryPrimitives.ReadInt32LittleEndian(status.AsSpan(1));
        // STATUS follows payload exit and flushed forwarding. Terminate the retained scope now so every
        // native/runtime duplicate of the output pipes closes; final cleanup still awaits actual exit.
        KillTree();
        return exitCode;
    }

    private static void Observe(Task task) => _ = task.ContinueWith(fault => _ = fault.Exception,
        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private sealed class AuthorizedInput(Stream inner, Task authorize) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await authorize.WaitAsync(cancellationToken).ConfigureAwait(false);
            await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override ValueTask DisposeAsync() => inner.DisposeAsync();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override void Flush() => inner.Flush();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }

    private sealed class WindowsJob : SafeHandleZeroOrMinusOneIsInvalid, ICandidateProcessScope
    {
        public WindowsJob() : base(true) { }

        public static WindowsJob Create()
        {
            var job = CreateJobObject(IntPtr.Zero, null);
            if (job.IsInvalid) { job.Dispose(); throw new InvalidOperationException(); }
            var limits = new ExtendedLimits { Basic = new BasicLimits { Flags = 0x2000 } };
            if (!SetInformationJobObject(job, 9, ref limits, (uint)Marshal.SizeOf<ExtendedLimits>()))
            { job.Dispose(); throw new InvalidOperationException(); }
            return job;
        }

        public void Assign(ICandidateProcess process)
        {
            if (process is not ProcessAdapter adapter || !AssignProcessToJobObject(this, adapter.Process.SafeHandle))
                throw new InvalidOperationException();
        }

        public void Terminate()
        {
            if (!TerminateJobObject(this, 1)) throw new InvalidOperationException();
        }

        public int ActiveProcesses
        {
            get
            {
                if (!QueryInformationJobObject(this, 1, out Accounting info, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero))
                    throw new InvalidOperationException();
                return checked((int)info.ActiveProcesses);
            }
        }

        protected override bool ReleaseHandle() => CloseHandle(handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits
        {
            public long ProcessUserTime, JobUserTime;
            public uint Flags;
            public UIntPtr MinimumWorkingSet, MaximumWorkingSet;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits
        {
            public BasicLimits Basic;
            public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Accounting
        {
            public long TotalUserTime, TotalKernelTime, PeriodUserTime, PeriodKernelTime;
            public uint PageFaults, TotalProcesses, ActiveProcesses, TerminatedProcesses;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern WindowsJob CreateJobObject(IntPtr attributes, string? name);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(WindowsJob job, int kind, ref ExtendedLimits information, uint length);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(WindowsJob job, SafeProcessHandle process);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool TerminateJobObject(WindowsJob job, uint exitCode);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool QueryInformationJobObject(WindowsJob job, int kind, out Accounting information, uint length, IntPtr returnLength);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    private sealed class ProcessAdapter(Process process) : ICandidateProcess
    {
        public Process Process { get; } = process;
        public Stream StandardInput => Process.StandardInput.BaseStream;
        public Stream StandardOutput => Process.StandardOutput.BaseStream;
        public Stream StandardError => Process.StandardError.BaseStream;
        public int Id => Process.Id;
        public bool HasExited => Process.HasExited;
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Process.WaitForExitAsync(cancellationToken);
        public void Kill() => Process.Kill();
        public void Dispose() => Process.Dispose();
    }

    private sealed class NamedPipeControl(NamedPipeServerStream pipe) : ICandidateProcessControl
    {
        public Task WaitForConnectionAsync(CancellationToken cancellationToken) => pipe.WaitForConnectionAsync(cancellationToken);
        public Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken) => pipe.ReadExactlyAsync(buffer, cancellationToken).AsTask();
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken) => pipe.WriteAsync(buffer, cancellationToken);
        public Task FlushAsync(CancellationToken cancellationToken) => pipe.FlushAsync(cancellationToken);
        public void Dispose() => pipe.Dispose();
    }
}

/// <summary>Forwards Unix process-group termination and observation to the native/runtime adapters.</summary>
public sealed class CandidateNativeProcessGroup : ICandidateProcessGroup
{
    private readonly Func<int, int, int> kill;
    private readonly CandidateUnixProcessGroup observer;

    /// <summary>Creates the native group adapter; delegates are injectable for deterministic failure tests.</summary>
    public CandidateNativeProcessGroup(Func<int, int, int>? kill = null, CandidateUnixProcessGroup? observer = null)
    {
        this.kill = kill ?? Kill;
        this.observer = observer ?? new CandidateUnixProcessGroup();
    }

    public void Terminate(int processId)
    {
        if (kill(-processId, 9) != 0)
            throw new InvalidOperationException();
    }

    public Task WaitForExitAsync(int processGroupId, CancellationToken cancellationToken) =>
        observer.WaitForExitAsync(processGroupId, cancellationToken);

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);
}

/// <summary>The process operations owned by a candidate process handle.</summary>
public interface ICandidateProcess : IDisposable
{
    Stream StandardInput { get; }
    Stream StandardOutput { get; }
    Stream StandardError { get; }
    int Id { get; }
    bool HasExited { get; }
    Task WaitForExitAsync(CancellationToken cancellationToken);
    void Kill();
}

/// <summary>The private control channel used to authorize a candidate supervisor.</summary>
public interface ICandidateProcessControl : IDisposable
{
    Task WaitForConnectionAsync(CancellationToken cancellationToken);
    Task ReadExactlyAsync(Memory<byte> buffer, CancellationToken cancellationToken);
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken);
    Task FlushAsync(CancellationToken cancellationToken);
}

/// <summary>The platform process scope that owns a candidate supervisor and its descendants.</summary>
public interface ICandidateProcessScope : IDisposable
{
    void Assign(ICandidateProcess process);
    void Terminate();
    int ActiveProcesses { get; }
}

/// <summary>The Unix process-group operations used after supervisor authorization.</summary>
public interface ICandidateProcessGroup
{
    void Terminate(int processId);
    Task WaitForExitAsync(int processGroupId, CancellationToken cancellationToken);
}
