using CShells.Configuration;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Readability;
using Elsa.Locking.Core;
using Elsa.Tasks;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Distributed.Services;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests.Placement;

/// <summary>
/// The placement-store ownership check (<see cref="ExecutionPlacementStoreBackend.Register"/>) is an options validator
/// registered with <c>ValidateOnStart</c>. The generic host runs such validators through <see cref="IStartupValidator"/>
/// when it starts, against the host container only, so inside a shell the check ran nowhere (#2331). These tests compose
/// the same conflict, a second <see cref="IExecutionPlacementStore"/> added after the distributed feature claimed
/// ownership, once on the host root and once inside a shell of a host that composes <c>AddShellStartupValidation</c>,
/// as both shipped hosts do, and expect both to be refused.
/// </summary>
public sealed class ShellStartupValidationTests
{
    private const string ShellName = "validated";
    private const string Refusal = "no longer exclusively owns IExecutionPlacementStore";

    [Fact]
    public async Task A_competing_store_on_the_host_root_refuses_host_start()
    {
        var builder = CreateBuilder();
        new WorkflowsRuntimeDistributedFeature().ConfigureServices(builder.Services);
        AddCompetingStore(builder.Services);
        using var host = builder.Build();

        var refusal = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains(Refusal, refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_competing_store_in_a_shell_feature_refuses_shell_activation()
    {
        using var host = await StartShellHostAsync(shell => shell.WithFeature<CompetingPlacementStoreFeature>());

        var refusal = await Assert.ThrowsAnyAsync<Exception>(() => ActivateAsync(host));

        Assert.Contains(Refusal, refusal.ToString(), StringComparison.Ordinal);
        await host.StopAsync();
    }

    [Fact]
    public async Task The_distributed_runtime_alone_in_a_shell_passes_its_own_checks()
    {
        using var host = await StartShellHostAsync(_ => { });

        var shell = await ActivateAsync(host);

        Assert.NotNull(shell.ServiceProvider.GetService<IStartupValidator>());
        await host.StopAsync();
    }

    /// <summary>A host that composes the shell startup validation, as both shipped hosts do, with the distributed runtime in one shell.</summary>
    private static async Task<IHost> StartShellHostAsync(Action<ShellBuilder> configureShell)
    {
        var builder = CreateBuilder();
        builder.Services.AddShellStartupValidation();
        builder.Services.AddCShells(shells => shells
            .WithAssemblies(typeof(TasksFeature).Assembly, typeof(WorkflowsRuntimeDistributedFeature).Assembly, typeof(CompetingPlacementStoreFeature).Assembly)
            .AddShell(ShellName, shell => configureShell(shell
                .WithFeature<TasksFeature>()
                // No sweep fires while a test runs.
                .WithFeature<WorkflowsRuntimeDistributedFeature>(feature => feature.SweepIntervalSeconds = TimeSpan.FromHours(1).TotalSeconds))));
        var host = builder.Build();
        await host.StartAsync();
        return host;
    }

    private static Task<IShell> ActivateAsync(IHost host) =>
        host.Services.GetRequiredService<IShellRegistry>().GetOrActivateAsync(ShellName);

    private static HostApplicationBuilder CreateBuilder()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        var services = builder.Services;
        services.Configure<ClusterMembershipOptions>(options => options.HostId = $"startup-validation-{Guid.NewGuid():N}");
        services.AddSingleton(new JoinSweepLedger());
        services.AddSingleton<IWorkflowExecutionStateStore>(new InMemoryWorkflowExecutionStateStore());
        services.AddSingleton<IWorkflowExecutionCommandExecutor>(NoopWorkflowExecutionCommandExecutor.Instance);
        services.AddSingleton<IDistributedLockProvider>(new NoLocks());
        return builder;
    }

    private static void AddCompetingStore(IServiceCollection services) =>
        services.AddScoped<IExecutionPlacementStore>(_ => new InMemoryExecutionPlacementStore());

    /// <summary>Registers after the distributed feature, so the in-memory backend already owns the contract.</summary>
    [ShellFeature(name: "CompetingPlacementStore", DependsOn = new object[] { "WorkflowsRuntimeDistributed" })]
    public sealed class CompetingPlacementStoreFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) => AddCompetingStore(services);
    }
}
