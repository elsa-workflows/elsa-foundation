using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static partial class EfRuntimeHistoryOccurrenceProjection
{
    internal sealed record Receipt(string Scope, string Run, string Activity, string FirstEvent, long FirstSequence,
        string? Parent, bool? ObservedAncestryKnown, bool AncestryKnown, string[] Ancestors);
    internal sealed record Membership(string Scope, string Run, string? ActivityScope, string Activity, long FirstSequence, long Ordinal);
    internal sealed record Counter(string Scope, string Run, string? ActivityScope, long NextOrdinal,
        long ProjectionStartsAtSequence, long CoveredThroughSequence, long? FirstUnknownObservationSequence);

    internal static string ReceiptId(string scope, string run, string activity) =>
        EfRelationalIdentity.HashLengthFramed("history:occurrence", scope, run, activity);
    internal static string CounterId(string scope, string run, string? activityScope) =>
        EfRelationalIdentity.HashLengthFramed("history:occurrence-counter", scope, run, activityScope is null ? "run" : "activity", activityScope ?? "");
    internal static string MembershipId(string scopeOwnerId, string activity) =>
        EfRelationalIdentity.HashLengthFramed("history:occurrence-member", scopeOwnerId, activity);
    private static string ScopeHash(string? activityScope) => activityScope is null
        ? EfRelationalIdentity.HashLengthFramed("run") : EfRelationalIdentity.HashLengthFramed("activity", activityScope);
    private static string Encode(string value) => EfRelationalIdentity.Encode(value);
    private static string Decode(string value) => EfRelationalIdentity.Decode(value);
    private static RuntimeHistoryIntegrityException Invalid(string run, string reason) => new(run, "occurrence projection " + reason);

    internal static Receipt ReadReceipt(RuntimeHistoryOccurrenceEntity row, string scope, string run, EfSchemaChain chain) => Checked(chain, row.SchemaVersion, run, () =>
    {
        var value = Content<RuntimeHistoryOccurrenceEntity, Receipt>(chain, row.SchemaVersion, row.ContentJson);
        if (value.Activity is null || value.FirstEvent is null || value.Ancestors is null) throw Invalid(run, "receipt is incomplete");
        var activity = Decode(value.Activity);
        Owner(scope, run, row.Id, ReceiptId(scope, run, activity), row.ScopeKey, row.ScopeKeyHash,
            row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.Revision);
        if (value.Scope != Encode(scope) || value.Run != Encode(run) || row.ActivityExecutionId != value.Activity ||
            row.ActivityExecutionIdHash != EfRelationalIdentity.Hash(activity) || row.FirstHistoryEventId != value.FirstEvent ||
            row.FirstSequence != value.FirstSequence || row.ParentActivityExecutionId != value.Parent || row.AncestryKnown != value.AncestryKnown ||
            value.FirstSequence <= 0 || value.Ancestors.Length > 255 || value.Ancestors.Distinct(StringComparer.Ordinal).Count() != value.Ancestors.Length ||
            value.Ancestors.Contains(value.Activity, StringComparer.Ordinal) ||
            value.AncestryKnown && (value.ObservedAncestryKnown != true || (value.Parent is null ? value.Ancestors.Length != 0 :
                value.Ancestors.Length == 0 || value.Ancestors[0] != value.Parent)) ||
            !value.AncestryKnown && value.Ancestors.Length != 0) throw Invalid(run, "receipt disagrees with its immutable identity or ancestry");
        _ = Decode(value.FirstEvent);
        if (value.Parent is not null) _ = Decode(value.Parent);
        foreach (var ancestor in value.Ancestors) _ = Decode(ancestor);
        return value;
    });

    internal static Counter ReadCounter(RuntimeHistoryOccurrenceCounterEntity row, string scope, string run, EfSchemaChain chain) => Checked(chain, row.SchemaVersion, run, () =>
    {
        var value = Content<RuntimeHistoryOccurrenceCounterEntity, Counter>(chain, row.SchemaVersion, row.ContentJson);
        var activityScope = value.ActivityScope is null ? null : Decode(value.ActivityScope);
        Owner(scope, run, row.Id, CounterId(scope, run, activityScope), row.ScopeKey, row.ScopeKeyHash,
            row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.Revision);
        if (value.Scope != Encode(scope) || value.Run != Encode(run) || row.ActivityScopeId != value.ActivityScope ||
            row.ActivityScopeIdHash != ScopeHash(activityScope) || row.NextOrdinal != value.NextOrdinal ||
            row.ProjectionStartsAtSequence != value.ProjectionStartsAtSequence || row.CoveredThroughSequence != value.CoveredThroughSequence ||
            row.FirstUnknownObservationSequence != value.FirstUnknownObservationSequence || value.NextOrdinal < 0 ||
            value.ProjectionStartsAtSequence <= 0 || value.CoveredThroughSequence < value.ProjectionStartsAtSequence ||
            value.FirstUnknownObservationSequence is { } unknown && (unknown < value.ProjectionStartsAtSequence || unknown > value.CoveredThroughSequence) ||
            activityScope is not null && value.FirstUnknownObservationSequence is not null)
            throw Invalid(run, "counter disagrees with its owner or coverage");
        return value;
    });

    internal static Membership ReadMembership(RuntimeHistoryOccurrenceMembershipEntity row, string scope, string run, EfSchemaChain chain) => Checked(chain, row.SchemaVersion, run, () =>
    {
        var value = Content<RuntimeHistoryOccurrenceMembershipEntity, Membership>(chain, row.SchemaVersion, row.ContentJson);
        if (value.Activity is null) throw Invalid(run, "membership is incomplete");
        var activity = Decode(value.Activity);
        var activityScope = value.ActivityScope is null ? null : Decode(value.ActivityScope);
        var scopeOwner = CounterId(scope, run, activityScope);
        Owner(scope, run, row.Id, MembershipId(scopeOwner, activity), row.ScopeKey, row.ScopeKeyHash,
            row.WorkflowExecutionId, row.WorkflowExecutionIdHash, row.Revision);
        if (value.Scope != Encode(scope) || value.Run != Encode(run) || row.ScopeOwnerId != scopeOwner ||
            row.ActivityScopeId != value.ActivityScope || row.ActivityExecutionId != value.Activity ||
            row.ActivityExecutionIdHash != EfRelationalIdentity.Hash(activity) || row.FirstSequence != value.FirstSequence ||
            row.Ordinal != value.Ordinal || value.Ordinal < 0 || value.FirstSequence <= 0)
            throw Invalid(run, "membership disagrees with its owner or ordinal");
        return value;
    });

    private static T Content<TRow, T>(EfSchemaChain chain, string version, string json) where T : class
    {
        // The caller supplies the owning family, including its declared content and upcasters.
        // No alternate version reader, hard-coded family, or raw-deserialization fallback exists.
        var content = chain.Upcast<TRow>(version, ("ContentJson", json))["ContentJson"];
        return RuntimeArtifactJson.Deserialize<T>(content ?? throw new InvalidDataException("Occurrence projection content is missing."))
            ?? throw new InvalidDataException("Occurrence projection content is null.");
    }

    private static T Checked<T>(EfSchemaChain chain, string version, string run, Func<T> read)
    {
        // Settle deployment compatibility before interpreting any stamped identity or content.
        EfSchemaVersion.EnsureReadable(chain, version);
        try { return read(); }
        catch (Exception error) when (error is JsonException or InvalidDataException or ArgumentException or FormatException or
            InvalidOperationException or NotSupportedException or OverflowException)
        { throw new RuntimeHistoryIntegrityException(run, "an occurrence projection cannot be interpreted safely", error); }
    }

    private static void Owner(string scope, string run, string id, string expectedId, string storedScope,
        string storedScopeHash, string storedRun, string storedRunHash, long revision)
    {
        if (id != expectedId || storedScope != Encode(scope) || storedScopeHash != EfRelationalIdentity.Hash(scope) ||
            storedRun != Encode(run) || storedRunHash != EfRelationalIdentity.Hash(run) || revision <= 0)
            throw Invalid(run, "row belongs to a different or unprovable physical owner");
    }

    private static RuntimeHistoryOccurrenceEntity Row(Receipt value, string scope, string run, EfSchemaChain chain) => new()
    {
        Id = ReceiptId(scope, run, Decode(value.Activity)), ScopeKey = value.Scope, ScopeKeyHash = EfRelationalIdentity.Hash(scope),
        WorkflowExecutionId = value.Run, WorkflowExecutionIdHash = EfRelationalIdentity.Hash(run),
        ActivityExecutionId = value.Activity, ActivityExecutionIdHash = EfRelationalIdentity.Hash(Decode(value.Activity)),
        FirstHistoryEventId = value.FirstEvent, FirstSequence = value.FirstSequence, ParentActivityExecutionId = value.Parent,
        AncestryKnown = value.AncestryKnown, ContentJson = RuntimeArtifactJson.Serialize(value), SchemaVersion = chain.CurrentVersion, Revision = 1
    };

    private static RuntimeHistoryOccurrenceMembershipEntity Row(Membership value, string scope, string run, EfSchemaChain chain)
    {
        var scopeOwner = CounterId(scope, run, value.ActivityScope is null ? null : Decode(value.ActivityScope));
        return new()
        {
            Id = MembershipId(scopeOwner, Decode(value.Activity)), ScopeOwnerId = scopeOwner,
            ScopeKey = value.Scope, ScopeKeyHash = EfRelationalIdentity.Hash(scope), WorkflowExecutionId = value.Run,
            WorkflowExecutionIdHash = EfRelationalIdentity.Hash(run), ActivityScopeId = value.ActivityScope,
            ActivityExecutionId = value.Activity, ActivityExecutionIdHash = EfRelationalIdentity.Hash(Decode(value.Activity)),
            FirstSequence = value.FirstSequence, Ordinal = value.Ordinal,
            ContentJson = RuntimeArtifactJson.Serialize(value), SchemaVersion = chain.CurrentVersion, Revision = 1
        };
    }

    private static RuntimeHistoryOccurrenceCounterEntity Row(Counter value, string scope, string run, EfSchemaChain chain, long revision) => new()
    {
        Id = CounterId(scope, run, value.ActivityScope is null ? null : Decode(value.ActivityScope)),
        ScopeKey = value.Scope, ScopeKeyHash = EfRelationalIdentity.Hash(scope), WorkflowExecutionId = value.Run,
        WorkflowExecutionIdHash = EfRelationalIdentity.Hash(run), ActivityScopeId = value.ActivityScope,
        ActivityScopeIdHash = ScopeHash(value.ActivityScope is null ? null : Decode(value.ActivityScope)),
        NextOrdinal = value.NextOrdinal, ProjectionStartsAtSequence = value.ProjectionStartsAtSequence,
        CoveredThroughSequence = value.CoveredThroughSequence, FirstUnknownObservationSequence = value.FirstUnknownObservationSequence,
        ContentJson = RuntimeArtifactJson.Serialize(value), SchemaVersion = chain.CurrentVersion, Revision = revision
    };
}
