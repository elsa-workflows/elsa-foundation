using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Transactions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

/// <summary>
/// Opens one immutable, explicitly global session only after the selected
/// provider has published cross-unit atomic admission evidence.
/// </summary>
public sealed class OpenIddictGroundworkStoreSessionFactory(
    GroundworkStoreSessionSource admittedSource,
    IGroundworkStoreSessionFactory sessions)
{
    public async ValueTask<GroundworkStoreSession> CreateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!admittedSource.IsInitialized)
        {
            throw new OpenIddictGroundworkReadinessException(
                "No admitted Groundwork provider publication is available for OpenIddict storage.");
        }

        if (admittedSource.AdmittedTransactionBoundary is not TransactionBoundary.CrossUnitAtomic)
        {
            throw new OpenIddictGroundworkReadinessException(
                "OpenIddict storage requires an admitted cross-unit atomic transaction boundary.");
        }

        try
        {
            return await sessions.CreateOrdinaryGlobalAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OpenIddictGroundworkProviderException("session.open", exception);
        }
    }

    internal static async ValueTask DisposeAsync(
        GroundworkStoreSession session,
        string operation)
    {
        ArgumentNullException.ThrowIfNull(session);
        try
        {
            await session.DisposeAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new OpenIddictGroundworkProviderException($"{operation}.dispose", exception);
        }
    }
}
