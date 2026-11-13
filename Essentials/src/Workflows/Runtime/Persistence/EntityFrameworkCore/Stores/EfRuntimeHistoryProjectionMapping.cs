using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.ActivityExecutionEfSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

/// <summary>
/// Checked lossless projections for the provider under construction. The eventual owner supplies its
/// declared schema chain; this helper neither declares a substitute family nor registers a reader.
/// </summary>
internal sealed class EfRuntimeHistoryProjectionMapping(EfSchemaChain chain)
{
    internal static string Id(string kind, string scope, string run, string subject = "") => CreateId("history:" + kind, scope, run, subject);
    internal static string CountSubject(string? activity) => activity is null ? "run" : "activity:" + activity;

    internal RuntimeHistoryHead Read(RuntimeHistoryHeadEntity row, string scope, string run) => Checked(run, () =>
    {
        Envelope(row.SchemaVersion, row.Id, Id("head", scope, run), row.ScopeKey, row.ScopeKeyHash,
            row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.Revision, scope, run);
        var item = Content<RuntimeHistoryHeadEntity, RuntimeHistoryHead>(row.SchemaVersion, row.ContentJson);
        ValidateHead(run, item);
        if (row.FirstSequence != item.FirstSequence || row.LastSequence != item.LastSequence || row.PurgedThroughSequence != item.PurgedThroughSequence)
            throw Corrupt(run, "the sequence head projections disagree");
        return item;
    });

    internal EventReceipt ReadReceipt(RuntimeHistoryEventEntity row, string scope, string run, RuntimeHistoryHead? head) => Checked(run, () =>
    {
        // Settle schema compatibility before interpreting any stamped identity or content.
        EfSchemaVersion.EnsureReadable(chain, row.SchemaVersion);
        var eventId = Decode(row.HistoryEventId);
        Envelope(row.SchemaVersion, row.Id, Id("event", scope, run, eventId), row.ScopeKey, row.ScopeKeyHash,
            row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.Revision, scope, run);
        ValidateHead(run, head);
        if (string.IsNullOrWhiteSpace(eventId) || row.HistoryEventIdHash != Hash(eventId) || !ValidFingerprint(row.CandidateFingerprint) ||
            head is null || row.Sequence <= 0 || row.Sequence > head.LastSequence ||
            row.IsPurged != (row.Sequence <= head.PurgedThroughSequence) || !row.IsPurged && row.Sequence < head.FirstSequence)
            throw Corrupt(run, "an event receipt has invalid identity, fingerprint or acknowledged position");
        return new(eventId, row.Sequence, row.CandidateFingerprint, row.IsPurged);
    });

    internal RuntimeHistoryEvent Read(RuntimeHistoryEventEntity row, string scope, string run, RuntimeHistoryHead? head) => Checked(run, () =>
    {
        var receipt = ReadReceipt(row, scope, run, head);
        if (receipt.IsPurged) throw Corrupt(run, "purged event content was requested as retained content");
        var item = Content<RuntimeHistoryEventEntity, RuntimeHistoryEvent>(row.SchemaVersion, row.ContentJson);
        if (item.HistoryEventId != receipt.HistoryEventId || item.WorkflowExecutionId != run || item.Sequence != receipt.Sequence ||
            string.IsNullOrWhiteSpace(item.PhysicalCommitId) || item.RecordedAt is null || item.Payload is not null ||
            row.Name != Encode(item.Name) || row.NameHash != Hash(item.Name) || row.Category != (int)item.Category ||
            row.ActivityExecutionId != Encoded(item.ActivityExecutionId) || row.ActivityExecutionIdHash != Hashed(item.ActivityExecutionId) ||
            row.BookmarkId != Encoded(item.BookmarkId) || row.BookmarkIdHash != Hashed(item.BookmarkId) ||
            item.Metadata.Count > 16 || item.Metadata.Any(pair => !RuntimeHistorySafety.IsSafeMetadataKey(pair.Key) || pair.Value is null || pair.Value.Length > 2048))
            throw Corrupt(run, "an event has corrupt content, protected payload or filter projections");
        // Unknown event vocabulary/schema remains readable as recorded; only known owners may interpret it.
        return item;
    });

