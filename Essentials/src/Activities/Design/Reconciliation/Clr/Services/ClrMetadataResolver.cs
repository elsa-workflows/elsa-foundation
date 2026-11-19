using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>Shared reflection-only dependency resolution; never resolves a client alias to a CLR type.</summary>
internal static class ClrMetadataResolver
{
    private static readonly Lazy<IReadOnlyList<string>> InvariantFrameworkPaths = new(CollectInvariantFrameworkPaths);

    // Resolution precedence stays folder → AppDomain → cached framework closure. The process-lifetime
    // framework paths are cached; the live AppDomain dependency set is read on each call.
    internal static Dictionary<string, string>.ValueCollection BuildResolverPaths(IEnumerable<string> folderDlls, ILogger logger)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);

        void Add(string path)
        {
            if (!seenPaths.Add(path))
                return;

            var name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name))
                return;

            if (byName.TryGetValue(name, out var existing))
            {
                // A later source (AppDomain or framework closure) offers the same simple-name as an
                // already-registered path. First-wins is intentional. Another copy of the same identity is the
                // normal layout of one package (a bin-root copy beside its runtimes/<rid>/lib asset) and hides
                // nothing. A divergent copy can mask a version the author shipped, so that drop must not be silent.
                if (HaveSameIdentity(existing, path))
                {
                    logger.LogDebug(
                        "Assembly '{AssemblyName}' has an identical copy at '{SkippedPath}'; keeping '{KeptPath}' for reflection-only resolution.",
                        name, path, existing);
                    return;
                }

                logger.LogWarning(
                    "Duplicate assembly name '{AssemblyName}' during reflection-only resolver setup; keeping '{KeptPath}' and skipping '{SkippedPath}'.",
                    name, existing, path);
                return;
            }

            byName[name] = path;
        }

        // Author assemblies win over framework copies so we read the version the author shipped.
        foreach (var dll in folderDlls)
            Add(dll);

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
                continue;

            var location = assembly.Location;
            if (!string.IsNullOrEmpty(location) && File.Exists(location))
                Add(location);
        }

        // Cached framework closure (base directory → TPA → runtime directory), in the same
        // first-wins order as before; folder + AppDomain entries above still take precedence.
        foreach (var dll in InvariantFrameworkPaths.Value)
            Add(dll);

        return byName.Values;
    }

    /// <summary>
    /// Whether two files carry the same assembly identity: simple name, version, culture, and public key token. Reads
    /// metadata only, without loading either assembly. A file whose identity cannot be read is treated as divergent so
    /// the caller still warns about it.
    /// </summary>
    private static bool HaveSameIdentity(string firstPath, string secondPath)
    {
        try
        {
            var first = AssemblyName.GetAssemblyName(firstPath);
            var second = AssemblyName.GetAssemblyName(secondPath);
            return string.Equals(first.Name, second.Name, StringComparison.OrdinalIgnoreCase) &&
                   first.Version == second.Version &&
                   string.Equals(first.CultureName ?? string.Empty, second.CultureName ?? string.Empty, StringComparison.OrdinalIgnoreCase) &&
                   (first.GetPublicKeyToken() ?? []).AsSpan().SequenceEqual(second.GetPublicKeyToken() ?? []);
        }
        catch (Exception exception) when (exception is IOException or BadImageFormatException or ArgumentException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static IReadOnlyList<string> CollectInvariantFrameworkPaths()
    {
        var paths = new List<string>();

        // The host's own dependency closure (e.g. the activity base types and their references)
        // lives next to the host binary; these are not necessarily loaded into the AppDomain yet,
        // so add them explicitly to satisfy reflection-only base-type/reference resolution.
        var baseDirectory = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDirectory) && Directory.Exists(baseDirectory))
            paths.AddRange(Directory.EnumerateFiles(baseDirectory, "*.dll"));

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string trustedPlatformAssemblies)
            paths.AddRange(trustedPlatformAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        paths.AddRange(Directory.EnumerateFiles(RuntimeEnvironment.GetRuntimeDirectory(), "*.dll"));

        return paths;
    }
}
