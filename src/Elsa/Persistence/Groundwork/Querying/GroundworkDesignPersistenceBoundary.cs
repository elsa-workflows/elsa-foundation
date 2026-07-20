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
            GroundworkProviderFailureException or DesignWriteProviderException =>
                DesignPersistenceFailureKind.Provider,
            GroundworkCorruptPayloadException or
                DesignWriteSerializationException or
                CorruptDesignResultException or
                CorruptDesignMarkerException =>
                DesignPersistenceFailureKind.Serialization,
            _ => (DesignPersistenceFailureKind?)null
        };
        if (failureKind is null)
            return false;

        var cause = exception switch
        {
            GroundworkProviderFailureException { InnerException: not null } provider => provider.InnerException,
            DesignWriteProviderException { InnerException: not null } provider => provider.InnerException,
            GroundworkCorruptPayloadException { InnerException: not null } payload => payload.InnerException,
            DesignWriteSerializationException { InnerException: not null } serialization => serialization.InnerException,
            CorruptDesignResultException { InnerException: not null } result => result.InnerException,
            CorruptDesignMarkerException { InnerException: not null } marker => marker.InnerException,
            _ => exception is GroundworkCorruptPayloadException or
                    DesignWriteSerializationException or
                    CorruptDesignResultException or
                    CorruptDesignMarkerException
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
