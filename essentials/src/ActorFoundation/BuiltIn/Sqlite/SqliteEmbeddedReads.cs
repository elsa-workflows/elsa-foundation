using Kernel.EntityFramework;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

public static partial class SqliteEmbeddedHosting
{
    internal static Contract.IActorReads CreateFoundationReads(string databasePath, int pageSize = 100)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(pageSize, int.MaxValue);
        return new SqliteEmbeddedReads(Path.GetFullPath(databasePath), pageSize);
    }

    private sealed class SqliteEmbeddedReads(string path, int pageSize) : Contract.IActorReads
    {
        public ValueTask<Contract.ActorView?> GetAsync(Contract.BoundScope scope, Contract.ActorRef actor, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.GetAsync(scope, actor, cancellationToken), cancellationToken);

        public ValueTask<Contract.Page<Contract.DeliveryView>> InboxAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.InboxAsync(scope, actor, from, cancellationToken), cancellationToken);

        public ValueTask<Contract.Page<Contract.ActorView>> ListAsync(Contract.BoundScope scope, Contract.ActorQuery query,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SQLite actor lists are outside the Park inspection slice.");

        public ValueTask<Contract.Page<Contract.JournalEntry>> TailAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("SQLite journal reads are outside the Park inspection slice.");

        private async ValueTask<T> ReadAsync<T>(Func<GeneratedRootReads, ValueTask<T>> read, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            using var file = AcquireFile(path);
            KernelDb Open() => KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions);
            await using var db = Open();
            await EfStore.RequireSqliteRootAdmissionAsync(db, token);
            await file.AdmissionDefinitions.RequireAsync(db, token);
            var cursor = await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
            var store = new EfStore(Open, postgres: false, fenceInBatch: true);
            return await read(new GeneratedRootReads(store, cursor, pageSize, provider: "sqlite"));
        }
    }
}
