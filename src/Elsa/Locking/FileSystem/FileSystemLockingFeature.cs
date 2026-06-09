using CShells.Features;
using Elsa.Locking.FileSystem.Options;
using Medallion.Threading.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Locking.FileSystem;

[ShellFeature(
    name: "FileSystemDistributedLocking",
    DisplayName = "File System Distributed Locking",
    Description = "Provides services to enable distributed locking using the file system"
)]
public class FileSystemLockingFeature : IShellFeature
{
    public string LocksFolderPath { get; set; } = Path.Combine(Environment.CurrentDirectory, "App_Data/locks");

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