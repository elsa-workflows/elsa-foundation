using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Elsa.Cli;

/// <summary>Acquires a regular-file handle without following a link or blocking on a special file.</summary>
public static class RegularFileOpener
{
    private const int BufferSize = 81920;

    /// <summary>Opens a regular local file and refuses unsupported or nonregular inputs before returning a stream.</summary>
    public static Stream OpenRead(string path)
    {
        try
        {
            using var lease = Capture(path);
            return lease.OpenRead();
        }
        catch (CliRefusal)
        {
            throw;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or AccessViolationException or StackOverflowException or SEHException))
        {
            // Native loader and operating-system details can contain paths or machine-specific data.
            throw CliRefusal.Resolution("composition-input-unreadable", "A supplied composition input could not be read as a regular local file.");
        }
    }

    internal static RegularFileOpenLease Capture(string path)
    {
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var architecture = RuntimeInformation.ProcessArchitecture;
                var layout = architecture switch
                {
                    Architecture.X64 => LinuxX64StatLayout,
                    Architecture.Arm64 => LinuxArm64StatLayout,
                    _ => default
                };
                if (layout.Size == 0)
                    throw new IOException();
                var native = LinuxNative.Value ?? throw new IOException();
                return CaptureUnix(path, native.Open, native.OpenAt, native.FStat, native.Flock, layout,
                    architecture == Architecture.X64 ? 0x20000 : 0x8000, isMac: false);
            }

            if (OperatingSystem.IsMacOS())
            {
                var architecture = RuntimeInformation.ProcessArchitecture;
                if (architecture is not (Architecture.X64 or Architecture.Arm64))
                    throw new IOException();
                var native = DarwinNative.Value;
                return CaptureUnix(path, native.Open, native.OpenAt, native.FStat, native.Flock, DarwinStatLayout,
                    OpenNoFollowDarwin, isMac: true);
            }

            if (OperatingSystem.IsWindows())
                return WindowsFunctions.Capture(path);

            throw new IOException();
        }
        catch (CliRefusal)
        {
            throw;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or AccessViolationException or StackOverflowException or SEHException))
        {
            throw CliRefusal.Resolution("composition-input-unreadable", "A supplied composition input could not be read as a regular local file.");
        }
    }

    private static RegularFileOpenLease CaptureUnix(string path, OpenDelegate open, OpenAtDelegate openAt,
        FStatDelegate fstat, FlockDelegate flock, NativeStatLayout layout, int noFollow, bool isMac)
    {
        var fullPath = NormalizeUnixPath(path);
        var components = fullPath.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (components.Length == 0)
            throw new IOException();

        var closeOnExec = OpenCloseOnExecFor(isMac);
        var rootDescriptor = open("/", closeOnExec);
        if (rootDescriptor < 0)
            throw new IOException();

        var root = new SafeFileHandle(new IntPtr(rootDescriptor), ownsHandle: true);
        var directories = new List<SafeFileHandle> { root };
        try
        {
            for (var index = 0; index < components.Length - 1; index++)
            {
                var descriptor = openAt(GetFileDescriptor(directories[^1]), components[index],
                    OpenNonBlockingFor(isMac) | noFollow | closeOnExec);
                if (descriptor < 0)
                    throw new IOException();

                SafeFileHandle? next = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
                try
                {
                    if (!HasMode(next, fstat, layout, DirectoryFileMode))
                        throw new IOException();

                    directories.Add(next);
                    next = null;
                }
                finally
                {
                    next?.Dispose();
                }
            }

            return new RegularFileOpenLease(
                () =>
                {
                    var descriptor = openAt(GetFileDescriptor(directories[^1]), components[^1],
                        OpenNonBlockingFor(isMac) | noFollow | closeOnExec);
                    if (descriptor < 0)
                        throw new IOException();
                    return WrapRegularFile(descriptor, fstat, flock, layout, isMac);
                },
                () => DisposeHandles(directories));
        }
        catch
        {
            DisposeHandles(directories);
            throw;
        }
    }

    private static void DisposeHandles(IEnumerable<SafeFileHandle> handles)
    {
        foreach (var handle in handles.Reverse())
        {
            handle.Dispose();
        }
    }

    private static int GetFileDescriptor(SafeFileHandle handle) => handle.DangerousGetHandle().ToInt32();

    private static bool HasMode(SafeFileHandle handle, FStatDelegate fstat, NativeStatLayout layout, int expectedMode)
    {
        var status = Marshal.AllocHGlobal(layout.Size);
        try
        {
            return fstat(handle, status) == 0 && (ReadMode(status, layout) & FileTypeMask) == expectedMode;
        }
        finally
        {
            Marshal.FreeHGlobal(status);
        }
    }

    private static string NormalizeUnixPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!OperatingSystem.IsMacOS())
            return fullPath;

        // macOS exposes these stable system aliases as symlinks. Resolve only these
        // aliases so ordinary temporary paths remain usable while user-controlled
        // parent symlinks are refused by the descriptor walk above.
        foreach (var (alias, target) in new[]
                 {
                     ("/tmp", "/private/tmp"),
                     ("/var", "/private/var"),
                     ("/etc", "/private/etc")
                 })
        {
            if (fullPath.Equals(alias, StringComparison.Ordinal) || fullPath.StartsWith(alias + "/", StringComparison.Ordinal))
                return target + fullPath[alias.Length..];
        }

        return fullPath;
    }

    private static Stream WrapRegularFile(int descriptor, FStatDelegate fstat, FlockDelegate flock, NativeStatLayout layout, bool isMac)
    {
        var handle = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
        try
        {
            var status = Marshal.AllocHGlobal(layout.Size);
            try
            {
                if (fstat(handle, status) != 0 || (ReadMode(status, layout) & FileTypeMask) != RegularFileMode)
                    throw new IOException();
            }
            finally
            {
                Marshal.FreeHGlobal(status);
            }

            LockForSharedReadIfSupported(handle, flock, isMac);
            return new FileStream(handle, FileAccess.Read, bufferSize: BufferSize, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private static int ReadMode(IntPtr status, NativeStatLayout layout) =>
        layout.ModeSize == sizeof(ushort) ? Marshal.ReadInt16(status, layout.ModeOffset) & 0xffff : Marshal.ReadInt32(status, layout.ModeOffset);

    private static void LockForSharedReadIfSupported(SafeFileHandle handle, FlockDelegate flock, bool isMac)
    {
        var result = flock(handle, SharedLock | NonBlockingLock);
        var error = Marshal.GetLastPInvokeError();
        var wouldBlock = isMac ? 35 : 11;
        if (result != 0 && error == wouldBlock)
            throw new IOException();
        // Match FileStream's best-effort shared lock: unsupported filesystems do not reject regular files.
    }

    private static Stream OpenWindowsHandle(SafeFileHandle handle)
    {
        try
        {
            if (handle.IsInvalid || WindowsFunctions.GetFileType(handle) != WindowsFunctions.DiskFileType)
                throw new IOException();

            var attributes = File.GetAttributes(handle);
            if ((attributes & (FileAttributes.Directory | FileAttributes.ReparsePoint | FileAttributes.Device)) != 0)
                throw new IOException();

            return new FileStream(handle, FileAccess.Read, bufferSize: BufferSize, isAsync: false);
        }
        catch
        {
            handle.Dispose();
            throw;
        }
    }

    private const int OpenNonBlocking = 0x800;
    private const int OpenCloseOnExec = 0x80000;
    private const int OpenNonBlockingDarwin = 0x4;
    private const int OpenNoFollowDarwin = 0x100;
    private const int OpenCloseOnExecDarwin = 0x1000000;
    private const int RegularFileMode = 0x8000;
    private const int DirectoryFileMode = 0x4000;
    private const int FileTypeMask = 0xF000;
    private const int SharedLock = 1;
    private const int NonBlockingLock = 4;

    private static int OpenNonBlockingFor(bool isMac) => isMac ? OpenNonBlockingDarwin : OpenNonBlocking;

    private static int OpenCloseOnExecFor(bool isMac) => isMac ? OpenCloseOnExecDarwin : OpenCloseOnExec;

    internal readonly record struct NativeStatLayout(int Size, int ModeOffset, int ModeSize);
    // These layouts were verified against the macOS SDK and compiled glibc/musl headers for Linux x64/arm64.
    // Every other architecture fails closed before interpreting native stat memory.
    private static readonly NativeStatLayout LinuxX64StatLayout = new(144, 24, sizeof(uint));
    private static readonly NativeStatLayout LinuxArm64StatLayout = new(128, 16, sizeof(uint));
    private static readonly NativeStatLayout DarwinStatLayout = new(144, 4, sizeof(ushort));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int OpenDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int OpenAtDelegate(int directoryDescriptor, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int FStatDelegate(SafeFileHandle handle, IntPtr status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    internal delegate int FlockDelegate(SafeFileHandle handle, int operation);

    private sealed class LinuxFunctions(IntPtr library)
    {
        internal OpenDelegate Open { get; } = Marshal.GetDelegateForFunctionPointer<OpenDelegate>(NativeLibrary.GetExport(library, "open"));
        internal OpenAtDelegate OpenAt { get; } = Marshal.GetDelegateForFunctionPointer<OpenAtDelegate>(NativeLibrary.GetExport(library, "openat"));
        internal FStatDelegate FStat { get; } = Marshal.GetDelegateForFunctionPointer<FStatDelegate>(NativeLibrary.GetExport(library, "fstat"));
        internal FlockDelegate Flock { get; } = Marshal.GetDelegateForFunctionPointer<FlockDelegate>(NativeLibrary.GetExport(library, "flock"));

        internal static LinuxFunctions? Load()
        {
            if (NativeLibrary.TryLoad("libc.so.6", out var glibc))
                return Bind(glibc);

            var muslPath = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "/lib/ld-musl-x86_64.so.1",
                Architecture.Arm64 => "/lib/ld-musl-aarch64.so.1",
                _ => string.Empty
            };
            if (string.IsNullOrEmpty(muslPath) || !NativeLibrary.TryLoad(muslPath, out var musl))
                return null;
            return Bind(musl);
        }

        private static LinuxFunctions Bind(IntPtr library)
        {
            try
            {
                return new LinuxFunctions(library);
            }
            catch
            {
                NativeLibrary.Free(library);
                throw;
            }
        }

    }

    private sealed class DarwinFunctions(IntPtr library, Architecture architecture)
    {
        internal OpenDelegate Open { get; } = Marshal.GetDelegateForFunctionPointer<OpenDelegate>(NativeLibrary.GetExport(library, "open"));
        internal OpenAtDelegate OpenAt { get; } = Marshal.GetDelegateForFunctionPointer<OpenAtDelegate>(NativeLibrary.GetExport(library, "openat"));
        internal FStatDelegate FStat { get; } = Marshal.GetDelegateForFunctionPointer<FStatDelegate>(NativeLibrary.GetExport(library,
            architecture == Architecture.X64 ? "fstat$INODE64" : "fstat"));
        internal FlockDelegate Flock { get; } = Marshal.GetDelegateForFunctionPointer<FlockDelegate>(NativeLibrary.GetExport(library, "flock"));

        internal static DarwinFunctions Load()
        {
            if (!NativeLibrary.TryLoad("libSystem.B.dylib", out var library))
                throw new DllNotFoundException();
            try
            {
                return new DarwinFunctions(library, RuntimeInformation.ProcessArchitecture);
            }
            catch
            {
                NativeLibrary.Free(library);
                throw;
            }
        }

    }

    private static readonly Lazy<LinuxFunctions?> LinuxNative = new(LinuxFunctions.Load);
    private static readonly Lazy<DarwinFunctions> DarwinNative = new(DarwinFunctions.Load);

    private static class WindowsFunctions
    {
        internal const uint DiskFileType = 1;
        private const uint GenericRead = 0x80000000;
        private const uint ShareRead = 0x1;
        private const uint OpenExisting = 3;
        private const uint OpenReparsePoint = 0x00200000;
        private const uint BackupSemantics = 0x02000000;
        private const uint ShareWrite = 0x2;

        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileNative(
            string path,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "GetFileType", SetLastError = true)]
        internal static extern uint GetFileType(SafeFileHandle handle);

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandleNative(SafeFileHandle handle, [Out] System.Text.StringBuilder filePath,
            uint filePathLength, uint flags);

        internal static SafeFileHandle CreateFile(string path) => CreateFileNative(
            path, GenericRead, ShareRead, IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);

        internal static RegularFileOpenLease Capture(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var parentPath = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(fileName))
                throw new IOException();

            // Acquire ancestors from the root down and retain every handle until the
            // final file is acquired. Each handle excludes delete sharing, so a host
            // cannot exchange a checked ancestor while a deeper path is opened.
            var directories = new List<SafeFileHandle>();
            var ancestorPaths = new List<string>();
            for (var directory = new DirectoryInfo(parentPath); directory is not null; directory = directory.Parent)
                ancestorPaths.Add(directory.FullName);
            ancestorPaths.Reverse();

            try
            {
                foreach (var ancestorPath in ancestorPaths)
                {
                    var handle = CreateDirectory(ancestorPath);
                    var retained = false;
                    try
                    {
                        if (handle.IsInvalid || GetFileType(handle) != DiskFileType ||
                            (File.GetAttributes(handle) & FileAttributes.ReparsePoint) != 0)
                            throw new IOException();
                        directories.Add(handle);
                        retained = true;
                    }
                    finally
                    {
                        if (!retained)
                            handle.Dispose();
                    }
                }

                return new RegularFileOpenLease(
                    () =>
                    {
                        var anchoredPath = Path.Join(GetFinalPath(directories[^1]), fileName);
                        return OpenWindowsHandle(CreateFile(anchoredPath));
                    },
                    () => DisposeHandles(directories));
            }
            catch
            {
                DisposeHandles(directories);
                throw;
            }
        }

        private static SafeFileHandle CreateDirectory(string path) => CreateFileNative(
            path, 0, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting,
            OpenReparsePoint | BackupSemantics, IntPtr.Zero);

        internal static string GetFinalPath(SafeFileHandle handle)
        {
            var capacity = 512;
            var path = new System.Text.StringBuilder(capacity);
            while (capacity <= 32768)
            {
                path.Clear();
                path.Capacity = capacity;
                var length = GetFinalPathNameByHandleNative(handle, path, (uint)path.Capacity, 0);
                if (length == 0)
                    throw new IOException();
                if (length < path.Capacity)
                    return path.ToString();
                capacity = checked((int)length + 1);
            }

            throw new IOException();
        }
    }
}
