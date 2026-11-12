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
    private readonly int _noFollow;
    private readonly int _directory;
    private readonly int _nonBlocking;
    private readonly int _closeOnExec;
    private ReleaseDirectoryReader(SafeFileHandle root, int noFollow, int directory, int nonBlocking, int closeOnExec)
    { _root = root; _noFollow = noFollow; _directory = directory; _nonBlocking = nonBlocking; _closeOnExec = closeOnExec; }

    internal static ReleaseDirectoryReader Open(string path)
    {
        if (!(OperatingSystem.IsMacOS() || OperatingSystem.IsLinux()) ||
            RuntimeInformation.ProcessArchitecture is not (Architecture.X64 or Architecture.Arm64)) throw Refuse("directory_platform_unsupported");
        var noFollow = OperatingSystem.IsMacOS() ? 0x100 : 0x20000;
        var directory = OperatingSystem.IsMacOS() ? 0x100000 : 0x10000;
        var closeOnExec = OperatingSystem.IsMacOS() ? 0x1000000 : 0x80000;
        var descriptor = OpenNative(path, noFollow | directory | closeOnExec);
        if (descriptor < 0) throw Refuse("unsafe_directory_entry");
        return new(new((IntPtr)descriptor, ownsHandle: true), noFollow, directory, OperatingSystem.IsMacOS() ? 4 : 0x800, closeOnExec);
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
                var descriptor = OpenAt(current.DangerousGetHandle().ToInt32(), parts[index], _noFollow | _closeOnExec | (last ? _nonBlocking : _directory));
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

    private static void EnsureSingleRegularFile(SafeFileHandle handle)
    {
        // The supported libc ABIs use different stat offsets. Read only mode/nlink, after opening without links.
        var buffer = Marshal.AllocHGlobal(512);
        try
        {
            if (FStat(handle.DangerousGetHandle().ToInt32(), buffer) != 0) throw Refuse("unsafe_directory_entry");
            var mac = OperatingSystem.IsMacOS();
            var arm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;
            var mode = mac ? (ushort)Marshal.ReadInt16(buffer, 4) : Marshal.ReadInt32(buffer, arm ? 16 : 24);
            var links = mac ? (ushort)Marshal.ReadInt16(buffer, 6) : arm ? Marshal.ReadInt32(buffer, 20) : Marshal.ReadInt64(buffer, 16);
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
