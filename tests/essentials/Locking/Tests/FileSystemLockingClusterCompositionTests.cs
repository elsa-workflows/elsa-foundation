using CShells.DependencyInjection;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.InProcess;
using Elsa.Locking.Core;
using Elsa.Locking.FileSystem;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Locking.Tests;

/// <summary>
/// A host that joined a cluster through a durable membership provider refuses the file-system lock on its unconfigured default folder,
/// which is node-local, when the shell starts (#2192). The membership provider is composed on the host container, as a real
/// host composes it, and the lock in a shell feature, so this also holds that the shell sees the host's provider.
/// </summary>
public sealed class FileSystemLockingClusterCompositionTests : IAsyncDisposable
{
    private const string ShellName = "locking";
    private const string DurableProvider = "durable-under-test";

    private readonly string _sharedFolder = Path.Join(Path.GetTempPath(), $"elsa-locking-{Guid.NewGuid():N}");
    private IHost? _host;

    [Fact]
    public async Task A_cluster_member_refuses_the_default_folder_at_shell_start()
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => ActivateAsync(durableMembership: true));

        var messages = Messages(failure);
        Assert.Contains("FileSystemDistributedLocking keeps its locks in", messages);
        Assert.Contains($"durable membership provider '{DurableProvider}'", messages);
        Assert.Contains("Compose DatabaseDistributedLocking", messages);
        Assert.Contains("FileSystemDistributedLocking:LocksFolderPath", messages);
    }

    [Theory]
    [InlineData("App_Data/locks")]
    [InlineData("app_data/LOCKS/")]
    public async Task A_cluster_member_accepts_a_configured_folder_even_when_it_equals_the_default(string spelling)
    {
        var shell = await ActivateAsync(durableMembership: true, locksFolderPath: Path.Join(Environment.CurrentDirectory, spelling));

        Assert.NotNull(shell.ServiceProvider.GetRequiredService<IDistributedLockProvider>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_cluster_member_refuses_a_blank_folder_as_not_chosen(string blank)
    {
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => ActivateAsync(durableMembership: true, locksFolderPath: blank));

        Assert.Contains("FileSystemDistributedLocking keeps its locks in", Messages(failure));
    }

    [Fact]
    public async Task A_cluster_member_starts_with_a_folder_it_names()
    {
        var shell = await ActivateAsync(durableMembership: true, locksFolderPath: _sharedFolder);

        Assert.NotNull(shell.ServiceProvider.GetRequiredService<IDistributedLockProvider>());
    }

    [Fact]
    public async Task A_cluster_of_one_starts_with_the_default_folder()
    {
        var shell = await ActivateAsync(durableMembership: false);

        Assert.NotNull(shell.ServiceProvider.GetRequiredService<IDistributedLockProvider>());
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        if (Directory.Exists(_sharedFolder))
            Directory.Delete(_sharedFolder, recursive: true);
    }

    private async Task<IShell> ActivateAsync(bool durableMembership, string? locksFolderPath = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        var services = builder.Services;
        if (durableMembership)
        {
            services.Configure<ClusterMembershipOptions>(options => options.HostId = "locking-test-host");
            services.AddClusterMembershipProvider(new ClusterMembershipProviderRegistration(
                DurableProvider,
                ClusterProviderKind.Durable,
                ServiceDescriptor.Singleton<IClusterMembership>(_ => throw new NotSupportedException("Membership is not used here."))));
        }
        else
            services.TryAddInProcessClusterMembership();

        services.AddCShells(shells => shells
            .WithAssemblies(typeof(FileSystemLockingFeature).Assembly)
            .AddShell(ShellName, shell => shell.WithFeature<FileSystemLockingFeature>(feature =>
            {
                if (locksFolderPath is not null)
                    feature.LocksFolderPath = locksFolderPath;
            })));
        _host = builder.Build();
        await _host.StartAsync();
        return await _host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
    }

    private static string Messages(Exception exception) =>
        string.Join(" | ", Chain(exception).Select(inner => inner.Message));

    private static IEnumerable<Exception> Chain(Exception? exception)
    {
        for (; exception is not null; exception = exception.InnerException)
            yield return exception;
    }
}
