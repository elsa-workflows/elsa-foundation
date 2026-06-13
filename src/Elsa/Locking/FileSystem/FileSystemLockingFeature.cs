using CShells.Features;
using Elsa.Platform.PackageManifest.Generator.Hints;
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
    [ManifestSetting(DisplayName = "Locks folder path", Description = "Directory used to store file-system distributed lock files.", Category = "Locking", Required = true)]
    public string LocksFolderPath { get; set; } = Path.Combine(Environment.CurrentDirectory, "App_Data/locks");

    [ManifestSetting(DisplayName = "Lock acquisition timeout", Description = "Maximum time in minutes to wait when acquiring a distributed lock.", Category = "Locking", DefaultValue = "10")]

    public double LockAcquisitionTimeoutMinutes { get; set; } = 10;

    public void ConfigureServices(IServiceCollection services)
    {
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
}