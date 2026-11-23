using Xunit;

namespace Elsa.Workbench.Tests;

/// <summary>
/// #2191 on the stock Workbench: clustered through the durable EF membership provider from configuration alone, it says as it
/// starts when its Data Protection key ring is its own, since nothing else enforces sharing it: a host that does not share it
/// starts and serves, and refuses only the cookies and antiforgery tokens another host issued. The same host sharing the key
/// ring says nothing, so the warning is not one every clustered host logs.
/// </summary>
public sealed class WorkbenchDataProtectionTests : IDisposable
{
    /// <summary>What the host's startup check says, restated: nothing of the host is loaded into this process.</summary>
    private const string NotShared = "Data Protection key ring is its own";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The lock folder the clustered hosts name: a cluster refuses the file-system lock on its node-local default folder
    /// (#2192), and the database lock refuses the SQLite database these hosts use, so the folder is set explicitly, as
    /// the nodes of a real cluster would share one.
    /// </summary>
    private readonly string _locksFolderPath = Path.Join(Path.GetTempPath(), $"workbench-keys-locks-{Guid.NewGuid():n}");

    public void Dispose()
    {
        if (Directory.Exists(_locksFolderPath))
            Directory.Delete(_locksFolderPath, recursive: true);
    }

    [Fact]
    public async Task A_clustered_workbench_without_the_shared_key_store_warns_as_it_starts()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(Clustered("workbench-keys-alone"));

        Assert.Contains(NotShared, await StartedOutputAsync(workbench), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clustered_workbench_with_the_shared_key_store_does_not()
    {
        await using var workbench = await WorkbenchProcess.StartAsync(Clustered("workbench-keys-shared", new Dictionary<string, string>
        {
            ["Elsa:DataProtection:EntityFrameworkCore:Enabled"] = "true",
            ["Elsa:DataProtection:EntityFrameworkCore:Provider"] = "Sqlite",
            ["Elsa:DataProtection:EntityFrameworkCore:ConnectionString"] = "Data Source=keys.db;Pooling=False"
        }));

        Assert.DoesNotContain(NotShared, await StartedOutputAsync(workbench), StringComparison.Ordinal);
    }

    /// <summary>
    /// The stock development composition with the durable EF membership provider enabled under <paramref name="hostId"/>, over
    /// a SQLite file in the host's own directory, which is its working directory too, and the explicit lock folder.
    /// </summary>
    private WorkbenchShell Clustered(string hostId, IReadOnlyDictionary<string, string>? more = null)
    {
        var settings = new Dictionary<string, string>(WorkbenchShell.Development.Settings)
        {
            ["CShells:Shells:default:Features:FileSystemDistributedLocking:LocksFolderPath"] = _locksFolderPath,
            ["Elsa:Cluster:Membership:HostId"] = hostId,
            ["Elsa:Cluster:Membership:EntityFrameworkCore:Enabled"] = "true",
            ["Elsa:Cluster:Membership:EntityFrameworkCore:Provider"] = "Sqlite",
            ["Elsa:Cluster:Membership:EntityFrameworkCore:ConnectionString"] = "Data Source=membership.db;Pooling=False"
        };
        foreach (var (key, value) in more ?? new Dictionary<string, string>())
            settings[key] = value;
        return WorkbenchShell.Development with { Settings = settings };
    }

    /// <summary>
    /// The host's output once it reports itself started, which it does after every hosted service has started, the startup
    /// check among them, so a warning the check logged is in the output by then.
    /// </summary>
    private static async Task<string> StartedOutputAsync(WorkbenchProcess workbench)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!workbench.Output.Contains("Application started", StringComparison.Ordinal))
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException($"The Workbench did not report itself started within {Patience}. Host output:{Environment.NewLine}{workbench.Output}");
            await Task.Delay(100);
        }

        return workbench.Output;
    }
}
