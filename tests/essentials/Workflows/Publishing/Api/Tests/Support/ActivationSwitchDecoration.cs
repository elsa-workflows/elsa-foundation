using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

/// <summary>
/// Decorates the in-memory <see cref="IWorkflowActivationSwitch"/> a host composes, which moves every slot (#2230), so a
/// test can fail or race a slot transition where the runtime makes it.
/// </summary>
internal static class ActivationSwitchDecoration
{
    public static void Decorate(this IServiceCollection services, Func<IServiceProvider, IWorkflowActivationSwitch, IWorkflowActivationSwitch> decorate)
    {
        services.RemoveAll<IWorkflowActivationSwitch>();
        services.AddScoped(sp => decorate(sp, ActivatorUtilities.CreateInstance<InMemoryWorkflowActivationSwitch>(sp)));
    }
}

/// <summary>Forwards every switch operation; a test double overrides the one it intercepts.</summary>
internal abstract class ForwardingActivationSwitch(IWorkflowActivationSwitch inner) : IWorkflowActivationSwitch
{
    public virtual ValueTask<WorkflowActivationTransition> TryActivateAsync(WorkflowActivationSlotRequest request, CancellationToken cancellationToken = default) =>
        inner.TryActivateAsync(request, cancellationToken);

    public virtual ValueTask<bool> TryRevertAsync(WorkflowActivationRevert revert, CancellationToken cancellationToken = default) =>
        inner.TryRevertAsync(revert, cancellationToken);

    public virtual ValueTask<WorkflowActivationTransition> TryDeactivateAsync(
        WorkflowDeactivationSlotRequest request,
        IReadOnlyCollection<string> alsoServing,
        CancellationToken cancellationToken = default) =>
        inner.TryDeactivateAsync(request, alsoServing, cancellationToken);

    public virtual ValueTask<bool> TryDiscardAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default) =>
        inner.TryDiscardAsync(reference, cancellationToken);
}
