using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.WorkHandlers;

/// <summary>
/// Dispatch scaffold shared by the payload-typed scheduler work handlers: deserialize the work item's
/// payload once, then run the handler body either against the pipeline's ambient services (staged
/// explicitly by the dispatcher, replacing an earlier AsyncLocal service locator) or against a fresh
/// scope. A fresh scope is bound to the partition the dispatcher staged, so the body works in the partition
/// the work item belongs to; direct no-pipeline dispatch knows no partition and its scope carries the host's.
/// Derivations own payload deserialization, <see cref="CanHandle"/>, and the body; commit semantics stay
/// entirely theirs.
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

    /// <summary>Direct (no-pipeline) dispatch: runs against a fresh scope that carries the host's persistence scope.</summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        await ExecuteAsync(workItem, ambientServices: null, persistenceScope: null, cancellationToken);
    }

    /// <summary>
    /// Pipeline dispatch (Move 2): run in the Invoke slot reading the drain's ambient services from
    /// the workspace (staged explicitly by the dispatcher) instead of an AsyncLocal service locator, or, when the
    /// drain carried none, against a fresh scope bound to the partition the dispatcher staged.
    /// </summary>
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, IRuntimePipelineContext pipelineContext, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        ArgumentNullException.ThrowIfNull(pipelineContext);
        cancellationToken.ThrowIfCancellationRequested();

        await ExecuteAsync(workItem, pipelineContext.Workspace.AmbientServices, pipelineContext.Workspace.PersistenceScope, cancellationToken);
    }

    /// <summary>Deserializes and validates the work item's payload; thrown validation errors never open a scope.</summary>
    protected abstract TPayload DeserializePayload(RuntimeSchedulerWorkItem workItem);

    protected abstract ValueTask HandleWithServicesAsync(
        RuntimeSchedulerWorkItem workItem,
        TPayload payload,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken);

    private async ValueTask ExecuteAsync(
        RuntimeSchedulerWorkItem workItem,
        IServiceProvider? ambientServices,
        PersistenceScope? persistenceScope,
        CancellationToken cancellationToken)
    {
        var payload = DeserializePayload(workItem);
        if (ambientServices is { } provider)
        {
            await HandleWithServicesAsync(workItem, payload, provider, cancellationToken);
            return;
        }

        await using var scope = await _serviceScopeFactory.CreateAsyncScopeAsync(persistenceScope);
        await HandleWithServicesAsync(workItem, payload, scope.ServiceProvider, cancellationToken);
    }
}
