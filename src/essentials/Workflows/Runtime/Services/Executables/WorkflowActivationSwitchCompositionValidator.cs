using CShells.Lifecycle;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Executables;

/// <summary>
/// Fails shell activation when the shell's <see cref="IWorkflowActivationSwitch"/> cannot be composed with its slot
/// authority and projection stores (#2230): a mixed composition, such as EF slots beside in-memory projections, which
/// nothing could commit together. It constructs the switch once, in a scope of its own, so the switch's own refusal is
/// the check, and names it.
/// </summary>
/// <remarks>
/// <para>
/// The switch's participants are registered by different features and each registration sees only what came before it,
/// so a mixed composition can be told only once every feature has registered. Without this check it would surface at the
/// first activation, inside a startup task that logs and goes on. It runs in the CShells
/// <see cref="LifecyclePhase.Prepare"/> phase after the EF provider-binding check and before any migrator, so nothing has
/// touched a store by the time it fails.
/// </para>
/// <para>
/// A composition without the trigger serving spine (<see cref="IWorkflowTriggerIndexer"/> and
/// <see cref="IWorkflowTriggerBindingStore"/>) never activates, because the coordinator refuses to, so it has nothing to
/// commit together and is not checked.
/// </para>
/// </remarks>
public sealed class WorkflowActivationSwitchCompositionValidator(IServiceScopeFactory scopes, IServiceProviderIsService services) : IShellInitializer
{
    /// <summary>After the EF provider-binding check, at -100, and before the EF module migrators, at 0.</summary>
    internal const int PrepareOrder = -50;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (!services.IsService(typeof(IWorkflowTriggerIndexer)) || !services.IsService(typeof(IWorkflowTriggerBindingStore)))
            return;

        await using var scope = scopes.CreateAsyncScope();
        try
        {
            _ = scope.ServiceProvider.GetRequiredService<IWorkflowActivationSwitch>();
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Workflow activation cannot start: the activation switch cannot be composed with this shell's slot authority and projection stores. {exception.Message}",
                exception);
        }
    }

    /// <summary>Registers the check once per service collection.</summary>
    internal static void Register(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(WorkflowActivationSwitchCompositionValidator)))
            return;
        services.AddShellInitializer<WorkflowActivationSwitchCompositionValidator>(LifecyclePhase.Prepare, PrepareOrder);
    }
}
