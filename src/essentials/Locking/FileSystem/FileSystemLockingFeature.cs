using CShells.Features;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Locking.FileSystem.Options;
using Medallion.Threading.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Locking.FileSystem;

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Locking")]
[ManifestFeatureCategory("Infrastructure")]
[ShellFeature(
    name: "FileSystemDistributedLocking",
    DisplayName = "File System Distributed Locking",
    Description = "Provides services to enable distributed locking using the file system"
)]
public class FileSystemLockingFeature : IShellFeature
{
    /// <summary>The folder this feature keeps its locks in unless <see cref="LocksFolderPath"/> names another: node-local.</summary>
    private static string DefaultLocksFolderPath => Path.Join(Environment.CurrentDirectory, "App_Data", "locks");

    [ManifestSetting(DisplayName = "Locks folder path", Description = "Directory used to store file-system distributed lock files. A lock excludes only the processes that share this folder, so a host that is a member of a cluster must name a folder every node shares, or compose DatabaseDistributedLocking instead.", Category = "Locking", Required = true)]
    public string LocksFolderPath
    {
        get => _locksFolderPath ?? DefaultLocksFolderPath;
        set => _locksFolderPath = value;
    }

    // Null until a host names a folder, so a folder that happens to equal the default still counts as chosen.
    private string? _locksFolderPath;

    [ManifestSetting(DisplayName = "Lock acquisition timeout", Description = "Maximum time in minutes to wait when acquiring a distributed lock.", Category = "Locking", DefaultValue = "10")]

    public double LockAcquisitionTimeoutMinutes { get; set; } = 10;

    public void ConfigureServices(IServiceCollection services)
    {
        RefuseNodeLocalFolderInACluster(services);

        services.Configure<DistributedLockingOptions>(options =>
        {
            options.LockAcquisitionTimeout = TimeSpan.FromMinutes(LockAcquisitionTimeoutMinutes);
        });
        services.AddSingleton<Elsa.Locking.Core.IDistributedLockProvider>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DistributedLockingOptions>>();
            var medallionLockProvider = new FileDistributedSynchronizationProvider(new DirectoryInfo(LocksFolderPath));
            return new DistributedLockProviderAdaptor(medallionLockProvider, options);
        });
    }

    /// <summary>
    /// Refuses the unconfigured, node-local default folder on a host that joined a cluster through a durable membership provider (#2192):
    /// there every node would take the same lock in a folder of its own, and each would believe it held it alone.
    /// </summary>
    /// <remarks>
    /// The host composes membership on its own container, and CShells copies the host's registrations into the shell's
    /// collection before any feature configures it, so the provider's registration is visible here. A folder the host
    /// configured is trusted to be one every node shares, even one equal to the default, because setting a shared path is the
    /// remedy this refusal names; only a default nobody chose is known not to be.
    /// </remarks>
    private void RefuseNodeLocalFolderInACluster(IServiceCollection services)
    {
        var durable = services
            .Where(descriptor => descriptor.ServiceType == typeof(ClusterMembershipProviderRegistration) && !descriptor.IsKeyedService)
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<ClusterMembershipProviderRegistration>()
            .FirstOrDefault(registration => registration.Kind == ClusterProviderKind.Durable);
        if (durable is null || _locksFolderPath is not null)
            return;

        throw new InvalidOperationException(
            $"FileSystemDistributedLocking keeps its locks in '{LocksFolderPath}', the default folder under this process's working " +
            $"directory, but this host is a member of a cluster through the durable membership provider '{durable.Name}'. A lock " +
            "in a node-local folder excludes only the processes that share it, so every node would take the same lock at once. " +
            "Compose DatabaseDistributedLocking (PostgreSql, SqlServer or MySql) instead, or set " +
            $"FileSystemDistributedLocking:{nameof(LocksFolderPath)} to a folder every node of the cluster shares.");
    }
}
