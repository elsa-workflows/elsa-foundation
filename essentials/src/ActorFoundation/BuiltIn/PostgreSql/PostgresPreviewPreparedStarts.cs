using System.Data;
using Kernel.EntityFramework;
using Kernel.Foundation.Hosting;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Postgres;

public static partial class PostgresPreviewHosting
{
    /// <summary>Creates a private PostgreSQL context that qualifies prepared held starts and managed schedules.</summary>
    public static KernelDb CreatePreparedStartsQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootPreparedStartsQualification(options);

    /// <summary>Installs the PostgreSQL prepared-start qualification profile.</summary>
    public static Task InstallPreparedStartsQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken cancellationToken = default) =>
        InstallPreviewFormatAsync(() => CreatePreparedStartsQualificationContext(options), cancellationToken);

    internal static KernelDb CreateInlineStartsQualificationContext(DbContextOptions<KernelDb> options) =>
        KernelDb.ForPostgresRootInlinePromotionQualification(options);

    internal static Task InstallInlineStartsQualificationAsync(DbContextOptions<KernelDb> options, CancellationToken token) =>
        InstallPreviewFormatAsync(() => CreateInlineStartsQualificationContext(options), token);

    /// <summary>Confirms the exact protected held start under the installed root source.</summary>
    public static async Task<RootStartProof> ConfirmPreparedStartAsync(Func<KernelDb> open, Contract.BoundScope scope,
        Contract.ActorRef actor, string issuance, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(scope);
        try
        {
            await using var db = open();
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
            // Queries used to settle lifetime state must fail if policies would filter committed rows.
            await db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken);
            var (key, bind) = await PostgresRootAdmission.PinRootPreparedStartProofAsync(db, cancellationToken);
            if (RootPreparedStarts.Verify(key, scope, actor, issuance) is not { } stage)
                return RootStartProof.Unresolved;
            var now = await RootEventAdmission.ReadPostgresClockAsync(db, cancellationToken);
            await RootPreparedStarts.ExpireAsync(db, stage, now, cancellationToken, bind);
            var proof = await RootPreparedStarts.ReadProofAsync(db, stage, cancellationToken, bind);
            await transaction.CommitAsync(cancellationToken);
            return proof;
        }
        catch (Exception error) when (!cancellationToken.IsCancellationRequested &&
            error is TimeoutException or NpgsqlException { IsTransient: true })
        {
            return RootStartProof.Unresolved;
        }
    }

    /// <summary>Removes one bounded page of expired held-root preparations for this scope.</summary>
    public static async Task<int> PrunePreparedStartsAsync(Func<KernelDb> open, Contract.BoundScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(open);
        ArgumentNullException.ThrowIfNull(scope);
        await using var db = open();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("SET LOCAL row_security = off", cancellationToken);
        var (_, bind) = await PostgresRootAdmission.PinRootPreparedStartProofAsync(db, cancellationToken);
        var now = await RootEventAdmission.ReadPostgresClockAsync(db, cancellationToken);
        var count = await RootPreparedStarts.PruneAsync(db, scope.Tenant, now, cancellationToken, bind);
        await transaction.CommitAsync(cancellationToken);
        return count;
    }
}
