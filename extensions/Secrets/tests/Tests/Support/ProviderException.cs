using System.Data.Common;

namespace Elsa.Secrets.Tests.Support;

/// <summary>
/// A database provider's failure, as a provider-specific <see cref="DbException"/> is: transient when the provider says
/// so (as Npgsql does for a connection failure), with the SQLSTATE and inner exception the provider gives it.
/// </summary>
internal sealed class ProviderException(string message, bool isTransient = false, string? sqlState = null, Exception? innerException = null)
    : DbException(message, innerException)
{
    public override bool IsTransient => isTransient;

    public override string? SqlState => sqlState;
}
