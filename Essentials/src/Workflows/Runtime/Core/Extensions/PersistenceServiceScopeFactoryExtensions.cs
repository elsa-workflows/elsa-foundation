using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Extensions;

public static class PersistenceServiceScopeFactoryExtensions
{
    /// <summary>
    /// Creates a dependency-injection scope whose ordinary persistence context is bound to
    /// <paramref name="persistenceScope"/>. Without one the scope is left unbound, so it carries the host's own
    /// persistence scope: a caller that knows which partition its work belongs to must pass it.
    /// </summary>
    public static async ValueTask<AsyncServiceScope> CreateAsyncScopeAsync(
        this IServiceScopeFactory scopeFactory,
        PersistenceScope? persistenceScope)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        var scope = scopeFactory.CreateAsyncScope();
        if (persistenceScope is null)
            return scope;

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

            return scope;
        }
        catch
        {
            await scope.DisposeAsync();
            throw;
        }
    }
}
