using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa.Workflows.Runtime.KernelHost.Tests.Compiled;

namespace Elsa.Workflows.Runtime.KernelHost.Tests;

public sealed class KernelPersistenceModeTests
{
    [Fact]
    public void Reads_workflow_choice_and_configured_default_without_a_legacy_runner()
    {
        var services = new ServiceCollection();
        var feature = new WorkflowsRuntimeKernelFeature();
        feature.ConfigureServices(services);
        var options = services.Single(descriptor => descriptor.ServiceType == typeof(PersistenceModeOptions));
        var executable = Executable(Node("write", Types.WriteLine));
        using (var provider = services.BuildServiceProvider())
        {
            Assert.Null(provider.GetService<IRuntimePersistenceModeResolver>());
            Assert.Equal(PersistenceMode.Balanced, KernelPersistenceMode.Resolve(provider, executable));
        }

        services.Remove(options);
        services.AddSingleton(new PersistenceModeOptions { Mode = PersistenceMode.Durable });
        feature.ConfigureServices(services);
        var chosen = new WorkflowExecutable(executable.Identity, executable.RootActivity, executable.ResumeTargets,
            executable.CreatedAt, executable.CompatibilityMetadata, executable.InputContract, executable.Dependencies,
            executable.RuntimeRequirements, executable.StorageDriverRequirements, executable.IncidentStrategy,
            persistenceMode: PersistenceMode.Ephemeral);
        using var configured = services.BuildServiceProvider();
        Assert.Equal(PersistenceMode.Durable, KernelPersistenceMode.Resolve(configured, executable));
        Assert.Equal(PersistenceMode.Ephemeral, KernelPersistenceMode.Resolve(configured, chosen));
    }

    [Fact]
    public void Keeps_the_explicit_runner_resolver_in_a_hybrid_shell()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeKernelFeature().ConfigureServices(services);
        services.AddSingleton<IRuntimePersistenceModeResolver>(new CustomResolver());
        using var provider = services.BuildServiceProvider();

        var executable = Executable(Node("write", Types.WriteLine));
        Assert.Equal(PersistenceMode.Durable, KernelPersistenceMode.Resolve(provider, executable));
    }

    private sealed class CustomResolver : IRuntimePersistenceModeResolver
    {
        public PersistenceMode Resolve(WorkflowExecutable executable) => PersistenceMode.Durable;

        public ValueTask<ResolvedPersistenceMode> ResolveAsync(WorkflowExecutionCommandEnvelope envelope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
