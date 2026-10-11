using System.Collections.Concurrent;
using CShells.DependencyInjection;
using CShells.Hosting;
using CShells.Lifecycle;
using Elsa.Workbench.Boot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>
/// Unit tests for opt-in eager shell activation (spec 132, First-Request/Cold-Start Readiness unit 4). Covers the
/// switch parsing, the configured-shell/target-shell resolution rules (default = all configured shells; named
/// shells honored and de-duplicated; the "*" marker), and that the hosted service drives the shared runner for
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
        using var provider = CreateRunnerProvider(registry);
        var service = CreateService(configuration, provider);

        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(registry.Activated);
        await service.StopAsync(CancellationToken.None);
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
        using var provider = CreateRunnerProvider(registry);
        var service = CreateService(configuration, provider);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["default", "tenant-a"], registry.Activated.OrderBy(n => n).ToList());
        await service.StopAsync(CancellationToken.None);
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
        using var provider = CreateRunnerProvider(registry);
        var service = CreateService(configuration, provider);

        await service.StartAsync(CancellationToken.None);

        Assert.Equal(["tenant-a"], registry.Activated.ToArray());
        await service.StopAsync(CancellationToken.None);
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
        using var provider = CreateRunnerProvider(registry);
        var service = CreateService(configuration, provider);

        // Must not throw even though tenant-a activation faults — eager activation degrades to lazy for that shell.
        await service.StartAsync(CancellationToken.None);

        Assert.Contains("default", registry.Activated);
        Assert.Contains("tenant-b", registry.Activated);
        Assert.DoesNotContain("tenant-a", registry.Activated);
        await service.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData("out-of-memory")]
    [InlineData("stack-overflow")]
    [InlineData("access-violation")]
    [InlineData("app-domain-unloaded")]
    [InlineData("bad-image")]
    public async Task HostedService_FoundationFatalExceptionsKeepWorkbenchContinuePolicy(string failureKind)
    {
        Exception failure = failureKind switch
        {
            "out-of-memory" => new OutOfMemoryException("simulated"),
            "stack-overflow" => new StackOverflowException("simulated"),
            "access-violation" => new AccessViolationException("simulated"),
            "app-domain-unloaded" => new AppDomainUnloadedException("simulated"),
            "bad-image" => new BadImageFormatException("simulated"),
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind), failureKind, "Unknown failure kind.")
        };
        var registry = new RecordingShellRegistry { FailForShell = "tenant-a", FailureException = failure };
        var logger = new CapturingLogger();
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            [$"{EagerShellActivationOptions.ShellsConfigurationKey}:0"] = "tenant-a",
            [$"{EagerShellActivationOptions.ShellsConfigurationKey}:1"] = "tenant-b",
            ["CShells:Shells:tenant-a:Name"] = "tenant-a",
            ["CShells:Shells:tenant-b:Name"] = "tenant-b"
        });
        using var provider = CreateRunnerProvider(registry, logger);
        var service = CreateService(configuration, provider);

        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["tenant-a", "tenant-b"], registry.Started.ToArray());
        Assert.Equal(["tenant-b"], registry.Activated.ToArray());
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("tenant-a", StringComparison.Ordinal));
        Assert.Contains(logger.Exceptions, logged => ReferenceEquals(failure, logged));
    }

    [Fact]
    public async Task HostedService_UncancelledOperationCanceledExceptionContinuesWithoutRetry()
    {
        var failure = new OperationCanceledException("simulated uncancelled activation failure");
        var registry = new RecordingShellRegistry { FailForShell = "tenant-a", FailureException = failure };
        var logger = new CapturingLogger();
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            [$"{EagerShellActivationOptions.ShellsConfigurationKey}:0"] = "tenant-a",
            [$"{EagerShellActivationOptions.ShellsConfigurationKey}:1"] = "tenant-b",
            ["CShells:Shells:tenant-a:Name"] = "tenant-a",
            ["CShells:Shells:tenant-b:Name"] = "tenant-b"
        });
        using var provider = CreateRunnerProvider(registry, logger);
        var service = CreateService(configuration, provider);

        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["tenant-a", "tenant-b"], registry.Started.ToArray());
        Assert.Equal(["tenant-b"], registry.Activated.ToArray());
        Assert.Contains(logger.Exceptions, logged => ReferenceEquals(failure, logged));
    }

    [Fact]
    public async Task HostedService_BoundedStopRetainsAndJoinsCancellationIgnoringActivation()
    {
        var registry = new RecordingShellRegistry { BlockForShell = "default", IgnoreCancellation = true };
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            ["CShells:Shells:default:Name"] = "default"
        });
        using var provider = CreateRunnerProvider(registry);
        var service = CreateService(configuration, provider);
        var start = service.StartAsync(CancellationToken.None);

        try
        {
            await registry.WaitUntilEnteredAsync();
            using var boundedWait = new CancellationTokenSource();
            var firstStop = service.StopAsync(boundedWait.Token);
            boundedWait.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstStop.WaitAsync(TimeSpan.FromSeconds(10)));

            Assert.False(start.IsCompleted);
            Assert.Equal(["default"], registry.Started.ToArray());
            registry.Release();
            var startFailure = await Record.ExceptionAsync(() => start.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.True(startFailure is null or OperationCanceledException, startFailure?.ToString());
            await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(["default"], registry.Started.ToArray());
            Assert.Single(registry.Activated);
        }
        finally
        {
            registry.Release();
            await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    [Fact]
    public async Task HostedService_RepeatedStartRunsOnlyOnePass()
    {
        var registry = new RecordingShellRegistry();
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            ["CShells:Shells:default:Name"] = "default"
        });
        using var provider = CreateRunnerProvider(registry);
        var service = CreateService(configuration, provider);

        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["default"], registry.Activated);
    }

    [Fact]
    public async Task HostedService_PreservesConfiguredSerialOrder()
    {
        var registry = new RecordingShellRegistry();
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            [$"{EagerShellActivationOptions.ShellsConfigurationKey}:0"] = "tenant-b",
            [$"{EagerShellActivationOptions.ShellsConfigurationKey}:1"] = "tenant-a",
            ["CShells:Shells:tenant-a:Name"] = "tenant-a",
            ["CShells:Shells:tenant-b:Name"] = "tenant-b"
        });
        using var provider = CreateRunnerProvider(registry);
        var service = CreateService(configuration, provider);

        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["tenant-b", "tenant-a"], registry.Activated.ToArray());
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task HostedService_CancellationStopsTheSerialInitialPass()
    {
        var registry = new RecordingShellRegistry { BlockForShell = "default" };
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            ["CShells:Shells:default:Name"] = "default",
            ["CShells:Shells:tenant-a:Name"] = "tenant-a"
        });
        using var provider = CreateRunnerProvider(registry);
        var service = CreateService(configuration, provider);
        using var cancellation = new CancellationTokenSource();

        var start = service.StartAsync(cancellation.Token);
        await registry.WaitUntilEnteredAsync();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(TimeSpan.FromSeconds(10)));
        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(["default"], registry.Started.ToArray());
    }

    [Fact]
    public async Task HostedService_StopBeforeStartPreventsLaterActivation()
    {
        var registry = new RecordingShellRegistry();
        var configuration = Config(new()
        {
            [EagerShellActivationOptions.EnabledConfigurationKey] = "true",
            ["CShells:Shells:default:Name"] = "default"
        });
        using var provider = CreateRunnerProvider(registry);
        var service = CreateService(configuration, provider);

        await service.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));
        await service.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Empty(registry.Started);
    }

    private static ServiceProvider CreateRunnerProvider(RecordingShellRegistry registry, ILoggerProvider? loggerProvider = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            if (loggerProvider is not null)
                builder.AddProvider(loggerProvider);
        });
        services.AddSingleton<IShellRegistry>(registry);
        services.AddShellActivationRunner();
        return services.BuildServiceProvider();
    }

    private static EagerShellActivationHostedService CreateService(IConfiguration configuration, IServiceProvider provider) =>
        new(configuration, provider.GetRequiredService<IShellActivationRunner>(), provider.GetRequiredService<ILogger<EagerShellActivationHostedService>>());

    /// <summary>
    /// Minimal custom registry that records activation calls and returns a stable current shell for runner verification.
    /// </summary>
    private sealed class RecordingShellRegistry : IShellRegistry
    {
        private readonly ConcurrentQueue<string> _activated = new();
        private readonly ConcurrentQueue<string> _started = new();
        private readonly ConcurrentDictionary<string, IShell> _active = new(StringComparer.OrdinalIgnoreCase);
        private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<string> Activated => _activated.ToArray();
        public IReadOnlyCollection<string> Started => _started.ToArray();
        public string? FailForShell { get; init; }
        public Exception? FailureException { get; init; }
        public string? BlockForShell { get; init; }
        public bool IgnoreCancellation { get; init; }

        public async Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default)
        {
            _started.Enqueue(name);
            if (BlockForShell is not null && string.Equals(name, BlockForShell, StringComparison.OrdinalIgnoreCase))
            {
                _entered.TrySetResult();
                if (IgnoreCancellation)
                    await _release.Task.WaitAsync(TimeSpan.FromSeconds(10));
                else
                    await _release.Task.WaitAsync(cancellationToken);
            }

            if (FailForShell is not null && string.Equals(name, FailForShell, StringComparison.OrdinalIgnoreCase))
                throw FailureException ?? new InvalidOperationException($"Simulated activation failure for '{name}'.");

            var shell = new NuplaneHostTestComposition.StubShell(name);
            _activated.Enqueue(name);
            _active[name] = shell;
            return shell;
        }

        public Task WaitUntilEnteredAsync() => _entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        public void Release() => _release.TrySetResult();

        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IShell? GetActive(string name) => _active.GetValueOrDefault(name);
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }
}
