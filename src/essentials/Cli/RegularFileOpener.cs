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
        var descriptor = native.Open(path, OpenNonBlocking | noFollow | OpenCloseOnExec);
        if (descriptor < 0)
            throw new IOException();

        return WrapRegularFile(descriptor, native.FStat, native.Flock, layout, isMac: false);
    }

    private static Stream OpenDarwin(string path)
    {
        var architecture = RuntimeInformation.ProcessArchitecture;
        if (architecture is not (Architecture.X64 or Architecture.Arm64))
            throw new IOException();

        var native = DarwinNative.Value;
        var descriptor = native.Open(path, OpenNonBlockingDarwin | OpenNoFollowDarwin | OpenCloseOnExecDarwin);
        if (descriptor < 0)
            throw new IOException();

        return WrapRegularFile(descriptor, native.FStat, native.Flock, DarwinStatLayout, isMac: true);
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
        var handle = WindowsFunctions.CreateFile(path);
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
    private const int FileTypeMask = 0xF000;
    private const int SharedLock = 1;
    private const int NonBlockingLock = 4;

    private readonly record struct NativeStatLayout(int Size, int ModeOffset, int ModeSize);
    // These layouts were verified against the macOS SDK and compiled glibc/musl headers for Linux x64/arm64.
    // Every other architecture fails closed before interpreting native stat memory.
    private static readonly NativeStatLayout LinuxX64StatLayout = new(144, 24, sizeof(uint));
    private static readonly NativeStatLayout LinuxArm64StatLayout = new(128, 16, sizeof(uint));
    private static readonly NativeStatLayout DarwinStatLayout = new(144, 4, sizeof(ushort));

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OpenDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FStatDelegate(SafeFileHandle handle, IntPtr status);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl, SetLastError = true)]
    private delegate int FlockDelegate(SafeFileHandle handle, int operation);

    private sealed class LinuxFunctions(IntPtr library)
    {
        internal OpenDelegate Open { get; } = Marshal.GetDelegateForFunctionPointer<OpenDelegate>(NativeLibrary.GetExport(library, "open"));
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

        internal static SafeFileHandle CreateFile(string path) => CreateFileNative(
            path, GenericRead, ShareRead, IntPtr.Zero, OpenExisting, OpenReparsePoint, IntPtr.Zero);
    }
}
