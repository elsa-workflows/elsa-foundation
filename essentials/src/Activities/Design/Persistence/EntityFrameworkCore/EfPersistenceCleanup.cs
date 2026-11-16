using Microsoft.EntityFrameworkCore.Storage;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore;

internal static class EfPersistenceCleanup
{
    public static async Task RollbackQuietlyAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception exception) when (IsCatchable(exception))
        {
            // Best-effort cleanup must not mask the operation's original exception.
        }
    }

    public static bool IsCatchable(Exception exception) =>
        exception is not (OutOfMemoryException or
            StackOverflowException or
            AccessViolationException or
            AppDomainUnloadedException or
            BadImageFormatException or
            CannotUnloadAppDomainException or
            InvalidProgramException);
}
