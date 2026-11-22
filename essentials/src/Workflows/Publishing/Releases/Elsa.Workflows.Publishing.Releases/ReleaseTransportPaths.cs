using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static Elsa.Workflows.Publishing.Releases.ReleaseManifest;

namespace Elsa.Workflows.Publishing.Releases;

internal static class ReleaseTransportPaths
{
    internal static (string Path, bool Directory) Validate(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith('/') || path.Contains('\\') || path.Contains(':') || path.Any(char.IsControl))
            throw Refuse("unsafe_transport_path");
        var directory = path.EndsWith('/');
        var normalized = directory ? path[..^1] : path;
        var parts = normalized.Split('/');
        if (parts.Any(part => part.Length == 0 || part is "." or ".." || part.EndsWith('.') || part.EndsWith(' '))) throw Refuse("unsafe_transport_path");
        if (directory)
        {
            if (normalized != "blobs" && parts[0] != "attachments") throw Refuse("unlisted_transport_path");
        }
        else if (normalized != "manifest.json")
        {
            if (parts[0] == "blobs" && parts.Length == 2 && parts[1].Length == 69 && parts[1].EndsWith(".json", StringComparison.Ordinal))
                CheckDigest("sha256:" + parts[1][..^5]);
            else if (parts[0] != "attachments" || parts.Length < 2) throw Refuse("unlisted_transport_path");
        }
        return (normalized, directory);
    }
}

/// <summary>Reads a directory without following swapped directory/file links. Unsupported platforms fail closed.</summary>
internal sealed class ReleaseDirectoryReader : IDisposable
{
    private readonly SafeFileHandle _root;
    private readonly DirectoryAbi _abi;
    private ReleaseDirectoryReader(SafeFileHandle root, DirectoryAbi abi) { _root = root; _abi = abi; }

    // Only admitted ABIs are listed. Linux arm64 uses different open flags; macOS x64's plain fstat uses a legacy layout.
    // Linux x64: include/uapi/asm-generic/fcntl.h and arch/x86/include/uapi/asm/stat.h in torvalds/linux.
    // macOS arm64: bsd/sys/fcntl.h and bsd/sys/stat.h in apple-oss-distributions/xnu (native 64-bit inode ABI).
    internal static DirectoryAbi GetAbi(OSPlatform platform, Architecture architecture)
    {
        if (platform == OSPlatform.Linux && architecture == Architecture.X64)
            return new(0x20000, 0x10000, 0x800, 0x80000, 24, 16, 4, 8);
        if (platform == OSPlatform.OSX && architecture == Architecture.Arm64)
            return new(0x100, 0x100000, 4, 0x1000000, 4, 6, 2, 2);
        throw Refuse("directory_platform_unsupported");
    }

    internal readonly record struct DirectoryAbi(int NoFollow, int Directory, int NonBlocking, int CloseOnExec,
        int ModeOffset, int LinksOffset, int ModeBytes, int LinksBytes);

    internal static ReleaseDirectoryReader Open(string path)
    {
        var platform = OperatingSystem.IsMacOS() ? OSPlatform.OSX : OperatingSystem.IsLinux() ? OSPlatform.Linux : default;
        var abi = GetAbi(platform, RuntimeInformation.ProcessArchitecture);
        var descriptor = OpenNative(path, abi.NoFollow | abi.Directory | abi.CloseOnExec);
        if (descriptor < 0) throw Refuse("unsafe_directory_entry");
        return new(new((IntPtr)descriptor, ownsHandle: true), abi);
    }

    internal FileStream OpenRegularFile(string relative)
    {
        var parts = relative.Split('/');
        var current = _root;
        try
        {
            for (var index = 0; index < parts.Length; index++)
            {
                var last = index == parts.Length - 1;
                var descriptor = OpenAt(current.DangerousGetHandle().ToInt32(), parts[index], _abi.NoFollow | _abi.CloseOnExec | (last ? _abi.NonBlocking : _abi.Directory));
                if (descriptor < 0) throw Refuse("unsafe_directory_entry");
                var next = new SafeFileHandle((IntPtr)descriptor, ownsHandle: true);
                if (!ReferenceEquals(current, _root)) current.Dispose();
                current = next;
            }
            EnsureSingleRegularFile(current);
            var stream = new FileStream(current, FileAccess.Read, bufferSize: 4096, isAsync: false);
            current = _root; // ownership transferred to the stream
            return stream;
        }
        finally { if (!ReferenceEquals(current, _root)) current.Dispose(); }
    }

    private void EnsureSingleRegularFile(SafeFileHandle handle)
    {
        // The supported libc ABIs use different stat offsets. Read only mode/nlink, after opening without links.
        var buffer = Marshal.AllocHGlobal(512);
        try
        {
            if (FStat(handle.DangerousGetHandle().ToInt32(), buffer) != 0) throw Refuse("unsafe_directory_entry");
            var mode = _abi.ModeBytes == 2 ? (ushort)Marshal.ReadInt16(buffer, _abi.ModeOffset) : Marshal.ReadInt32(buffer, _abi.ModeOffset);
            var links = _abi.LinksBytes == 2 ? (ushort)Marshal.ReadInt16(buffer, _abi.LinksOffset) : Marshal.ReadInt64(buffer, _abi.LinksOffset);
            if ((mode & 0xf000) != 0x8000 || links != 1) throw Refuse("unsafe_directory_entry");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    public void Dispose() => _root.Dispose();
    [DllImport("libc", EntryPoint = "open", SetLastError = true)]
    private static extern int OpenNative([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "openat", SetLastError = true)]
    private static extern int OpenAt(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags);
    [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
    private static extern int FStat(int descriptor, IntPtr buffer);
}