    internal RuntimeHistoryBookmark Read(RuntimeHistoryBookmarkEntity row, string scope, string run, RuntimeHistoryHead? head) => Checked(run, () =>
    {
        EfSchemaVersion.EnsureReadable(chain, row.SchemaVersion);
        var bookmarkId = Decode(row.BookmarkId);
        Envelope(row.SchemaVersion, row.Id, Id("bookmark", scope, run, bookmarkId), row.ScopeKey, row.ScopeKeyHash,
            row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.Revision, scope, run);
        var item = Content<RuntimeHistoryBookmarkEntity, RuntimeHistoryBookmark>(row.SchemaVersion, row.ContentJson);
        ValidateHead(run, head);
        if (item.Bookmark?.Identity is not { } identity || identity.WorkflowExecutionId != run || identity.BookmarkId != bookmarkId ||
            string.IsNullOrWhiteSpace(identity.ActivityExecutionId) || string.IsNullOrWhiteSpace(identity.ResumeTargetId) ||
            row.BookmarkIdHash != Hash(bookmarkId) || row.ActivityExecutionId != Encode(identity.ActivityExecutionId) ||
            row.ActivityExecutionIdHash != Hash(identity.ActivityExecutionId) || row.Status != (int)item.Status ||
            !Enum.IsDefined(item.Status) || !Enum.IsDefined(item.PriorState) || string.IsNullOrWhiteSpace(item.LastHistoryEventId) ||
            row.FirstRecordedSequence != item.FirstRecordedSequence || item.FirstRecordedSequence <= 0 || head is null ||
            item.LastSequence < item.FirstRecordedSequence || item.LastSequence > head.LastSequence)
            throw Corrupt(run, "a bookmark lifecycle has corrupt ownership, status or position");
        if (item.FirstRecordedSequence <= head.PurgedThroughSequence)
            item = item with { CreatedHistoryEventId = null,
                PriorState = item.PriorState == RuntimeHistoryCreationEvidence.CreationEvent ? RuntimeHistoryCreationEvidence.CreationMetadata : item.PriorState,
                EndedHistoryEventId = item.LastSequence <= head.PurgedThroughSequence ? null : item.EndedHistoryEventId };
        return item;
    });

    internal ActivityParent Read(RuntimeHistoryActivityEntity row, string scope, string run, string activity) => Checked(run, () =>
    {
        Envelope(row.SchemaVersion, row.Id, Id("activity", scope, run, activity), row.ScopeKey, row.ScopeKeyHash,
            row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.Revision, scope, run);
        var item = Content<RuntimeHistoryActivityEntity, ActivityParent>(row.SchemaVersion, row.ContentJson);
        if (item.ActivityExecutionId != activity || string.IsNullOrWhiteSpace(activity) || row.ActivityExecutionId != Encode(activity) ||
            row.ActivityExecutionIdHash != Hash(activity) || row.ParentActivityExecutionId != Encoded(item.ParentActivityExecutionId) ||
            item.ParentActivityExecutionId == activity || item.ParentActivityExecutionId is { } parent && string.IsNullOrWhiteSpace(parent))
            throw Corrupt(run, "an ancestry index has corrupt ownership or parent facts");
        return item;
    });

    internal BookmarkCount Read(RuntimeHistoryBookmarkCountEntity row, string scope, string run, string? activity) => Checked(run, () =>
    {
        var subject = CountSubject(activity);
        Envelope(row.SchemaVersion, row.Id, Id("count", scope, run, subject), row.ScopeKey, row.ScopeKeyHash,
            row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.Revision, scope, run);
        var item = Content<RuntimeHistoryBookmarkCountEntity, BookmarkCount>(row.SchemaVersion, row.ContentJson);
        if (row.ActivityExecutionId != Encoded(activity) || row.ActivityExecutionIdHash != Hash(subject) || item.ActivityExecutionId != activity ||
            row.TotalCount != item.TotalCount || row.ActiveCount != item.ActiveCount || item.ActiveCount < 0 || item.TotalCount < item.ActiveCount)
            throw Corrupt(run, "bookmark counts have corrupt ownership or values");
        return item;
    });

