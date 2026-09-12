using System.Reflection;

namespace Elsa.Workbench.Tests;

/// <summary>Locates the Workbench's committed configuration and its build output.</summary>
public static class WorkbenchBuild
{
    public static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly string SourceDirectory = Path.Join(RepositoryRoot, "src", "Apps", "Elsa.Workbench");

    public static string SourceFile(string fileName) => Path.Join(SourceDirectory, fileName);

    /// <summary>
    /// <c>Elsa.Workbench.dll</c> from the host's own <c>bin</c> folder, built with the same configuration and target
    /// framework as this test assembly. The build-order project reference guarantees it exists.
    /// </summary>
    public static string AssemblyPath()
    {
        var configuration = typeof(WorkbenchBuild).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var targetFramework = Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var path = Path.Join(SourceDirectory, "bin", configuration, targetFramework, "Elsa.Workbench.dll");

        return File.Exists(path)
            ? path
            : throw new FileNotFoundException($"Build src/Apps/Elsa.Workbench ({configuration}) before running these tests.", path);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        }

        throw new InvalidOperationException($"No Elsa.Server.slnx above {AppContext.BaseDirectory}.");
    }
}
