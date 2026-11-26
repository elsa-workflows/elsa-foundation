using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class PublicationOriginSnapshotTests
{
    [Fact]
    public async Task Actual_journal_snapshot_copies_publication_identity_and_ignores_unbounded_failure_text()
    {
        await using var owner = await Owner.Create(); var record = Record(); await owner.Store.SaveAsync(record);
        await owner.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_publication_records SET FailureMessage={new string('x', 100_000)}");
        var actual = await owner.Store.FindOriginBoundedAsync(record.PublicationId, new("partition"), new() { MaxMetadataBytes = 512 });
        Assert.NotNull(actual); Assert.Equal(record.PublicationId, actual.PublicationId); Assert.Equal(record.SourceReferenceId, actual.SourceReferenceId);
        Assert.Equal(record.SlotId, actual.SlotId); Assert.Equal(record.WorkflowDefinitionVersionId, actual.WorkflowDefinitionVersionId);
        Assert.Equal(record.ArtifactId, actual.ArtifactId); Assert.Equal(record.Status, actual.Status); Assert.Equal(record.ActivatedAt, actual.ActivatedAt);
        Assert.InRange(actual.StoredMetadataBytes, 1, 8192);
    }

    [Theory]
    [InlineData("size", "publication_source_stored_byte_limit", false)]
    [InlineData("utf8", "publication_source_stored_content_invalid", false)]
    [InlineData("schema", "publication_source_schema_unsupported", true)]
    [InlineData("ticks", "publication_source_stored_content_invalid", false)]
    public async Task Raw_journal_metadata_must_pass_type_byte_schema_and_clock_pair_checks(string change, string code, bool unsupported)
    {
        await using var owner = await Owner.Create(); await owner.Store.SaveAsync(Record());
        if (change == "size") await owner.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_publication_records SET ArtifactId={new string('x', 8192)}");
        else await owner.Context.Database.ExecuteSqlRawAsync(change switch
        {
            "utf8" => "UPDATE elsa_publication_records SET ArtifactId=CAST(X'FF' AS TEXT)",
            "schema" => "UPDATE elsa_publication_records SET SchemaVersion='future'",
            _ => "UPDATE elsa_publication_records SET ActivatedAtOffsetMinutes=NULL"
        });
        var error = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => owner.Store.FindOriginBoundedAsync("publication", new("partition"), new()).AsTask());
        Assert.Equal(code, error.Code); Assert.Equal(unsupported, error.Unsupported);
    }

    [Fact]
    public async Task Scope_mismatch_and_caller_transaction_are_refused_without_disclosing_a_row()
    {
        await using var owner = await Owner.Create(); await owner.Store.SaveAsync(Record());
        Assert.Equal("publication_source_scope_mismatch", (await Assert.ThrowsAsync<RuntimeSourceReadException>(() =>
            owner.Store.FindOriginBoundedAsync("publication", new("other"), new()).AsTask())).Code);
        await using var transaction = await owner.Context.Database.BeginTransactionAsync();
        var error = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => owner.Store.FindOriginBoundedAsync("publication", new("partition"), new()).AsTask());
        Assert.True(error.Unsupported); Assert.Same(transaction, owner.Context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Ordinary_publication_provider_does_not_implicitly_gain_bounded_source_authority()
    {
        IPublicationRecordStore store = new Ordinary();
        var error = await Assert.ThrowsAsync<RuntimeSourceReadException>(() => store.FindOriginBoundedAsync("publication", new("partition"), new()).AsTask());
        Assert.True(error.Unsupported);
    }
    [Fact]
    public async Task Actual_provider_failure_keeps_its_cause_instead_of_becoming_a_content_refusal()
    {
        await using var owner = await Owner.Create(); await owner.Context.Database.ExecuteSqlRawAsync("DROP TABLE elsa_publication_records");
        var error = await Assert.ThrowsAsync<PublicationSourceSnapshotPersistenceException>(() => owner.Store.FindOriginBoundedAsync("publication", new("partition"), new()).AsTask());
        Assert.IsType<SqliteException>(error.InnerException); Assert.Equal("publication", error.PublicationId); Assert.Equal(new("partition"), error.Scope);
    }

    private sealed class Ordinary : IPublicationRecordStore
    {
        public ValueTask SaveAsync(PublicationRecord publication, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<PublicationRecord?> FindAsync(string publicationId, CancellationToken cancellationToken = default) => ValueTask.FromResult<PublicationRecord?>(null);
        public ValueTask<IReadOnlyCollection<PublicationRecord>> ListBySlotAsync(string slotId, CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<PublicationRecord>>([]);
        public ValueTask<bool> TryTransitionAsync(PublicationRecord publication, PublicationStatus expectedStatus, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }
    private static PublicationRecord Record() => new("publication", "slot", "definition", "version", "artifact", "reference", 0,
        PublicationStatus.Active, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);
    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("partition")); }
    private sealed class Owner : IAsyncDisposable
    {
        private SqliteConnection _connection = null!;
        public PublishingSnapshotReviewSqliteDbContext Context { get; private set; } = null!;
        public EfPublicationRecordStore Store { get; private set; } = null!;
        public static async Task<Owner> Create()
        {
            var owner = new Owner { _connection = new SqliteConnection("Data Source=:memory:;Pooling=False") }; await owner._connection.OpenAsync();
            owner.Context = new(new DbContextOptionsBuilder<PublishingSnapshotReviewSqliteDbContext>().UseSqlite(owner._connection).Options);
            await owner.Context.Database.EnsureCreatedAsync(); owner.Store = new(owner.Context, new Accessor()); return owner;
        }
        public async ValueTask DisposeAsync() { await Context.DisposeAsync(); await _connection.DisposeAsync(); }
    }
}
