namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

internal static class Repository
{
    public static string Root()
    {
        for (var current = new DirectoryInfo(Directory.GetCurrentDirectory()); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, "AGENTS.md")))
                return current.FullName;
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}

internal sealed class WorkloadFixture : IDisposable
{
    private WorkloadFixture(string root) => Root = root;
    public string Root { get; }

    public static WorkloadFixture CopyFromRepository()
    {
        var root = Path.Combine(Path.GetTempPath(), "elsa-store-performance-", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(Repository.Root(), "specs", "094-harden-groundwork-stores", "workloads");
        var destination = Path.Combine(root, "specs", "094-harden-groundwork-stores", "workloads");
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source, "*.json"))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        return new WorkloadFixture(root);
    }

    public void Replace(string from, string to)
    {
        var path = Directory.EnumerateFiles(Path.Combine(Root, "specs", "094-harden-groundwork-stores", "workloads"), "runtime.json").Single();
        File.WriteAllText(path, File.ReadAllText(path).Replace(from, to, StringComparison.Ordinal));
    }

    public void Dispose() => Directory.Delete(Root, recursive: true);
}
