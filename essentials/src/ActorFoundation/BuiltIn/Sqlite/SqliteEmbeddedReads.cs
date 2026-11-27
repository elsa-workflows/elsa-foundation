using Kernel.EntityFramework;
using Microsoft.Data.Sqlite;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

public static partial class SqliteEmbeddedHosting
{
    internal static Contract.IActorReads CreateFoundationReads(string databasePath, int pageSize = 100, StoreFormat? qualificationFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentOutOfRangeException.ThrowIfLessThan(pageSize, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(pageSize, int.MaxValue);
        using var file = AcquireFile(databasePath);
        return new SqliteEmbeddedReads(file.Path, pageSize, qualificationFormat);
    }

    private sealed class SqliteEmbeddedReads(string path, int pageSize, StoreFormat? qualificationFormat)
        : Contract.IActorReads, Contract.IActorTerminalOutputReads, Contract.IActorSequencedJournalReads, Contract.IActorPlaybackReads, Contract.IActorRetentionConfirmation,
          Contract.IActorRetentionReads
    {
        public bool SupportsSequencedJournal => qualificationFormat is null
            ? TakesSequencedJournal
            : qualificationFormat is { } format &&
                (format == StoreFormat.RootSequencedJournalQualification || format == StoreFormat.RootLiveEventQualification || SupportsPlayback);

        public ValueTask<string> ProtectRetentionQuoteAsync(Contract.BoundScope scope, ReadOnlyMemory<byte> quote,
            CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ProtectRetentionQuoteAsync(scope, quote, cancellationToken), cancellationToken, normalizeLockTimeout: true);

        public ValueTask<byte[]?> UnprotectRetentionQuoteAsync(Contract.BoundScope scope, string confirmation,
            CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.UnprotectRetentionQuoteAsync(scope, confirmation, cancellationToken), cancellationToken, normalizeLockTimeout: true);

        public bool SupportsPlayback => qualificationFormat == StoreFormat.RootPlaybackQualification;

        public ValueTask<Contract.ActorPlaybackPage?> ReadPlaybackAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.ActorPlaybackQuery query, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ReadPlaybackAsync(scope, actor, query, cancellationToken), cancellationToken, normalizeLockTimeout: true);

        public ValueTask<Contract.ActorSequencedJournalPage?> ReadSequencedJournalAsync(Contract.BoundScope scope,
            Contract.ActorRef actor, long afterSequence, long? upperSequence, int limit, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ReadSequencedJournalAsync(scope, actor, afterSequence, upperSequence, limit, cancellationToken),
                cancellationToken, normalizeLockTimeout: true);

        public ValueTask<Contract.ActorRetentionQuote> ReadRetentionQuoteAsync(Contract.BoundScope scope, string actorType,
            Contract.ActorRetentionWindows previous, Contract.ActorRetentionWindows proposed,
            KeyValuePair<string, string>? definitionLabel = null, long? asOf = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ReadRetentionQuoteAsync(scope, actorType, previous, proposed, definitionLabel, asOf, cancellationToken),
                cancellationToken, normalizeLockTimeout: true);

        public ValueTask<Contract.ActorTerminalOutput?> ReadTerminalOutputAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            long endedAt, bool includeValues, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ReadTerminalOutputAsync(scope, actor, endedAt, includeValues, cancellationToken), cancellationToken);

        public ValueTask<Contract.ActorView?> GetAsync(Contract.BoundScope scope, Contract.ActorRef actor, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.GetAsync(scope, actor, cancellationToken), cancellationToken, normalizeLockTimeout: SupportsSequencedJournal);

        public ValueTask<Contract.Page<Contract.DeliveryView>> InboxAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.InboxAsync(scope, actor, from, cancellationToken), cancellationToken);

        public ValueTask<Contract.Page<Contract.ActorView>> ListAsync(Contract.BoundScope scope, Contract.ActorQuery query,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.ListAsync(scope, query, from, cancellationToken), cancellationToken);

        public ValueTask<Contract.Page<Contract.JournalEntry>> TailAsync(Contract.BoundScope scope, Contract.ActorRef actor,
            Contract.PageToken? from = null, CancellationToken cancellationToken = default) =>
            ReadAsync(reads => reads.TailAsync(scope, actor, from, cancellationToken), cancellationToken);

        private async ValueTask<T> ReadAsync<T>(Func<GeneratedRootReads, ValueTask<T>> read, CancellationToken token,
            bool normalizeLockTimeout = false)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var file = AcquireFile(path);
                KernelDb Open() => KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions, qualificationFormat);
                await using var db = Open();
                await EfStore.RequireSqliteRootAdmissionAsync(db, file.AdmissionDefinitions, token);
                var cursor = await SqliteRootAdmission.ReadSelectionCursorAsync(db, token);
                var store = new EfStore(Open, postgres: false, fenceInBatch: true);
                return await read(new GeneratedRootReads(store, cursor, pageSize, provider: "sqlite"));
            }
            catch (SqliteException error) when (normalizeLockTimeout && error.SqliteErrorCode is 5 or 6)
            {
                token.ThrowIfCancellationRequested();
                throw new TimeoutException("The SQLite reader could not acquire its read lock.", error);
            }
        }
    }
}
