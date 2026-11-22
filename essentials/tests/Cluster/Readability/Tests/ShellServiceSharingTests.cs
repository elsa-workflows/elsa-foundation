using System.Reflection;
using CShells;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// <c>ShareWithShells</c>: a shell container built from copies of the host's registrations resolves the host's own instance of a
/// service another package registered by type, and the host's instance is never one a shell container can dispose.
/// </summary>
public sealed class ShellServiceSharingTests : IAsyncDisposable
{
    private const string ShellName = "orders";

    private readonly ServiceCollection _hostServices = new();
    private ServiceProvider? _host;

    [Fact]
    public async Task Every_shell_generation_resolves_the_hosts_instance_where_an_unshared_registration_gives_each_its_own()
    {
        _hostServices.AddSingleton<Queue>().AddSingleton<Stats>();
        _hostServices.ShareWithShells<Queue>();
        var host = await StartAsync();
        var registry = host.GetRequiredService<IShellRegistry>();

        var first = await registry.GetOrActivateAsync(ShellName);
        var queueInFirst = first.ServiceProvider.GetRequiredService<Queue>();
        var statsInFirst = first.ServiceProvider.GetRequiredService<Stats>();
        await registry.ReloadActiveAsync();
        var reloaded = await registry.GetOrActivateAsync(ShellName);

        Assert.NotSame(first, reloaded);
        Assert.Same(host.GetRequiredService<Queue>(), queueInFirst);
        Assert.Same(host.GetRequiredService<Queue>(), reloaded.ServiceProvider.GetRequiredService<Queue>());
        Assert.NotSame(host.GetRequiredService<Stats>(), statsInFirst);
        Assert.NotSame(statsInFirst, reloaded.ServiceProvider.GetRequiredService<Stats>());
    }

    [Fact]
    public async Task A_shell_container_that_is_disposed_leaves_the_hosts_instance_usable()
    {
        _hostServices.AddSingleton<Queue>();
        _hostServices.ShareWithShells<Queue>();
        var host = await StartAsync();
        var shell = ShellContainer();

        var shared = shell.GetRequiredService<Queue>();
        await shell.DisposeAsync();

        Assert.Same(host.GetRequiredService<Queue>(), shared);
        shared.Enqueue("still usable");
        Assert.Equal(["still usable"], host.GetRequiredService<Queue>().Items);
    }

    /// <summary>
    /// A container disposes what its factories return, so a disposable service shared with the shells would be disposed by the
    /// first shell that drained. It is refused where the host creates it, which is the first resolution in its boot.
    /// </summary>
    [Fact]
    public async Task A_disposable_service_is_refused_at_its_first_resolution_by_name_and_is_never_disposed()
    {
        var owner = new OwnsResources();
        _hostServices.AddSingleton<OwnsResources>(_ => owner);
        _hostServices.ShareWithShells<OwnsResources>();
        var host = await StartAsync();

        var atRoot = Assert.Throws<InvalidOperationException>(() => host.GetRequiredService<OwnsResources>());
        var inShell = Assert.Throws<InvalidOperationException>(() => ShellContainer().GetRequiredService<OwnsResources>());

        Assert.All([atRoot, inShell], exception => Assert.Contains(nameof(OwnsResources), exception.Message, StringComparison.Ordinal));
        Assert.Equal(0, owner.Disposals);
    }

    [Fact]
    public void A_disposable_registration_by_type_is_refused_when_it_is_shared()
    {
        _hostServices.AddSingleton<OwnsResources>();

        var refusal = Assert.Throws<InvalidOperationException>(() => _hostServices.ShareWithShells<OwnsResources>());

        Assert.Contains(nameof(OwnsResources), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_shell_resolving_before_cshells_has_bound_the_host_is_refused_by_name()
    {
        _hostServices.AddSingleton<Queue>();
        _hostServices.ShareWithShells<Queue>();
        _host = _hostServices.BuildServiceProvider();

        var refusal = Assert.Throws<InvalidOperationException>(() => ShellContainer().GetRequiredService<Queue>());

        Assert.Contains(nameof(Queue), refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_a_registered_singleton_can_be_shared()
    {
        _hostServices.AddScoped<Stats>();

        Assert.Throws<InvalidOperationException>(() => _hostServices.ShareWithShells<Queue>());
        Assert.Throws<InvalidOperationException>(() => _hostServices.ShareWithShells<Stats>());
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
    }

    /// <summary>The host with CShells and one shell, and CShells' registry having bound the host's container as it does before any shell is built.</summary>
    private async Task<ServiceProvider> StartAsync()
    {
        _hostServices.AddCShells(shells => shells
            .WithAssemblyProvider(new NoFeatures())
            .AddShell(ShellName, shell => { }));
        _host = _hostServices.BuildServiceProvider();
        await _host.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);
        return _host;
    }

    /// <summary>A container built as CShells builds a shell's: from copies of the host's registrations, holding its own <see cref="ShellSettings"/>.</summary>
    private ServiceProvider ShellContainer()
    {
        IServiceCollection shell = new ServiceCollection();
        foreach (var descriptor in _hostServices)
            shell.Add(descriptor);
        shell.AddSingleton(new ShellSettings(new ShellId("manual")));
        return shell.BuildServiceProvider();
    }

    private sealed class NoFeatures : IFeatureAssemblyProvider
    {
        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<Assembly>>([]);
    }

    /// <summary>State only the host's own instance should hold.</summary>
    private sealed class Queue
    {
        private readonly List<string> _items = [];

        public IReadOnlyList<string> Items => _items;

        public void Enqueue(string item) => _items.Add(item);
    }

    private sealed class Stats;

    private sealed class OwnsResources : IDisposable
    {
        public int Disposals { get; private set; }

        public void Dispose() => Disposals++;
    }
}
