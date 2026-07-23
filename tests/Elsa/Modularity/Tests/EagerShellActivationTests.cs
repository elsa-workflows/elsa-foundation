using CShells.Lifecycle;
using Elsa.Server.Boot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Unit tests for opt-in eager shell activation (spec 132, First-Request/Cold-Start Readiness unit 4). Covers the
/// switch parsing, the configured-shell/target-shell resolution rules (default = all configured shells; named
/// shells honored and de-duplicated; the "*" marker), and that the hosted service drives
/// <see cref="IShellRegistry.GetOrActivateAsync"/> — the same activation path the request middleware uses — for
/// exactly the resolved shells, degrading to lazy activation when a shell fails.
/// </summary>
public sealed class EagerShellActivationTests
{
    private static IConfiguration Config(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("True", true)]
    public void IsEnabled_ParsesSwitchLoosely(string? value, bool expected)
    {
        var configuration = Config(new() { [EagerShellActivationOptions.EnabledConfigurationKey] = value });
        Assert.Equal(expected, EagerShellActivationOptions.IsEnabled(configuration));
    }

    [Fact]
    public void ReadConfiguredShellNames_ReturnsChildKeysOfCShellsShells()
    {
        var configuration = Config(new()
        {
            ["CShells:Shells:default:Name"] = "default",
            ["CShells:Shells:tenant-a:Name"] = "tenant-a"
        });

        var names = EagerShellActivationOptions.ReadConfiguredShellNames(configuration);

        Assert.Equal(["default", "tenant-a"], names.OrderBy(n => n).ToList());
    }

    [Fact]
    public void Read_BindsEnabledAndNamedShells()
    {
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            [$"{EagerShellActivationOptions.ShellsConfigurationKey}:0"] = "default",
            [$"{EagerShellActivationOptions.ShellsConfigurationKey}:1"] = "tenant-a"
        });

        var options = EagerShellActivationOptions.Read(configuration);

        Assert.True(options.Enabled);
        Assert.Equal(["default", "tenant-a"], options.Shells);
    }

    [Fact]
    public void ResolveTargetShellNames_EmptyList_MeansAllConfiguredShells()
    {
        var options = new EagerShellActivationOptions { Enabled = true, Shells = [] };
        var targets = options.ResolveTargetShellNames(["default", "tenant-a"]);
        Assert.Equal(["default", "tenant-a"], targets);
    }

    [Fact]
    public void ResolveTargetShellNames_StarMarker_MeansAllConfiguredShells()
    {
        var options = new EagerShellActivationOptions { Enabled = true, Shells = [EagerShellActivationOptions.AllShellsMarker] };
        var targets = options.ResolveTargetShellNames(["default", "tenant-a"]);
        Assert.Equal(["default", "tenant-a"], targets);
    }

    [Fact]
    public void ResolveTargetShellNames_NamedShells_AreHonoredAndDeduplicated()
    {
        var options = new EagerShellActivationOptions { Enabled = true, Shells = ["tenant-a", "tenant-a", "default"] };
        var targets = options.ResolveTargetShellNames(["default", "tenant-a", "tenant-b"]);
        Assert.Equal(["tenant-a", "default"], targets);
    }

    [Fact]
    public async Task HostedService_Disabled_DoesNotActivate()
    {
        var registry = new RecordingShellRegistry();
        var configuration = Config(new() { [EagerShellActivationOptions.EnabledConfigurationKey] = "false" });
        var service = new EagerShellActivationHostedService(registry, configuration, NullLogger<EagerShellActivationHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Empty(registry.Activated);
    }

    [Fact]
    public async Task HostedService_Enabled_ActivatesAllConfiguredShellsByDefault()
    {
        var registry = new RecordingShellRegistry();
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            ["CShells:Shells:default:Name"] = "default",
            ["CShells:Shells:tenant-a:Name"] = "tenant-a"
        });
        var service = new EagerShellActivationHostedService(registry, configuration, NullLogger<EagerShellActivationHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["default", "tenant-a"], registry.Activated.OrderBy(n => n).ToList());
    }

    [Fact]
    public async Task HostedService_Enabled_ActivatesOnlyNamedShells()
    {
        var registry = new RecordingShellRegistry();
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            [$"{EagerShellActivationOptions.ShellsConfigurationKey}:0"] = "tenant-a",
            ["CShells:Shells:default:Name"] = "default",
            ["CShells:Shells:tenant-a:Name"] = "tenant-a"
        });
        var service = new EagerShellActivationHostedService(registry, configuration, NullLogger<EagerShellActivationHostedService>.Instance);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["tenant-a"], registry.Activated);
    }

    [Fact]
    public async Task HostedService_ActivationFailure_IsSwallowedAndOthersStillActivate()
    {
        var registry = new RecordingShellRegistry { FailForShell = "tenant-a" };
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            ["CShells:Shells:default:Name"] = "default",
            ["CShells:Shells:tenant-a:Name"] = "tenant-a",
            ["CShells:Shells:tenant-b:Name"] = "tenant-b"
        });
        var service = new EagerShellActivationHostedService(registry, configuration, NullLogger<EagerShellActivationHostedService>.Instance);

        // Must not throw even though tenant-a activation faults — eager activation degrades to lazy for that shell.
        await service.StartAsync(CancellationToken.None);

        Assert.Contains("default", registry.Activated);
        Assert.Contains("tenant-b", registry.Activated);
        Assert.DoesNotContain("tenant-a", registry.Activated);
    }

    /// <summary>
    /// Minimal <see cref="IShellRegistry"/> fake that records which shell names were driven through
    /// <see cref="GetOrActivateAsync"/> — the one activation entry point eager activation shares with the request
    /// middleware. Every other member is out of scope for these tests.
    /// </summary>
    private sealed class RecordingShellRegistry : IShellRegistry
    {
        public List<string> Activated { get; } = [];
        public string? FailForShell { get; init; }

        public Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default)
        {
            if (FailForShell is not null && string.Equals(name, FailForShell, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Simulated activation failure for '{name}'.");

            Activated.Add(name);
            return Task.FromResult<IShell>(null!);
        }

        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IShell? GetActive(string name) => null;
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }
}
