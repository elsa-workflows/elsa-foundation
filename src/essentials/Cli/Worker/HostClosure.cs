using System.Reflection;
using System.Runtime.Loader;

namespace Elsa.Cli.Worker;

/// <summary>
/// The host's own assemblies, loaded into this process the way the host itself would load them.
/// </summary>
/// <remarks>
/// The worker runs on the host's runtimeconfig and deps file (FR-002), so every assembly the host pins is
/// already on this process's trusted platform list — but nothing is loaded until something asks for it, and
/// module discovery reads declarations off <em>loaded</em> assemblies. Without this step an unscoped
/// <c>list</c> against a packaged host would report nothing at all and look like a host with no modules,
/// which is the one answer that must never be silently wrong.
/// </remarks>
public static class HostClosure
{
    /// <summary>The assembly carrying the tooling entry point the worker calls into.</summary>
    public const string PersistenceAssemblyName = "Elsa.Persistence.EntityFramework";

    /// <summary>
    /// Loads every assembly the host's deps file lists a runtime asset for. A failure is skipped rather than
    /// fatal: a host's closure legitimately carries assemblies this process cannot load — a resource
    /// assembly, a platform-specific one — and none of them can declare a module anyway.
    /// </summary>
    public static void Preload(HostDepsFile deps)
    {
        foreach (var name in deps.AssemblyNames.Order(StringComparer.Ordinal))
        {
            try
            {
                Assembly.Load(new AssemblyName(name));
            }
            catch (Exception failure) when (failure is FileNotFoundException or FileLoadException or BadImageFormatException or TypeLoadException)
            {
                // Deliberately silent: a module assembly that fails to load surfaces as an unresolved module
                // with its own named refusal, and warning about every unrelated one would bury it.
            }
        }
    }

    /// <summary>Loads the selected application itself; its deps assets alone do not include the entry assembly.</summary>
    public static void LoadHostAssembly(string hostDirectory, string hostName)
    {
        var expected = Path.Join(hostDirectory, $"{hostName}.dll");
        try
        {
            var host = AssemblyLoadContext.Default.LoadFromAssemblyPath(expected);
            if (!StringComparer.Ordinal.Equals(host.GetName().Name, hostName))
                throw WorkerRefusal.Resolution("host-composition-unavailable",
                    "The selected host assembly does not match its validated layout.");
        }
        catch (WorkerRefusal)
        {
            throw;
        }
        catch (Exception failure) when (failure is BadImageFormatException || WorkerRunner.IsNonFatal(failure))
        {
            throw WorkerRefusal.Resolution("host-composition-unavailable",
                "The selected host assembly could not be loaded for configuration inspection.");
        }
    }

    /// <summary>
    /// Loads the host's own <c>Elsa.Persistence.EntityFramework</c>. A host without one is not a host this
    /// tool can work against at all, and saying so names the one thing to add.
    /// </summary>
    public static Assembly LoadPersistence()
    {
        try
        {
            return Assembly.Load(new AssemblyName(PersistenceAssemblyName));
        }
        catch (Exception failure) when (failure is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            throw WorkerRefusal.Resolution(
                "host-persistence-missing",
                $"This host does not carry '{PersistenceAssemblyName}', so it has no Elsa persistence policy assembly to run migration tooling through: {failure.Message}");
        }
    }
}
