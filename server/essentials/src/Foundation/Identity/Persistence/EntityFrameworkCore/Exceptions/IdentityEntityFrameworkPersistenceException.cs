namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore.Exceptions;

/// <summary>Stable Identity EF persistence failure boundary; provider exception types do not cross it.</summary>
public sealed class IdentityEntityFrameworkPersistenceException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);

/// <summary>The provider acknowledgement was lost and no durable mutation receipt was observed.</summary>
public sealed class IdentityEntityFrameworkUncertainCommitException(string message, Exception innerException)
    : InvalidOperationException(message, innerException);

internal sealed class IdentityEntityFrameworkAdmissionException(string message)
    : InvalidOperationException(message);
