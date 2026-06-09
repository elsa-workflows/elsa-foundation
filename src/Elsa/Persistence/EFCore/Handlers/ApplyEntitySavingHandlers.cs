using Elsa.Events.Core.Contracts;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EFCore.Handlers;

/// <summary>
/// The single <see cref="OnEntitySaving"/> handler — the write-side mirror of the
/// <c>ExecuteValidations</c> aggregator. Resolves every registered
/// <see cref="IEntitySavingHandler{TDbContext,TEntity}"/> closed over the runtime DbContext +
/// entity types and invokes each against the entity being saved. Replaces the former
/// per-handler <see cref="IEventHandler{T}"/> sprawl and the legacy reflection loop that lived
/// on <c>ElsaDbContextBase</c>; features now contribute by registering a typed
/// <see cref="IEntitySavingHandler{TDbContext,TEntity}"/>.
/// </summary>
public sealed class ApplyEntitySavingHandlers(IServiceProvider serviceProvider) : IEventHandler<OnEntitySaving>
{
    public async Task Handle(OnEntitySaving domainEvent, CancellationToken cancellationToken)
    {
        var dbContext = domainEvent.DbContext;
        var entity = domainEvent.Entry.Entity;

        var handlerType = typeof(IEntitySavingHandler<,>).MakeGenericType(dbContext.GetType(), entity.GetType());
        var method = handlerType.GetMethod(nameof(IEntitySavingHandler<,>.Handle));
        if (method is null)
            return;

        foreach (var handler in serviceProvider.GetServices(handlerType))
        {
            var task = (ValueTask)method.Invoke(handler, [dbContext, entity, cancellationToken])!;
            await task;
        }
    }
}