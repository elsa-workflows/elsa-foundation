using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Xunit;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class RuntimeHistoryProjectionMappingTests
{
    private const string Scope = "Tenant-\uD800";
    private const string Run = "Run-\uDC00";
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.FromHours(2));
    private static readonly string Fingerprint = new('A', 64);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("policy", 1, true, ["ActivityCompleted", "BookmarkConsumed"]);
    private static RuntimeHistoryHead Head => new(Run, 1, 2, "physical", Now, Policy, true, false, 0);
    private static Mapping Create() => new(EfSchemaChain.For(Declaration()));

    [Fact]
    public void All_projections_round_trip_opaque_identity_and_original_time_evidence()
    {
        var mapping = Create();
        var head = mapping.Read(mapping.Row(Scope, Head), Scope, Run);
        Assert.True(Policy.IsEquivalentTo(head.Policy)); Assert.Equal(Head.LastCommittedAt, head.LastCommittedAt);
        var item = mapping.Read(mapping.Row(Scope, Observation(), Fingerprint), Scope, Run, head);
        Assert.Equal(Run, item.WorkflowExecutionId); Assert.False(item.OccurredAtKnown);
        Assert.Equal(Now, item.OccurredAt); Assert.Equal(Now.AddHours(1), item.RecordedAt);
        Assert.Equal("activity-\uD800", item.ActivityExecutionId); Assert.Equal("iteration", item.IterationId);
        Assert.Equal("attempt", item.AttemptId); Assert.Equal("execution-scope", item.ExecutionScopeId);
        Assert.Equal("delivery", item.Causation!.DeliveryId);
        Assert.Equal(Bookmark(), mapping.Read(mapping.Row(Scope, Bookmark()), Scope, Run, head));
        var parent = new Mapping.ActivityParent("activity-\uD800", null);
        Assert.Equal(parent, mapping.Read(mapping.Row(Scope, Run, parent), Scope, Run, parent.ActivityExecutionId));
        var counts = new Mapping.BookmarkCount(null, 3, 1);
        Assert.Equal(counts, mapping.Read(mapping.Row(Scope, Run, counts), Scope, Run, (string?)null));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    public void Every_projection_settles_schema_skew_before_interpreting_corrupt_content(int kind)
    {
        var mapping = Create();
        object Read()
        {
            switch (kind)
            {
                case 0:
                    var head = mapping.Row(Scope, Head); head.SchemaVersion = "2.0.0"; head.ContentJson = "{broken"; head.ScopeKey = "bad";
                    return mapping.Read(head, Scope, Run);
                case 1:
                    var observation = mapping.Row(Scope, Observation(), Fingerprint); observation.SchemaVersion = "2.0.0";
                    observation.HistoryEventId = "bad"; observation.ContentJson = "{broken";
                    return mapping.Read(observation, Scope, Run, Head);
                case 2:
                    var bookmark = mapping.Row(Scope, Bookmark()); bookmark.SchemaVersion = "2.0.0"; bookmark.BookmarkId = "bad";
                    return mapping.Read(bookmark, Scope, Run, Head);
                case 3:
                    var parent = mapping.Row(Scope, Run, new Mapping.ActivityParent("activity", null)); parent.SchemaVersion = "2.0.0"; parent.ContentJson = "{broken";
                    return mapping.Read(parent, Scope, Run, "activity");
                default:
                    var counts = mapping.Row(Scope, Run, new Mapping.BookmarkCount(null, 1, 0)); counts.SchemaVersion = "2.0.0"; counts.ContentJson = "{broken";
                    return mapping.Read(counts, Scope, Run, (string?)null);
            }
        }
        var failure = Assert.Throws<EfSchemaVersionSkewException>(() => Read());
        Assert.Equal("RuntimeHistory", failure.Family); Assert.Equal("2.0.0", failure.Found);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public void Content_is_upcast_through_the_supplied_family_before_deserialization()
    {
        var declaration = Declaration() with { Upcasters = [EfSchemaFamilyCatalog.DescribeUpcaster(typeof(HeadUpgrade))] };
        var mapping = new Mapping(EfSchemaChain.For(declaration));
        var row = mapping.Row(Scope, Head); row.SchemaVersion = "0.9.0"; row.ContentJson = "old-format";
        Assert.Equal(2, mapping.Read(row, Scope, Run).LastSequence);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    public void Retained_event_corruption_cannot_become_another_resource_or_successful_record(int fault)
    {
        var mapping = Create(); var row = mapping.Row(Scope, Observation(), Fingerprint);
        switch (fault)
        {
            case 0: row.ScopeKey = EfRelationalIdentity.Encode("other-scope"); break; // Original hash alone must not authorize it.
            case 1: row.WorkflowExecutionId = EfRelationalIdentity.Encode("other-run"); break;
            case 2: row.HistoryEventIdHash = new string('B', 64); break;
            case 3: row.Sequence = 2; break;
            case 4: row.NameHash = new string('B', 64); break;
            case 5: row.Category = (int)RuntimeHistoryEventCategory.BookmarkLifecycle; break;
            case 6: row.ActivityExecutionId = EfRelationalIdentity.Encode("other-activity"); break;
            case 7: row.BookmarkId = EfRelationalIdentity.Encode("other-bookmark"); break;
            case 8: row.CandidateFingerprint = "unbounded-or-missing-receipt"; break;
            case 9: row.ContentJson = "{broken"; break;
            case 10: row.ContentJson = RuntimeArtifactJson.Serialize(Observation(physical: false)); break;
            default: row.ContentJson = RuntimeArtifactJson.Serialize(Observation(protectedPayload: true)); break;
        }
        var failure = Assert.Throws<RuntimeHistoryIntegrityException>(() => mapping.Read(row, Scope, Run, Head));
        if (fault == 9) Assert.IsAssignableFrom<JsonException>(failure.InnerException);
    }

    [Fact]
    public void Unknown_event_semantics_remain_recorded_without_becoming_known_execution_evidence()
    {
        var mapping = Create(); var future = Observation(future: true);
        var row = mapping.Row(Scope, Observation(), Fingerprint);
        row.ContentJson = RuntimeArtifactJson.Serialize(future); row.Name = EfRelationalIdentity.Encode(future.Name);
        row.NameHash = EfRelationalIdentity.Hash(future.Name); row.Category = (int)future.Category;
        var retained = mapping.Read(row, Scope, Run, Head);
        Assert.Equal(7, retained.SchemaVersion); Assert.Equal("FutureLifecycle", retained.Name);
        Assert.Equal((RuntimeHistoryEventCategory)500, retained.Category);
        Assert.Equal((RuntimeHistoryAuthority)500, retained.Authority);
        Assert.False(RuntimeHistoryOccurrence.IsEligible(retained));
    }

    [Fact]
    public void Acknowledged_purge_retains_receipt_but_never_deserializes_removed_content()
    {
        var mapping = Create(); var row = mapping.Row(Scope, Observation(), Fingerprint);
        row.IsPurged = true; row.ContentJson = "";
        var head = Head with { FirstSequence = 2, PurgedThroughSequence = 1 };
        var receipt = mapping.ReadReceipt(row, Scope, Run, head);
        Assert.Equal("event", receipt.HistoryEventId); Assert.Equal(1, receipt.Sequence); Assert.True(receipt.IsPurged);
        Assert.Equal(Fingerprint, receipt.CandidateFingerprint);
        Assert.Throws<RuntimeHistoryIntegrityException>(() => mapping.Read(row, Scope, Run, head));
        Assert.Throws<RuntimeHistoryIntegrityException>(() => mapping.ReadReceipt(row, Scope, Run, Head));
    }

    [Theory]
    [InlineData(RuntimeHistoryCreationEvidence.Unknown)]
    [InlineData(RuntimeHistoryCreationEvidence.CreationMetadata)]
    [InlineData(RuntimeHistoryCreationEvidence.CreationEvent)]
    public void Retention_drops_purged_links_without_inventing_creation_evidence(RuntimeHistoryCreationEvidence evidence)
    {
        var mapping = Create(); var item = Bookmark() with { PriorState = evidence };
        var retained = mapping.Read(mapping.Row(Scope, item), Scope, Run, Head with { FirstSequence = 2, PurgedThroughSequence = 1 });
        Assert.Null(retained.CreatedHistoryEventId); Assert.Equal("ended", retained.EndedHistoryEventId);
        Assert.Equal(evidence == RuntimeHistoryCreationEvidence.CreationEvent ? RuntimeHistoryCreationEvidence.CreationMetadata : evidence, retained.PriorState);
        Assert.Equal(item.Bookmark, retained.Bookmark); Assert.Equal(item.Status, retained.Status);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Bookmark_ownership_status_and_position_are_checked_before_returning_its_lifecycle(int fault)
    {
        var mapping = Create(); var row = mapping.Row(Scope, Bookmark());
        switch (fault)
        {
            case 0: row.ActivityExecutionId = EfRelationalIdentity.Encode("other"); break;
            case 1: row.Status = (int)RuntimeHistoryBookmarkStatus.Active; break;
            case 2: row.FirstRecordedSequence = 2; break;
            default: row.ContentJson = RuntimeArtifactJson.Serialize(Bookmark() with { LastSequence = 3 }); break;
        }
        Assert.Throws<RuntimeHistoryIntegrityException>(() => mapping.Read(row, Scope, Run, Head));
    }

    [Fact]
    public void Known_root_and_run_counts_have_distinct_exact_scope_identities()
    {
        var mapping = Create();
        var root = mapping.Row(Scope, Run, new Mapping.ActivityParent("root", null));
        Assert.Null(mapping.Read(root, Scope, Run, "root").ParentActivityExecutionId);
        root.ParentActivityExecutionId = EfRelationalIdentity.Encode("invented-parent");
        Assert.Throws<RuntimeHistoryIntegrityException>(() => mapping.Read(root, Scope, Run, "root"));
        var run = mapping.Row(Scope, Run, new Mapping.BookmarkCount(null, 3, 1));
        var activity = mapping.Row(Scope, Run, new Mapping.BookmarkCount("run", 3, 1));
        Assert.NotEqual(run.Id, activity.Id); Assert.NotEqual(run.ActivityExecutionIdHash, activity.ActivityExecutionIdHash);
        Assert.Throws<RuntimeHistoryIntegrityException>(() => mapping.Read(run, Scope, Run, "run"));
        activity.ActiveCount = 4;
        Assert.Throws<RuntimeHistoryIntegrityException>(() => mapping.Read(activity, Scope, Run, "run"));
    }

    private static RuntimeHistoryEvent Observation(bool physical = true, bool protectedPayload = false, bool future = false) =>
        new("event", Run, 1, future ? (RuntimeHistoryEventCategory)500 : RuntimeHistoryEventCategory.ActivityLifecycle,
            future ? "FutureLifecycle" : "ActivityCompleted", Now, "activity-\uD800",
            payload: protectedPayload ? JsonSerializer.SerializeToElement(new { value = "must-not-be-semantic-history" }) : null)
        {
            SchemaVersion = future ? 7 : 1, Authority = future ? (RuntimeHistoryAuthority)500 : RuntimeHistoryAuthority.CommittedTransition,
            PhysicalCommitId = physical ? "physical" : null, RecordedAt = Now.AddHours(1), OccurredAtKnown = false,
            BookmarkId = "bookmark", AttemptId = "attempt", IterationId = "iteration", ExecutionScopeId = "execution-scope",
            ActivityAncestryKnown = true, Causation = new(DeliveryId: "delivery")
        };

    private static RuntimeHistoryBookmark Bookmark() => new(new(new("bookmark", Run, "activity-\uD800", "resume"), "signal", "hash", Now, Now.AddHours(1)),
        "node", RuntimeHistoryBookmarkStatus.Consumed, "created", "ended", "ended", 2, 1, Now.AddMinutes(1), "selected", "bookmark", RuntimeHistoryCreationEvidence.CreationEvent);

    private static EfSchemaFamilyDescriptor Declaration() => new("RuntimeHistory", "Workflows.Runtime", "1.0.0", typeof(RuntimeHistoryProjectionMappingTests).Assembly)
    {
        ContentColumns = new[] { typeof(RuntimeHistoryHeadEntity), typeof(RuntimeHistoryEventEntity), typeof(RuntimeHistoryBookmarkEntity),
            typeof(RuntimeHistoryActivityEntity), typeof(RuntimeHistoryBookmarkCountEntity) }.Select(type => new EfSchemaColumn(type, "ContentJson")).ToArray()
    };

    [EfSchemaUpcaster("0.9.0", "1.0.0")]
    public sealed class HeadUpgrade : IEfSchemaUpcaster
    {
        public EfSchemaRowContent Upcast(EfSchemaRowContent row) => row.With("ContentJson", RuntimeArtifactJson.Serialize(Head));
    }
}
