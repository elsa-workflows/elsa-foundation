namespace Groundwork.TestInfrastructure;

internal static class RepositoryRootLocator
{
    public static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        DirectoryInfo? gitRoot = null;

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;

            if (gitRoot is null && (Directory.Exists(Path.Combine(directory.FullName, ".git")) || File.Exists(Path.Combine(directory.FullName, ".git"))))
                gitRoot = directory;

            directory = directory.Parent;
        }

        return gitRoot?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
