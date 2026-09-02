using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

internal sealed class PersistenceOperationScopeFactory(IServiceScopeFactory scopeFactory)
    : IPersistenceOperationScopeFactory
{
    public async ValueTask<PersistenceOperationScope> CreateAsync(
        PersistenceScope persistenceScope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(persistenceScope);
        cancellationToken.ThrowIfCancellationRequested();

        var scope = scopeFactory.CreateAsyncScope();
        try
        {
            var transportedContext = PersistenceAccessContext.Scoped(persistenceScope);
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(transportedContext);
            var effectiveContext = scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
            if (effectiveContext != transportedContext)
            {
                throw new InvalidOperationException(
                    "The host persistence access accessor did not accept the explicitly bound persistence scope.");
            }

            return new PersistenceOperationScope(scope);
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }
}
