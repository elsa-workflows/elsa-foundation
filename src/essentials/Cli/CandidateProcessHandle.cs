using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Elsa.Cli.Worker;
using Microsoft.Win32.SafeHandles;

namespace Elsa.Cli;

/// <summary>Keeps a candidate supervisor and its OS termination scope until frontend cleanup.</summary>
internal sealed class CandidateProcessHandle : ICandidateProcessHandle
{
    private readonly Process process;
    private readonly NamedPipeServerStream control;
    private readonly CancellationTokenSource lifetime = new();
    private readonly WindowsJob? job;
    private readonly Task authorize;
    private readonly Task<int> operationExit;
    private readonly object ownershipSync = new();
    private int groupReady;
    private bool jobAssigned;
    private bool terminating;
    private bool terminationRequested;

    private CandidateProcessHandle(Process process, NamedPipeServerStream control, WindowsJob? job, Guid correlation)
    {
        this.process = process;
        this.control = control;
        this.job = job;
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
        WindowsJob? job = null;
        Process? process = null;
        try
        {
            job = OperatingSystem.IsWindows() ? WindowsJob.Create() : null;
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
            return new CandidateProcessHandle(process, control, job, correlation);
        }
        catch
        {
            // No GO has been sent: the supervisor cannot yet have spawned the payload.
            try { if (process is not null && !process.HasExited) process.Kill(); }
            finally
            {
                process?.Dispose();
                job?.Dispose();
                control.Dispose();
            }
            throw;
        }
    }

    public Stream StandardInput => new AuthorizedInput(process.StandardInput.BaseStream, authorize);
    public Stream StandardOutput => process.StandardOutput.BaseStream;
    public Stream StandardError => process.StandardError.BaseStream;
    public bool HasExited => process.HasExited;
    public int ExitCode => operationExit.GetAwaiter().GetResult();
    public Task WaitForOperationExitAsync(CancellationToken cancellationToken) => operationExit.WaitAsync(cancellationToken);

    public async Task WaitForExitAsync(CancellationToken cancellationToken)
    {
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        // OS termination requests are asynchronous. Supervisor exit does not establish payload/descendant
        // completion: verify the existing job/group under the caller's same bounded cleanup deadline.
        while (job is not null && job.ActiveProcesses != 0)
            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        if (groupReady != 0)
            await new CandidateUnixProcessGroup().WaitForExitAsync(process.Id, cancellationToken).ConfigureAwait(false);
    }

    public void KillTree()
    {
        lock (ownershipSync)
        {
            // Serialize termination with authorization: cleanup before READY must prevent a late GO.
            terminating = true;
            if (terminationRequested)
                return;
            if (job is not null && jobAssigned)
                job.Terminate();
            else if (groupReady != 0)
            {
                // Unexpected anchor loss is not successful cleanup; never target a possibly reused PGID.
                if (process.HasExited)
                    throw new InvalidOperationException();
                // The retained live supervisor anchors this PGID. No exited-PID tree scan is used.
                if (Kill(-process.Id, 9) != 0)
                    throw new InvalidOperationException();
            }
            else if (!process.HasExited)
                process.Kill(); // Before READY/GO there is no payload or descendant.
            terminationRequested = true;
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        try { control.Dispose(); }
        finally
        {
            try { job?.Dispose(); }
            finally { process.Dispose(); lifetime.Dispose(); }
        }
    }

    private async Task AuthorizeAsync(Guid correlation)
    {
        await control.WaitForConnectionAsync(lifetime.Token).ConfigureAwait(false);
        var ready = new byte[CandidateProcessOwner.ReadyFrameLength];
        await control.ReadExactlyAsync(ready, lifetime.Token).ConfigureAwait(false);
        if (ready[0] != CandidateProcessOwner.Ready || new Guid(ready.AsSpan(1)) != correlation)
            throw new InvalidOperationException();

        lock (ownershipSync)
        {
            if (terminating)
                throw new OperationCanceledException();
            if (job is not null)
            {
                job.Assign(process);
                jobAssigned = true;
            }
            else
                groupReady = 1;
        }
        await control.WriteAsync(new byte[] { CandidateProcessOwner.Go }, lifetime.Token).ConfigureAwait(false);
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

    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Kill(int processId, int signal);

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

    private sealed class WindowsJob : SafeHandleZeroOrMinusOneIsInvalid
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

        public void Assign(Process process)
        {
            if (!AssignProcessToJobObject(this, process.SafeHandle)) throw new InvalidOperationException();
        }

        public void Terminate()
        {
            if (!TerminateJobObject(this, 1)) throw new InvalidOperationException();
        }

        public uint ActiveProcesses
        {
            get
            {
                if (!QueryInformationJobObject(this, 1, out Accounting info, (uint)Marshal.SizeOf<Accounting>(), IntPtr.Zero))
                    throw new InvalidOperationException();
                return info.ActiveProcesses;
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
}
