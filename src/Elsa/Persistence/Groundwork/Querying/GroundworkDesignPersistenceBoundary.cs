using Elsa.Persistence.Core.Design;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>Maps only positively classified Groundwork failures at a public design-adapter boundary.</summary>
internal static class GroundworkDesignPersistenceBoundary
{
    public static bool TryMap(
        Exception exception,
        DesignPersistenceDomain? domain,
        string operation,
        string? context,
        out DesignPersistenceException? mapped)
    {
        mapped = null;
        if (domain is null || exception is OperationCanceledException or DesignPersistenceException)
            return false;

        var failureKind = exception switch
        {
            GroundworkProviderFailureException or GroundworkDesignAtomicWriteProviderFailureException =>
                DesignPersistenceFailureKind.Provider,
            GroundworkCorruptPayloadException or
                GroundworkDesignAtomicWriteSerializationException or
                GroundworkDesignAtomicWriteCorruptResultException or
                GroundworkDesignAtomicWriteCorruptMarkerException =>
                DesignPersistenceFailureKind.Serialization,
            _ => (DesignPersistenceFailureKind?)null
        };
        if (failureKind is null)
            return false;

        var cause = exception switch
        {
            GroundworkProviderFailureException { InnerException: not null } provider => provider.InnerException,
            GroundworkDesignAtomicWriteProviderFailureException { InnerException: not null } provider => provider.InnerException,
            GroundworkCorruptPayloadException { InnerException: not null } payload => payload.InnerException,
            GroundworkDesignAtomicWriteSerializationException { InnerException: not null } serialization => serialization.InnerException,
            GroundworkDesignAtomicWriteCorruptResultException { InnerException: not null } result => result.InnerException,
            GroundworkDesignAtomicWriteCorruptMarkerException { InnerException: not null } marker => marker.InnerException,
            _ => exception is GroundworkCorruptPayloadException or
                    GroundworkDesignAtomicWriteSerializationException or
                    GroundworkDesignAtomicWriteCorruptResultException or
                    GroundworkDesignAtomicWriteCorruptMarkerException
                ? new InvalidDataException(exception.Message)
                : exception
        };
        mapped = new DesignPersistenceException(domain.Value, failureKind.Value, operation, context, cause!);
        return true;
    }
}

/// <summary>Executes an explicitly supplied serialization operation at a design-adapter boundary.</summary>
public static class GroundworkDesignSerialization
{
    public static T Execute<T>(
        DesignPersistenceDomain domain,
        string operation,
        string context,
        Func<T> serialize)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        ArgumentNullException.ThrowIfNull(serialize);
        try
        {
            return serialize();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DesignPersistenceException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new DesignPersistenceException(
                domain,
                DesignPersistenceFailureKind.Serialization,
                operation,
                context,
                exception);
        }
    }
}
