namespace Elsa.Primitives.Exceptions;

/// <summary>
/// Thrown when an entity requested by identifier does not exist. First-party APIs map this to a
/// <c>404 Not Found</c> response, so a lookup miss surfaces as a not-found status rather than a generic 500
/// (issue #393). Stores and lookups throw it when a get-by-id yields nothing.
/// </summary>
public sealed class EntityNotFoundException : Exception
{
    public EntityNotFoundException()
    {
    }

    public EntityNotFoundException(string? message) : base(message)
    {
    }

    public EntityNotFoundException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for a missing entity of <paramref name="entityType"/> with the given <paramref name="id"/>.</summary>
    public static EntityNotFoundException ForEntity(Type entityType, string id)
    {
        ArgumentNullException.ThrowIfNull(entityType);
        return new($"Entity '{entityType}' with id '{id}' cannot be found");
    }
}
