using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.ActivityExecutionEfSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class EfRuntimeHistorySupport
{
    internal static RuntimeHistoryHead Read(RuntimeHistoryHeadEntity row, string scope, string run) => Checked(run, () => ReadCore(row, scope, run));
    internal static string ReadReceipt(RuntimeHistoryEventEntity row, string scope, string run, RuntimeHistoryHead? head) => Checked(run, () => ReadReceiptCore(row, scope, run, head));
    internal static RuntimeHistoryEvent Read(RuntimeHistoryEventEntity row, string scope, string run, RuntimeHistoryHead? head) => Checked(run, () => ReadCore(row, scope, run, head));
    internal static RuntimeHistoryBookmark Read(RuntimeHistoryBookmarkEntity row, string scope, string run, RuntimeHistoryHead? head) => Checked(run, () => ReadCore(row, scope, run, head));
    internal static ActivityParent Read(RuntimeHistoryActivityEntity row, string scope, string run, string activity) => Checked(run, () => ReadCore(row, scope, run, activity));
    internal static BookmarkCount Read(RuntimeHistoryBookmarkCountEntity row, string scope, string run, string? activity) => Checked(run, () => ReadCore(row, scope, run, activity));
    private static T Checked<T>(string run, Func<T> read)
    {
        try { return read(); }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException or InvalidOperationException or NotSupportedException or OverflowException)
        { throw new RuntimeHistoryIntegrityException(run, "a retained history projection cannot be interpreted safely", exception); }
    }
    internal static void EnsureWritable(RuntimeDbContext context)
    {
        // ExecuteUpdate reservations bypass SaveChanges interceptors, including a zero-new-event checkpoint.
        // Apply the same admitted family gate before any such write instead of relying on a later event insert.
        var gate = context.GetService<IDbContextOptions>().FindExtension<CoreOptionsExtension>()?.ApplicationServiceProvider?
            .GetService<EfSchemaFinalizationGates>()?.FindModuleGate(context.GetType());
        var state = gate?.StateOf(RuntimeHistoryEfModule.SchemaFamily);
        if (gate is not null && state is null) throw new InvalidOperationException("The history family has not been admitted by its schema gate.");
        if (state is { WritesRefused: true }) throw new EfSchemaFamilyWritesRefusedException(RuntimeHistoryEfModule.SchemaFamily,
            state.WriteVersion, state.UnreadableFinalizedVersion!, state.ReadableVersions);
        if (state is not null && state.WriteVersion != RuntimeHistoryEfModule.SchemaVersion)
            throw new EfSchemaWriteRefusedException(RuntimeHistoryEfModule.SchemaFamily, state.WriteVersion, RuntimeHistoryEfModule.SchemaVersion);
    }
    internal static string Id(string kind, string scope, string run, string subject = "") => CreateId("history:" + kind, scope, run, subject);
    internal static string CountSubject(string? activity) => activity is null ? "run" : "activity:" + activity;
    internal static string Serialize<T>(T item) => RuntimeArtifactJson.SerializeNumericEnums(item);
    internal static T Deserialize<T>(string json) => RuntimeArtifactJson.Deserialize<T>(json);

    internal static IQueryable<RuntimeHistoryEventEntity> Events(RuntimeDbContext context, string scope, string run)
    {
        var sh = Hash(scope); var sk = Encode(scope); var rh = Hash(run); var rk = Encode(run);
        return context.RuntimeHistoryEvents.AsNoTracking().Where(x => !x.IsPurged && x.ScopeKeyHash == sh && x.ScopeKey == sk && x.WorkflowExecutionIdHash == rh && x.WorkflowExecutionId == rk);
    }
    internal static IQueryable<RuntimeHistoryBookmarkEntity> Bookmarks(RuntimeDbContext context, string scope, string run)
    {
        var sh = Hash(scope); var sk = Encode(scope); var rh = Hash(run); var rk = Encode(run);
        return context.RuntimeHistoryBookmarks.AsNoTracking().Where(x => x.ScopeKeyHash == sh && x.ScopeKey == sk && x.WorkflowExecutionIdHash == rh && x.WorkflowExecutionId == rk);
    }
    internal static void Envelope(string run, string scope, string id, string expectedId, string scopeKey, string scopeHash,
        string runKey, string runHash, string version, long revision)
    {
        if (id != expectedId || scopeKey != Encode(scope) || scopeHash != Hash(scope) || runKey != Encode(run) || runHash != Hash(run) ||
            EfSchemaVersion.NotReadable(RuntimeHistoryEfModule.Chain, version) || revision <= 0)
            throw new RuntimeHistoryIntegrityException(run, "a stored history envelope does not match its scoped identity");
    }
    private static RuntimeHistoryHead ReadCore(RuntimeHistoryHeadEntity row, string scope, string run)
    {
        Envelope(run, scope, row.Id, Id("head", scope, run), row.ScopeKey, row.ScopeKeyHash, row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.SchemaVersion, row.Revision);
        var item = Deserialize<RuntimeHistoryHead>(row.ContentJson);
        ValidateHead(run, item);
        if (row.FirstSequence != item.FirstSequence || row.LastSequence != item.LastSequence || row.PurgedThroughSequence != item.PurgedThroughSequence)
            throw new RuntimeHistoryIntegrityException(run, "the history head projections disagree");
        return item;
    }
    internal static void ValidateHead(string run, RuntimeHistoryHead? item)
    {
        if (item is null) return;
        if (item.Policy is null || item.WorkflowExecutionId != run || item.FirstSequence < 0 || item.LastSequence < 0 || item.PurgedThroughSequence < 0 ||
            item.PurgedThroughSequence > item.LastSequence || item.FirstSequence > item.LastSequence &&
            (item.LastSequence == long.MaxValue || item.FirstSequence != item.LastSequence + 1))
            throw new RuntimeHistoryIntegrityException(run, "the retained history head is invalid");
    }
    private static string ReadReceiptCore(RuntimeHistoryEventEntity row, string scope, string run, RuntimeHistoryHead? head)
    {
        var eventId = Decode(row.HistoryEventId);
        Envelope(run, scope, row.Id, Id("event", scope, run, eventId), row.ScopeKey, row.ScopeKeyHash, row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.SchemaVersion, row.Revision);
        if (row.HistoryEventIdHash != Hash(eventId) || row.Sequence <= 0 || head is null || row.Sequence > head.LastSequence ||
            row.CandidateFingerprint.Length != 64 || row.CandidateFingerprint.Any(c => !Uri.IsHexDigit(c)) ||
            row.IsPurged && (row.Sequence > head.PurgedThroughSequence || row.ContentJson.Length != 0))
            throw new RuntimeHistoryIntegrityException(run, "a semantic history receipt is corrupt");
        return eventId;
    }
    private static RuntimeHistoryEvent ReadCore(RuntimeHistoryEventEntity row, string scope, string run, RuntimeHistoryHead? head)
    {
        var eventId = ReadReceipt(row, scope, run, head);
        if (row.IsPurged) throw new RuntimeHistoryIntegrityException(run, "a purged receipt was returned as an event");
        var item = Deserialize<RuntimeHistoryEvent>(row.ContentJson);
        // Read the common envelope of future semantic kinds/categories. Capture validation is deliberately
        // stricter; applying it here would hide otherwise readable evidence after a rolling upgrade.
        if (item.SchemaVersion <= 0 || item.HistoryEventId.Length > 256 || item.WorkflowExecutionId.Length > 1024 || item.Name.Length > 230 ||
            item.Payload is not null || item.Metadata.Count > 16 || item.Metadata.Any(pair => pair.Value is null || pair.Value.Length > 2048) ||
            new[] { item.ActivityExecutionId, item.ExecutableNodeId, item.CheckpointId, item.IncidentId, item.BookmarkId, item.AttemptId,
                item.ParentActivityExecutionId, item.ExecutionScopeId, item.IterationId, item.Causation?.HistoryEventId,
                item.Causation?.WorkflowExecutionId, item.Causation?.DeliveryId, item.Causation?.CommandId, item.Causation?.SchedulerWorkItemId }
                .Any(value => value is not null && (string.IsNullOrWhiteSpace(value) || value.Length > 1024)))
            throw new RuntimeHistoryIntegrityException(run, "an event has an unsafe common envelope");
        if (row.HistoryEventIdHash != Hash(eventId) || item.HistoryEventId != eventId || item.WorkflowExecutionId != run ||
            item.Sequence != row.Sequence || row.Name != Encode(item.Name) || row.NameHash != Hash(item.Name) || row.Category != (int)item.Category || row.CandidateFingerprint.Length != 64 || row.CandidateFingerprint.Any(c => !Uri.IsHexDigit(c)) ||
            item.Sequence <= 0 || head is null || item.Sequence < head.FirstSequence || item.Sequence <= head.PurgedThroughSequence || item.Sequence > head.LastSequence ||
            string.IsNullOrWhiteSpace(item.PhysicalCommitId) || item.RecordedAt is null || item.Authority == RuntimeHistoryAuthority.Unspecified ||
            item.Authority == RuntimeHistoryAuthority.CommittedTransition && (item.CheckpointId is null || item.CheckpointOrdinal is null || item.CheckpointOrdinal < 0) ||
            row.ActivityExecutionId != (item.ActivityExecutionId is null ? null : Encode(item.ActivityExecutionId)) || row.ActivityExecutionIdHash != (item.ActivityExecutionId is null ? null : Hash(item.ActivityExecutionId)) ||
            row.BookmarkId != (item.BookmarkId is null ? null : Encode(item.BookmarkId)) || row.BookmarkIdHash != (item.BookmarkId is null ? null : Hash(item.BookmarkId)))
            throw new RuntimeHistoryIntegrityException(run, "an event has corrupt content, identity or retained position");
        return item;
    }
    private static RuntimeHistoryBookmark ReadCore(RuntimeHistoryBookmarkEntity row, string scope, string run, RuntimeHistoryHead? head)
    {
        var bookmark = Decode(row.BookmarkId);
        Envelope(run, scope, row.Id, Id("bookmark", scope, run, bookmark), row.ScopeKey, row.ScopeKeyHash, row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.SchemaVersion, row.Revision);
        var item = Deserialize<RuntimeHistoryBookmark>(row.ContentJson);
        if (item.Bookmark?.Identity is null || string.IsNullOrWhiteSpace(item.LastHistoryEventId) || row.BookmarkIdHash != Hash(bookmark) || item.Bookmark.Identity.WorkflowExecutionId != run || item.Bookmark.Identity.BookmarkId != bookmark ||
            row.ActivityExecutionId != Encode(item.Bookmark.Identity.ActivityExecutionId) || row.ActivityExecutionIdHash != Hash(item.Bookmark.Identity.ActivityExecutionId) ||
            row.Status != (int)item.Status || !Enum.IsDefined(item.Status) || !Enum.IsDefined(item.PriorState) || row.FirstRecordedSequence != item.FirstRecordedSequence ||
            item.FirstRecordedSequence <= 0 || head is null || item.LastSequence < item.FirstRecordedSequence || item.LastSequence > head.LastSequence)
            throw new RuntimeHistoryIntegrityException(run, "a bookmark lifecycle has corrupt ownership or position");
        if (head.PurgedThroughSequence >= item.FirstRecordedSequence)
            item = item with { CreatedHistoryEventId = null, PriorState = RuntimeHistoryPriorState.CreationMetadata,
                EndedHistoryEventId = item.LastSequence <= head.PurgedThroughSequence ? null : item.EndedHistoryEventId };
        return item;
    }
    internal sealed record ActivityParent(string ActivityExecutionId, string? ParentActivityExecutionId);
    private static ActivityParent ReadCore(RuntimeHistoryActivityEntity row, string scope, string run, string activity)
    {
        Envelope(run, scope, row.Id, Id("activity", scope, run, activity), row.ScopeKey, row.ScopeKeyHash, row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.SchemaVersion, row.Revision);
        var item = Deserialize<ActivityParent>(row.ContentJson);
        if (item.ActivityExecutionId != activity || row.ActivityExecutionId != Encode(activity) || row.ActivityExecutionIdHash != Hash(activity) ||
            row.ParentActivityExecutionId != (item.ParentActivityExecutionId is null ? null : Encode(item.ParentActivityExecutionId)))
            throw new RuntimeHistoryIntegrityException(run, "an ancestry index has corrupt ownership or parent facts");
        return item;
    }
    internal static RuntimeHistoryEventEntity EventRow(string scope, RuntimeHistoryEvent item, string fingerprint) => new()
    {
        Id = Id("event", scope, item.WorkflowExecutionId, item.HistoryEventId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
        WorkflowExecutionId = Encode(item.WorkflowExecutionId), WorkflowExecutionIdHash = Hash(item.WorkflowExecutionId),
        HistoryEventId = Encode(item.HistoryEventId), HistoryEventIdHash = Hash(item.HistoryEventId), Name = Encode(item.Name), NameHash = Hash(item.Name), Category = (int)item.Category, CandidateFingerprint = fingerprint,
        ActivityExecutionId = item.ActivityExecutionId is null ? null : Encode(item.ActivityExecutionId), ActivityExecutionIdHash = item.ActivityExecutionId is null ? null : Hash(item.ActivityExecutionId),
        BookmarkId = item.BookmarkId is null ? null : Encode(item.BookmarkId), BookmarkIdHash = item.BookmarkId is null ? null : Hash(item.BookmarkId),
        Sequence = item.Sequence, ContentJson = Serialize(item), SchemaVersion = RuntimeHistoryEfModule.SchemaVersion, Revision = 1
    };
    internal static RuntimeHistoryHeadEntity HeadRow(string scope, RuntimeHistoryHead item) => new()
    {
        Id = Id("head", scope, item.WorkflowExecutionId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
        WorkflowExecutionId = Encode(item.WorkflowExecutionId), WorkflowExecutionIdHash = Hash(item.WorkflowExecutionId),
        FirstSequence = item.FirstSequence, LastSequence = item.LastSequence, PurgedThroughSequence = item.PurgedThroughSequence,
        ContentJson = Serialize(item), SchemaVersion = RuntimeHistoryEfModule.SchemaVersion, Revision = 1
    };
    internal static void Update(RuntimeHistoryHeadEntity row, RuntimeHistoryHead item)
    {
        row.FirstSequence = item.FirstSequence; row.LastSequence = item.LastSequence; row.PurgedThroughSequence = item.PurgedThroughSequence;
        row.ContentJson = Serialize(item); row.Revision = checked(row.Revision + 1);
    }
    internal static RuntimeHistoryBookmarkEntity BookmarkRow(string scope, RuntimeHistoryBookmark item) => new()
    {
        Id = Id("bookmark", scope, item.Bookmark.Identity.WorkflowExecutionId, item.Bookmark.Identity.BookmarkId), ScopeKey = Encode(scope), ScopeKeyHash = Hash(scope),
        WorkflowExecutionId = Encode(item.Bookmark.Identity.WorkflowExecutionId), WorkflowExecutionIdHash = Hash(item.Bookmark.Identity.WorkflowExecutionId),
        BookmarkId = Encode(item.Bookmark.Identity.BookmarkId), BookmarkIdHash = Hash(item.Bookmark.Identity.BookmarkId),
        ActivityExecutionId = Encode(item.Bookmark.Identity.ActivityExecutionId), ActivityExecutionIdHash = Hash(item.Bookmark.Identity.ActivityExecutionId),
        FirstRecordedSequence = item.FirstRecordedSequence, Status = (int)item.Status, ContentJson = Serialize(item), SchemaVersion = RuntimeHistoryEfModule.SchemaVersion, Revision = 1
    };
    internal static void Update(RuntimeHistoryBookmarkEntity row, RuntimeHistoryBookmark item)
    {
        row.Status = (int)item.Status; row.ContentJson = Serialize(item); row.Revision = checked(row.Revision + 1);
    }
    internal sealed record BookmarkCount(string? ActivityExecutionId, long TotalCount, long ActiveCount);
    private static BookmarkCount ReadCore(RuntimeHistoryBookmarkCountEntity row, string scope, string run, string? activity)
    {
        var subject = CountSubject(activity);
        Envelope(run, scope, row.Id, Id("count", scope, run, subject), row.ScopeKey, row.ScopeKeyHash, row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.SchemaVersion, row.Revision);
        var item = Deserialize<BookmarkCount>(row.ContentJson);
        if (row.ActivityExecutionId != (activity is null ? null : Encode(activity)) || row.ActivityExecutionIdHash != Hash(subject) ||
            item.ActivityExecutionId != activity || item.TotalCount != row.TotalCount || item.ActiveCount != row.ActiveCount || item.ActiveCount < 0 || item.TotalCount < item.ActiveCount)
            throw new RuntimeHistoryIntegrityException(run, "bookmark counts have corrupt ownership or values");
        return item;
    }
}
