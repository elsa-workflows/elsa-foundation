namespace Groundwork.TestInfrastructure;

internal static class RepositoryRootLocator
{
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? solutionRoot = null;

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;

            if (solutionRoot is null && File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                solutionRoot = directory;

            directory = directory.Parent;
        }

        return solutionRoot?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
