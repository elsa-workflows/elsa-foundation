namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>Stable Identity EF persistence failure boundary; provider exception types do not cross it.</summary>
public sealed class IdentityEntityFrameworkPersistenceException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);
