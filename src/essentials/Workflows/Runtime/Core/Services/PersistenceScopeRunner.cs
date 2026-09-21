using System.Runtime.ExceptionServices;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

internal sealed class PersistenceScopeRunner(
    IPersistenceScopeSource scopeSource,
    IPersistenceOperationScopeFactory operationScopeFactory) : IPersistenceScopeRunner
{
    public async ValueTask RunAsync(
        Func<PersistenceScope, PersistenceOperationScope, CancellationToken, ValueTask> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        var suppliedScopes = await scopeSource.GetScopesAsync(cancellationToken)
            ?? throw new InvalidOperationException("The persistence scope source returned no scope snapshot.");
        var scopes = suppliedScopes.ToArray();
        var uniqueScopes = new HashSet<PersistenceScope>();

        foreach (var scope in scopes)
        {
            if (scope is null)
                throw new InvalidOperationException("The persistence scope source returned a null partition.");
            if (!uniqueScopes.Add(scope))
                throw new InvalidOperationException("The persistence scope source returned a duplicate partition.");
        }

        List<Exception>? failures = null;
        foreach (var scope in scopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var operationScope = await operationScopeFactory.CreateAsync(scope, cancellationToken);
                await operation(scope, operationScope, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is [var single])
            ExceptionDispatchInfo.Capture(single).Throw();
        if (failures is not null)
            throw new AggregateException("Persistence background work failed for multiple partitions.", failures);
    }
}

internal sealed class SinglePersistenceScopeSource(PersistenceScope persistenceScope) : IPersistenceScopeSource
{
    private readonly IReadOnlyList<PersistenceScope> _scopes = [persistenceScope];

    public ValueTask<IReadOnlyList<PersistenceScope>> GetScopesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return new(_scopes);
    }
}
