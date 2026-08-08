using System.Reflection;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Loads a target build-output assembly for execution-context inspection (feature metadata, structure
/// handlers, expression providers, intrinsics) with dependency resolution over the tool's own closure
/// plus the target's output directory. The repo builds everything at one version, so same-name
/// assemblies unify to the tool's already-loaded identity — which is what lets instantiated target types
/// satisfy the tool's contract interfaces.
/// </summary>
public static class TargetAssembly
{
    private static readonly List<string> ProbeDirectories = [];
    private static bool resolverHooked;

    public static Assembly LoadForExecution(string assemblyPath)
    {
        HookResolver();

        var directory = Path.GetDirectoryName(Path.GetFullPath(assemblyPath))!;
        lock (ProbeDirectories)
        {
            if (!ProbeDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase))
                ProbeDirectories.Add(directory);
        }

        // Prefer an already-loaded same-name assembly (e.g. a product assembly the tool itself
        // references): loading a second copy from the target's bin would split type identity.
        var name = Path.GetFileNameWithoutExtension(assemblyPath);
        var loaded = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(candidate => !candidate.IsDynamic &&
                                         string.Equals(candidate.GetName().Name, name, StringComparison.OrdinalIgnoreCase));
        return loaded ?? Assembly.LoadFrom(assemblyPath);
    }

    public static void AddProbeDirectory(string directory)
    {
        HookResolver();
        lock (ProbeDirectories)
        {
            if (Directory.Exists(directory) && !ProbeDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase))
                ProbeDirectories.Add(directory);
        }
    }

    private static void HookResolver()
    {
        if (resolverHooked)
            return;
        resolverHooked = true;
        AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
        {
            var simpleName = new AssemblyName(args.Name).Name;
            if (string.IsNullOrEmpty(simpleName))
                return null;

            lock (ProbeDirectories)
            {
                foreach (var directory in ProbeDirectories)
                {
                    var candidate = Path.Combine(directory, simpleName + ".dll");
                    if (File.Exists(candidate))
                    {
                        try
                        {
                            return Assembly.LoadFrom(candidate);
                        }
                        catch (BadImageFormatException)
                        {
                            // Reference-only or non-runtime image; keep probing.
                        }
                    }
                }
            }

            return null;
        };
    }

    /// <summary>Types that resolved; partial loads are tolerated the same way the scanner tolerates them.</summary>
    public static IReadOnlyList<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type is not null).Cast<Type>().ToArray();
        }
    }
}
