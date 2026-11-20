using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
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

        return new PersistenceOperationScope(await scopeFactory.CreateAsyncScopeAsync(persistenceScope));
    }
}
