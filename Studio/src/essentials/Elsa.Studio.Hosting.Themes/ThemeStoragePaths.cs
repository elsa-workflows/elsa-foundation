using System.Runtime.InteropServices;

namespace Elsa.Studio.Hosting.Themes;

internal static class ThemeStoragePaths
{
    public static StringComparison ComparisonFor(string path)
    {
        if (OperatingSystem.IsWindows()) return StringComparison.OrdinalIgnoreCase;
        if (!OperatingSystem.IsMacOS()) return StringComparison.Ordinal;

        // macOS supports both case-sensitive and case-insensitive volumes. Query the existing
        // containing filesystem even when a configured store or material directory is not created yet.
        var existing = path;
        while (!Directory.Exists(existing) && !File.Exists(existing))
            existing = Path.GetDirectoryName(existing) ?? throw new IOException("Cannot locate the theme storage filesystem.");
        var sensitive = Pathconf(existing, 11); // _PC_CASE_SENSITIVE in the macOS SDK's sys/unistd.h.
        if (sensitive < 0)
            throw new IOException($"Cannot determine theme storage case sensitivity (errno {Marshal.GetLastPInvokeError()}).");
        return sensitive == 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    [DllImport("libc", EntryPoint = "pathconf", SetLastError = true)]
    private static extern long Pathconf([MarshalAs(UnmanagedType.LPUTF8Str)] string path, int name);
}
