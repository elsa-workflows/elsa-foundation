using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Events;
using Elsa.Locking.Core;
using Elsa.Primitives.Models;
using Elsa.Rules.Runtime;
using Elsa.Rules.Workflows.Runtime;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Elsa.Rules.Tests;

public sealed class RulesWorkflowsRuntimeCatalogTests
{
    private const string ShellName = "rules-runtime-catalog";

    [Fact]
    public async Task Composed_rules_runtime_feature_registers_RunRules_in_the_shell_clr_type_catalog()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using var lifetime = new TestHostApplicationLifetime();
        services.AddSingleton<IHostApplicationLifetime>(lifetime);
        services.AddCShells(builder => builder
            .WithAssemblies(
                typeof(SerializationFeature).Assembly,
                typeof(TasksFeature).Assembly,
                typeof(EventsFeature).Assembly,
                typeof(ActivitiesRuntimeFeature).Assembly,
                typeof(ActivitiesPrimitivesFeature).Assembly,
                typeof(RulesRuntimeFeature).Assembly,
                typeof(WorkflowsRuntimeResumptionFeature).Assembly,
                typeof(RulesWorkflowsRuntimeFeature).Assembly,
                typeof(RulesWorkflowsRuntimeCatalogTests).Assembly)
            .AddShell(ShellName, shell => shell
                .WithFeature<SerializationFeature>()
                .WithFeature<TasksFeature>()
                .WithFeature<EventsFeature>()
                .WithFeature<ActivitiesRuntimeFeature>()
                .WithFeature<ActivitiesPrimitivesFeature>()
                .WithFeature<RulesRuntimeFeature>()
                .WithFeature<WorkflowsRuntimeResumptionFeature>()
                .WithFeature<RulesWorkflowsRuntimeFeature>()
                .WithFeature<TestLockingFeature>()));

        await using var root = services.BuildServiceProvider();
        var registry = root.GetRequiredService<IShellRegistry>();
        var shell = await registry.GetOrActivateAsync(ShellName, CancellationToken.None);
        try
        {
            await using var scope = shell.BeginScope();
            var shellServices = scope.ServiceProvider;

            var featureDescriptors = shellServices.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>();
            Assert.Contains(featureDescriptors, descriptor => descriptor.StartupType == typeof(RulesWorkflowsRuntimeFeature));

            var types = shellServices.GetRequiredService<IWellKnownTypeRegistry>();
            var runRulesAlias = TypeAliasConvention.CanonicalAlias(typeof(RunRules));
            Assert.True(types.TryGetType(runRulesAlias, out var resolvedType));
            Assert.Equal(typeof(RunRules), resolvedType);
        }
        finally
        {
            lifetime.StopApplication();
            await (await registry.DrainAsync(shell)).WaitAsync();
        }
    }

    [ShellFeature(name: "RulesCatalogTestLocking", DisplayName = "Rules Catalog Test Locking",
        Description = "Supplies the Tasks feature's unused distributed-lock dependency.")]
    public sealed class TestLockingFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.AddSingleton<IDistributedLockProvider, NoopDistributedLockProvider>();

        private sealed class NoopDistributedLockProvider : IDistributedLockProvider
        {
            public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null,
                CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null,
                CancellationToken cancellationToken = default) => throw new NotSupportedException();

            public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null,
                CancellationToken cancellationToken = default) => throw new NotSupportedException();
        }
    }

    private sealed class TestHostApplicationLifetime : IHostApplicationLifetime, IDisposable
    {
        private readonly CancellationTokenSource _stopping = new();
        private readonly CancellationTokenSource _stopped = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => _stopping.Token;
        public CancellationToken ApplicationStopped => _stopped.Token;

        public void StopApplication()
        {
            _stopping.Cancel();
            _stopped.Cancel();
        }

        public void Dispose()
        {
            _stopping.Dispose();
            _stopped.Dispose();
        }
    }
}
