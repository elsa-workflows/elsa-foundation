using CShells.Lifecycle;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The in-memory activation switch beside one EF store selected through its own extension: a mixed composition, which no
/// switch can commit together (#2230). The switch refuses it when constructed, and shell start constructs it so that the
/// shell fails to start rather than its first activation. The EF aggregate's own composition is covered by
/// <see cref="RuntimeEntityFrameworkCoreStandaloneTests"/>.
/// </summary>
public sealed class WorkflowActivationSwitchCompositionTests : IDisposable
{
    private readonly ServiceCollection _services = new();
    private ServiceProvider? _provider;

    public WorkflowActivationSwitchCompositionTests()
    {
        _services.AddWorkflowRuntime();
        // The trigger serving spine the WorkflowsRuntimeTriggers feature composes; without it nothing activates.
        _services.TryAddSingleton<IWorkflowTriggerBindingStore, InMemoryWorkflowTriggerBindingStore>();
        _services.AddScoped<IWorkflowTriggerIndexer>(_ => throw new NotSupportedException("Nothing is activated here."));
    }

    public static TheoryData<string, string> EfParticipants => new()
    {
        { "slot authority", nameof(EfWorkflowActivationAuthority) },
        { "trigger bindings", nameof(EfWorkflowTriggerBindingStore) },
        { "recurring schedules", nameof(EfRecurringTriggerScheduleStore) }
    };

    [Theory]
    [MemberData(nameof(EfParticipants))]
    public async Task One_EF_participant_beside_the_in_memory_switch_is_refused_when_the_shell_starts(string participant, string store)
    {
        SelectOnEntityFramework(participant);

        var construction = Assert.Throws<InvalidOperationException>(ResolveSwitch);
        var shellStart = await Assert.ThrowsAsync<InvalidOperationException>(CheckAtShellStartAsync);

        Assert.Contains(store, construction.Message, StringComparison.Ordinal);
        Assert.StartsWith("Workflow activation cannot start", shellStart.Message, StringComparison.Ordinal);
        Assert.Contains(store, shellStart.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_in_memory_defaults_start()
    {
        Assert.IsType<InMemoryWorkflowActivationSwitch>(ResolveSwitch());
        await CheckAtShellStartAsync();
    }

    /// <summary>The coordinator refuses to activate without the trigger serving spine, so such a composition has nothing to commit together.</summary>
    [Fact]
    public async Task A_composition_that_cannot_activate_is_not_checked()
    {
        SelectOnEntityFramework("trigger bindings");
        _services.RemoveAll<IWorkflowTriggerIndexer>();

        await CheckAtShellStartAsync();
    }

    [Fact]
    public void The_check_runs_once_while_the_shell_prepares()
    {
        _services.AddWorkflowActivationDefaults();

        var registration = Assert.Single(
            Provider.GetServices<ShellInitializerRegistration>(),
            candidate => candidate.InitializerType == typeof(WorkflowActivationSwitchCompositionValidator));
        Assert.Equal(LifecyclePhase.Prepare, registration.Phase);
    }

    public void Dispose() => _provider?.Dispose();

    private ServiceProvider Provider => _provider ??= _services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

    private void SelectOnEntityFramework(string participant)
    {
        _services.AddRuntimeOperationalStateEntityFrameworkCore(new()
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:",
            RecoveryContinuationSigningKey = "switch-composition-recovery-signing-key-32"
        });
        switch (participant)
        {
            case "slot authority": _services.AddRuntimeWorkflowActivationAuthorityEntityFrameworkCore(); break;
            case "trigger bindings": _services.AddRuntimeWorkflowTriggerBindingEntityFrameworkCore(); break;
            case "recurring schedules": _services.AddRuntimeRecurringTriggerScheduleEntityFrameworkCore(); break;
            default: throw new ArgumentOutOfRangeException(nameof(participant));
        }
    }

    private IWorkflowActivationSwitch ResolveSwitch()
    {
        using var scope = Provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IWorkflowActivationSwitch>();
    }

    private Task CheckAtShellStartAsync() => Provider.GetRequiredService<WorkflowActivationSwitchCompositionValidator>().InitializeAsync();
}
