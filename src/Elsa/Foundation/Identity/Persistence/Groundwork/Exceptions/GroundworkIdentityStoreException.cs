namespace Elsa.Foundation.Identity.Persistence.Groundwork.Exceptions;

public class GroundworkIdentityStoreException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class GroundworkIdentityReadinessException(string message, Exception? innerException = null) : GroundworkIdentityStoreException(message, innerException);

public sealed class GroundworkIdentitySerializationException(string message, Exception? innerException = null) : GroundworkIdentityStoreException(message, innerException);

public sealed class GroundworkIdentityConflictException(string message, Exception? innerException = null) : GroundworkIdentityStoreException(message, innerException);

public sealed class GroundworkIdentityCorruptStateException(string message, Exception? innerException = null) : GroundworkIdentityStoreException(message, innerException);

public sealed class GroundworkIdentityUncertainCommitException(string message, Exception? innerException = null) : GroundworkIdentityStoreException(message, innerException);