    internal RuntimeHistoryHeadEntity Row(string scope, RuntimeHistoryHead item)
    {
        ValidateHead(item.WorkflowExecutionId, item);
        return new() { Id = Id("head", scope, item.WorkflowExecutionId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
            WorkflowExecutionId = Encode(item.WorkflowExecutionId), WorkflowExecutionIdHash = Hash(item.WorkflowExecutionId),
            FirstSequence = item.FirstSequence, LastSequence = item.LastSequence, PurgedThroughSequence = item.PurgedThroughSequence,
            ContentJson = RuntimeArtifactJson.Serialize(item), SchemaVersion = chain.CurrentVersion, Revision = 1 };
    }

    internal RuntimeHistoryEventEntity Row(string scope, RuntimeHistoryEvent item, string fingerprint)
    {
        RuntimeHistorySafety.ValidateMetadata(item);
        if (item.Sequence <= 0 || item.SchemaVersion != 1 || item.Authority == RuntimeHistoryAuthority.Unspecified ||
            item.RecordedAt is null || string.IsNullOrWhiteSpace(item.PhysicalCommitId) || !ValidFingerprint(fingerprint))
            throw Corrupt(item.WorkflowExecutionId, "an event write lacks its durable position or receipt");
        return new() { Id = Id("event", scope, item.WorkflowExecutionId, item.HistoryEventId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
            WorkflowExecutionId = Encode(item.WorkflowExecutionId), WorkflowExecutionIdHash = Hash(item.WorkflowExecutionId),
            HistoryEventId = Encode(item.HistoryEventId), HistoryEventIdHash = Hash(item.HistoryEventId), Name = Encode(item.Name), NameHash = Hash(item.Name),
            Category = (int)item.Category, CandidateFingerprint = fingerprint, ActivityExecutionId = Encoded(item.ActivityExecutionId),
            ActivityExecutionIdHash = Hashed(item.ActivityExecutionId), BookmarkId = Encoded(item.BookmarkId), BookmarkIdHash = Hashed(item.BookmarkId),
            Sequence = item.Sequence, ContentJson = RuntimeArtifactJson.Serialize(item), SchemaVersion = chain.CurrentVersion, Revision = 1 };
    }

    internal RuntimeHistoryBookmarkEntity Row(string scope, RuntimeHistoryBookmark item)
    {
        var identity = item.Bookmark.Identity;
        return new() { Id = Id("bookmark", scope, identity.WorkflowExecutionId, identity.BookmarkId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
            WorkflowExecutionId = Encode(identity.WorkflowExecutionId), WorkflowExecutionIdHash = Hash(identity.WorkflowExecutionId),
            BookmarkId = Encode(identity.BookmarkId), BookmarkIdHash = Hash(identity.BookmarkId), ActivityExecutionId = Encode(identity.ActivityExecutionId),
            ActivityExecutionIdHash = Hash(identity.ActivityExecutionId), FirstRecordedSequence = item.FirstRecordedSequence, Status = (int)item.Status,
            ContentJson = RuntimeArtifactJson.Serialize(item), SchemaVersion = chain.CurrentVersion, Revision = 1 };
    }

    internal RuntimeHistoryActivityEntity Row(string scope, string run, ActivityParent item) => new()
    {
        Id = Id("activity", scope, run, item.ActivityExecutionId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
        WorkflowExecutionId = Encode(run), WorkflowExecutionIdHash = Hash(run), ActivityExecutionId = Encode(item.ActivityExecutionId),
        ActivityExecutionIdHash = Hash(item.ActivityExecutionId), ParentActivityExecutionId = Encoded(item.ParentActivityExecutionId),
        ContentJson = RuntimeArtifactJson.Serialize(item), SchemaVersion = chain.CurrentVersion, Revision = 1
    };

    internal RuntimeHistoryBookmarkCountEntity Row(string scope, string run, BookmarkCount item) => new()
    {
        Id = Id("count", scope, run, CountSubject(item.ActivityExecutionId)), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
        WorkflowExecutionId = Encode(run), WorkflowExecutionIdHash = Hash(run), ActivityExecutionId = Encoded(item.ActivityExecutionId),
        ActivityExecutionIdHash = Hash(CountSubject(item.ActivityExecutionId)), TotalCount = item.TotalCount, ActiveCount = item.ActiveCount,
        ContentJson = RuntimeArtifactJson.Serialize(item), SchemaVersion = chain.CurrentVersion, Revision = 1
    };

    internal static void ValidateHead(string run, RuntimeHistoryHead? item)
    {
        if (item is not null && (item.WorkflowExecutionId != run || item.Policy is null || item.FirstSequence < 0 || item.LastSequence < 0 ||
            item.PurgedThroughSequence < 0 || item.PurgedThroughSequence > item.LastSequence ||
            item.FirstSequence > item.LastSequence && (item.LastSequence == long.MaxValue || item.FirstSequence != item.LastSequence + 1)))
            throw Corrupt(run, "the retained history head is invalid");
    }

    private void Envelope(string version, string id, string expectedId, string scopeKey, string scopeHash,
        string runKey, string runHash, long revision, string scope, string run)
    {
        EfSchemaVersion.EnsureReadable(chain, version);
        if (id != expectedId || scopeKey != Encode(scope) || scopeHash != Hash(scope) || runKey != Encode(run) || runHash != Hash(run) || revision <= 0)
            throw Corrupt(run, "a stored history envelope disagrees with its complete scoped identity");
    }

    private T Content<TEntity, T>(string version, string content)
    {
        var upcast = chain.Upcast<TEntity>(version, ("ContentJson", content));
        return RuntimeArtifactJson.Deserialize<T>(upcast["ContentJson"] ?? throw new InvalidDataException("History content was null."));
    }

    private static T Checked<T>(string run, Func<T> read)
    {
        try { return read(); }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        { throw new RuntimeHistoryIntegrityException(run, "a retained history projection cannot be interpreted safely", exception); }
    }

    private static bool ValidFingerprint(string? value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);
    private static string? Encoded(string? value) => value is null ? null : Encode(value);
    private static string? Hashed(string? value) => value is null ? null : Hash(value);
    private static RuntimeHistoryIntegrityException Corrupt(string run, string reason) => new(run, reason);
    internal sealed record EventReceipt(string HistoryEventId, long Sequence, string CandidateFingerprint, bool IsPurged);
    internal sealed record ActivityParent(string ActivityExecutionId, string? ParentActivityExecutionId);
    internal sealed record BookmarkCount(string? ActivityExecutionId, long TotalCount, long ActiveCount);
}
