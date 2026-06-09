using Elsa.Events.Core.Contracts;
using Elsa.Persistence.EFCore.Contracts;
using Elsa.Persistence.EFCore.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.EFCore.Handlers
{
    /// <summary>
    /// The single <see cref="OnEntityLoading"/> handler — the read-side mirror of
    /// <see cref="ApplyEntitySavingHandlers"/>. Resolves every registered
    /// <see cref="IEntityLoadingHandler{TDbContext,TEntity}"/> closed over the runtime DbContext +
    /// entity types and invokes each to hydrate the materialised entity. Replaces the legacy
    /// per-entity dispatch loop that lived on <c>EFCoreQueries</c>; features now contribute by
    /// registering a typed <see cref="IEntityLoadingHandler{TDbContext,TEntity}"/>.
    /// </summary>
    public sealed class ApplyEntityLoadingHandlers(IServiceProvider serviceProvider) : IEventHandler<OnEntityLoading>
    {
        public async Task Handle(OnEntityLoading domainEvent, CancellationToken cancellationToken)
        {
            var dbContext = domainEvent.DbContext;
            var entity = domainEvent.Entity;

            var handlerType = typeof(IEntityLoadingHandler<,>).MakeGenericType(dbContext.GetType(), entity.GetType());
            var method = handlerType.GetMethod(nameof(IEntityLoadingHandler<,>.Handle));
            if (method is null)
                return;

            foreach (var handler in serviceProvider.GetServices(handlerType))
            {
                var task = (ValueTask)method.Invoke(handler, [dbContext, entity, cancellationToken])!;
                await task;
            }
        }
    }
}
