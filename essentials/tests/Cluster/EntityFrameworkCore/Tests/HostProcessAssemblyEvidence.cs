using System.Security.Cryptography;
using System.Text.Json;
using Xunit.Abstractions;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>Retains identities of the product assemblies the operating system reports in the owned host process.</summary>
internal static class HostProcessAssemblyEvidence
{
    public static bool IsWithinInstallRoot(string mappedPath, string installRoot) =>
        PhysicalPath(mappedPath).StartsWith(PhysicalPath(installRoot) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Path.GetFullPath retains parent-directory links, such as macOS /var -> /private/var. Resolve each
    // existing component so the operator's configured path and the OS-reported mapping name the same directory.
    private static string PhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var resolved = Path.GetPathRoot(fullPath)!;
        foreach (var component in fullPath[resolved.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            resolved = Path.Join(resolved, component);
            FileSystemInfo entry = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
            if (entry.ResolveLinkTarget(returnFinalTarget: true) is { } target)
                resolved = target.FullName;
        }
        return Path.TrimEndingDirectorySeparator(resolved);
    }

    public static void Write(ITestOutputHelper output, string host, int processId, IEnumerable<string> mappedPaths)
    {
        var assemblies = mappedPaths
            .Where(path => Path.GetFileName(path).StartsWith("CShells", StringComparison.Ordinal)
                           || Path.GetFileName(path).StartsWith("Nuplane", StringComparison.Ordinal))
            .Select(path =>
            {
                using var stream = File.OpenRead(path);
                return new { path, sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant() };
            })
            .ToArray();
        Assert.Contains(assemblies, assembly => Path.GetFileName(assembly.path) == "CShells.Nuplane.dll");
        output.WriteLine("HOST_ASSEMBLY_EVIDENCE " + JsonSerializer.Serialize(new { host, processId, assemblies }));
    }
}
