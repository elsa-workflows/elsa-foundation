using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Dispatch scaffold shared by the payload-typed scheduler work handlers: deserialize the work item's
/// payload once, then run the handler body either against the pipeline's ambient services (staged
/// explicitly by the dispatcher — RT-7 replaced the AsyncLocal service locator) or against a fresh
/// scope for direct no-pipeline dispatch. Derivations own payload deserialization, <see cref="CanHandle"/>,
/// and the body; commit semantics stay entirely theirs.
/// </summary>
public abstract class RuntimeSchedulerWorkHandlerBase<TPayload> : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
{
    private readonly IServiceScopeFactory _serviceScopeFactory;

    protected RuntimeSchedulerWorkHandlerBase(IServiceScopeFactory serviceScopeFactory, TimeProvider? timeProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceScopeFactory);

        _serviceScopeFactory = serviceScopeFactory;
        TimeProvider = timeProvider ?? System.TimeProvider.System;
    }

    protected TimeProvider TimeProvider { get; }

    public abstract string Name { get; }

    public abstract bool CanHandle(RuntimeSchedulerWorkItem workItem);

    /// <summary>Direct (no-pipeline) dispatch: runs against a fresh scope.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        await ExecuteAsync(workItem, ambientServices: null, cancellationToken);
    }

    /// <summary>
    /// Pipeline dispatch (Move 2 / RT-7): run in the Invoke slot reading the drain's ambient services from
    /// the workspace (staged explicitly by the dispatcher) instead of an AsyncLocal service locator.
    /// </summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(pipelineContext);
        cancellationToken.ThrowIfCancellationRequested();

        await ExecuteAsync(workItem, pipelineContext.Workspace.AmbientServices, cancellationToken);
    }

    /// <summary>Deserializes and validates the work item's payload; thrown validation errors never open a scope.</summary>
    protected abstract TPayload DeserializePayload(RuntimeSchedulerWorkItem workItem);

    protected abstract ValueTask HandleWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        TPayload payload,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);

    private async ValueTask ExecuteAsync(RuntimeSchedulerWorkItem workItem, IServiceProvider? ambientServices, CancellationToken cancellationToken)
    {
        var payload = DeserializePayload(workItem);
        if (ambientServices is { } provider)
        {
            await HandleWithServicesAsync(workItem, payload, provider, cancellationToken);
            return;
        }

        await using var scope = _serviceScopeFactory.CreateAsyncScope();
        await HandleWithServicesAsync(workItem, payload, scope.ServiceProvider, cancellationToken);
    }
}
