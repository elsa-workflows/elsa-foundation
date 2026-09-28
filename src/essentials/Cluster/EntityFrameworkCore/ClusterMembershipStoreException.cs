using Elsa.Cluster.Core.Exceptions;

namespace Elsa.Cluster.EntityFrameworkCore;

/// <summary>
/// The membership store could not complete a write, or refused one this member may no longer make: the provider failure
/// wrapped at the store boundary (spec 183, FR-034; framework constitution §2.23.5). The message names the operation and
/// the member, never a connection string.
/// </summary>
public sealed class ClusterMembershipStoreException(string message, Exception? innerException = null)
    : ClusterMembershipException(message, innerException);
