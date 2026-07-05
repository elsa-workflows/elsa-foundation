using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Pins the published <c>ExpectedVersion</c> semantics matrix (Groundwork spec 014 amendment, 2026-07-05) against
/// BOTH the shared <see cref="Elsa.Persistence.Groundwork.Testing.InMemoryDocumentStore"/> test double and the real
/// SQLite provider, so the two can never silently diverge again. The Wave-B W27 incident was caused by exactly such
/// a divergence: the double supported create-only writes the providers did not, letting tests overstate provider
/// guarantees. Every cell of the matrix runs on every provider in this suite.
/// </summary>
/// <remarks>
/// Matrix (save): null = unconditional upsert; 0 = create-only (Saved v1 when absent, ConcurrencyConflict when
/// present); n>0 = CAS update (Saved on version match, ConcurrencyConflict on mismatch, NotFound when absent —
/// nothing written in the failure cases). Delete: null deletes unconditionally; any value CAS-deletes on version
/// match; absent is NotFound regardless (0 has no special meaning for delete).
/// </remarks>
public sealed class DocumentStoreCasSemanticsTests
{
    private const string Kind = ElsaRuntimeStorageManifest.DurableTimerDocumentKind;
    private const string Content = /*lang=json,strict*/ """{"collection":"durableTimer"}""";

    public static TheoryData<string> Providers => new() { "memory", "sqlite" };

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task ExpectedVersionZero_IsCreateOnly(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = fixture.DocumentStore;

        var created = await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-1", "1.0.0", Content, ExpectedVersion: 0));
        Assert.Equal(DocumentStoreWriteStatus.Saved, created.Status);
        Assert.Equal(1, created.Document!.Version);

        var refused = await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-1", "1.0.0", Content, ExpectedVersion: 0));
        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, refused.Status);
        Assert.Equal(1, (await store.LoadAsync(Kind, "cas-1"))!.Version);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task PositiveExpectedVersion_IsCasUpdate_AndNotFoundWhenAbsent(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = fixture.DocumentStore;

        // Absent + positive expectation: NotFound, nothing written.
        var missing = await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-2", "1.0.0", Content, ExpectedVersion: 3));
        Assert.Equal(DocumentStoreWriteStatus.NotFound, missing.Status);
        Assert.Null(await store.LoadAsync(Kind, "cas-2"));

        // Version match updates and increments; the stale retry conflicts and mutates nothing.
        await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-2", "1.0.0", Content));
        var updated = await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-2", "1.0.0", Content, ExpectedVersion: 1));
        Assert.Equal(DocumentStoreWriteStatus.Saved, updated.Status);
        Assert.Equal(2, updated.Document!.Version);

        var stale = await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-2", "1.0.0", Content, ExpectedVersion: 1));
        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
        Assert.Equal(2, (await store.LoadAsync(Kind, "cas-2"))!.Version);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task NullExpectedVersion_IsUnconditionalUpsert(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = fixture.DocumentStore;

        var first = await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-3", "1.0.0", Content));
        Assert.Equal(DocumentStoreWriteStatus.Saved, first.Status);
        Assert.Equal(1, first.Document!.Version);

        var second = await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-3", "1.0.0", Content));
        Assert.Equal(DocumentStoreWriteStatus.Saved, second.Status);
        Assert.Equal(2, second.Document!.Version);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Delete_HonoursCas_AndAbsentIsNotFoundRegardlessOfExpectation(string provider)
    {
        await using var fixture = GroundworkDocumentStoreFixture.Create(provider);
        var store = fixture.DocumentStore;

        // Absent: NotFound for null and for 0 — delete has no create-only analogue.
        Assert.Equal(DocumentStoreWriteStatus.NotFound, (await store.DeleteAsync(new DeleteDocumentRequest(Kind, "cas-4"))).Status);
        Assert.Equal(DocumentStoreWriteStatus.NotFound, (await store.DeleteAsync(new DeleteDocumentRequest(Kind, "cas-4", ExpectedVersion: 0))).Status);

        await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-4", "1.0.0", Content));
        await store.SaveAsync(new SaveDocumentRequest(Kind, "cas-4", "1.0.0", Content, ExpectedVersion: 1));

        var stale = await store.DeleteAsync(new DeleteDocumentRequest(Kind, "cas-4", ExpectedVersion: 1));
        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
        Assert.NotNull(await store.LoadAsync(Kind, "cas-4"));

        var deleted = await store.DeleteAsync(new DeleteDocumentRequest(Kind, "cas-4", ExpectedVersion: 2));
        Assert.Equal(DocumentStoreWriteStatus.Deleted, deleted.Status);
        Assert.Null(await store.LoadAsync(Kind, "cas-4"));
    }
}
