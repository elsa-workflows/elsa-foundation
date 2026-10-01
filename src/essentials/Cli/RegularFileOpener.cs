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
            if (OperatingSystem.IsLinux())
                return OpenLinux(path);
            if (OperatingSystem.IsMacOS())
                return OpenDarwin(path);
            if (OperatingSystem.IsWindows())
                return OpenWindows(path);

            throw new IOException();
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

    private static Stream OpenLinux(string path)
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
        var noFollow = architecture == Architecture.X64 ? 0x20000 : 0x8000;
        var native = LinuxNative.Value ?? throw new IOException();
        using var anchor = CaptureUnix(path, native.Open, native.OpenAt, native.FStat, native.Flock, layout,
            noFollow, isMac: false);
        return anchor.OpenRead();
    }

    private static Stream OpenDarwin(string path)
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (architecture is not (Architecture.X64 or Architecture.Arm64))
            throw new IOException();

        var native = DarwinNative.Value;
        using var anchor = CaptureUnix(path, native.Open, native.OpenAt, native.FStat, native.Flock, DarwinStatLayout,
            OpenNoFollowDarwin, isMac: true);
        return anchor.OpenRead();
    }

    internal static RegularFileAnchor Capture(string path)
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

    private static RegularFileAnchor CaptureUnix(string path, OpenDelegate open, OpenAtDelegate openAt,
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
        SafeFileHandle current = root;
        try
        {
            for (var index = 0; index < components.Length - 1; index++)
            {
                var descriptor = openAt(GetFileDescriptor(current), components[index],
                    OpenNonBlockingFor(isMac) | noFollow | closeOnExec);
                if (descriptor < 0)
                    throw new IOException();

                SafeFileHandle? next = new SafeFileHandle(new IntPtr(descriptor), ownsHandle: true);
                try
                {
                    if (!HasMode(next, fstat, layout, DirectoryFileMode))
                        throw new IOException();

                    if (ReferenceEquals(current, root))
                        root.Dispose();
                    else
                        current.Dispose();
                    current = next;
                    next = null;
                }
                finally
                {
                    next?.Dispose();
                }
            }

            return RegularFileAnchor.Unix(current, components[^1], openAt, fstat, flock, layout, noFollow, isMac);
        }
        catch
        {
            if (!ReferenceEquals(current, root))
                current.Dispose();
            root.Dispose();
            throw;
        }
    }

    internal sealed class RegularFileAnchor : IDisposable
    {
        private readonly SafeFileHandle _parent;
        private readonly string _fileName;
        private readonly OpenAtDelegate? _openAt;
        private readonly FStatDelegate? _fstat;
        private readonly FlockDelegate? _flock;
        private readonly NativeStatLayout _layout;
        private readonly int _noFollow;
        private readonly bool _isMac;

        private RegularFileAnchor(SafeFileHandle parent, string fileName, OpenAtDelegate openAt,
            FStatDelegate fstat, FlockDelegate flock, NativeStatLayout layout, int noFollow, bool isMac)
        {
            _parent = parent;
            _fileName = fileName;
            _openAt = openAt;
            _fstat = fstat;
            _flock = flock;
            _layout = layout;
            _noFollow = noFollow;
            _isMac = isMac;
        }

        private RegularFileAnchor(SafeFileHandle parent, string fileName)
        {
            _parent = parent;
            _fileName = fileName;
        }

        internal static RegularFileAnchor Unix(SafeFileHandle parent, string fileName, OpenAtDelegate openAt,
            FStatDelegate fstat, FlockDelegate flock, NativeStatLayout layout, int noFollow, bool isMac) =>
            new(parent, fileName, openAt, fstat, flock, layout, noFollow, isMac);

        internal static RegularFileAnchor Windows(SafeFileHandle parent, string fileName) => new(parent, fileName);

        internal Stream OpenRead()
        {
            if (_openAt is not null)
            {
                var descriptor = _openAt(GetFileDescriptor(_parent), _fileName, OpenNonBlockingFor(_isMac) |
                    _noFollow | OpenCloseOnExecFor(_isMac));
                if (descriptor < 0)
                    throw new IOException();
                return WrapRegularFile(descriptor, _fstat!, _flock!, _layout, _isMac);
            }

            var anchoredPath = Path.Join(WindowsFunctions.GetFinalPath(_parent), _fileName);
            return OpenWindowsHandle(WindowsFunctions.CreateFile(anchoredPath));
        }

        public void Dispose() => _parent.Dispose();
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

    private static Stream OpenWindows(string path)
    {
        using var anchor = WindowsFunctions.Capture(path);
        return anchor.OpenRead();
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
        private const uint ShareDelete = 0x4;

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

        internal static RegularFileAnchor Capture(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var parentPath = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(parentPath) || string.IsNullOrEmpty(fileName))
                throw new IOException();

            // Inspect every lexical ancestor before taking the deepest handle. This
            // rejects junctions/symlinks in the path, including an exchanged host
            // directory, before the final file handle is acquired.
            for (var directory = new DirectoryInfo(parentPath); directory is not null; directory = directory.Parent)
            {
                using var handle = CreateDirectory(directory.FullName);
                if (handle.IsInvalid || GetFileType(handle) != DiskFileType ||
                    (File.GetAttributes(handle) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException();
            }

            var parent = CreateDirectory(parentPath);
            try
            {
                if (parent.IsInvalid || GetFileType(parent) != DiskFileType ||
                    (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException();
            }
            catch
            {
                parent.Dispose();
                throw;
            }

            return RegularFileAnchor.Windows(parent, fileName);
        }

        private static SafeFileHandle CreateDirectory(string path) => CreateFileNative(
            path, 0, ShareRead | ShareWrite, IntPtr.Zero, OpenExisting,
            OpenReparsePoint | BackupSemantics, IntPtr.Zero);

        internal static string GetFinalPath(SafeFileHandle handle)
        {
            var capacity = 512;
            while (capacity <= 32768)
            {
                var path = new System.Text.StringBuilder(capacity);
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
