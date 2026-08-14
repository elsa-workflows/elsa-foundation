using System.Globalization;
using System.Text.Json;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Adapts an enumerable document double to Runtime's admitted bounded-query contract. Production hosts obtain
/// the equivalent route-bound query runtime from their provider initializer; a real provider fixture gets one
/// from <see cref="GroundworkPhysicalTestStores"/> instead of this evaluator.
/// </summary>
/// <remarks>
/// Evaluation reads the double's whole document set for a kind and filters in memory, so it needs
/// <see cref="IDocumentEnumerationSource"/> rather than a store query.
/// </remarks>
public sealed class RuntimeTestBoundedDocumentStore : IBoundedDocumentStore
{
    private readonly IDocumentEnumerationSource documents;

    public RuntimeTestBoundedDocumentStore(IDocumentStore documents) =>
        this.documents = documents as IDocumentEnumerationSource
            ?? throw new ArgumentException(
                "The Runtime test bounded store evaluates over an enumerable document double. A real provider " +
                "store must be paired with its own route-bound query runtime instead.",
                nameof(documents));

    public async Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
    {
        if (IsOrderedRangeQuery(query))
            return await QueryOrderedRangeAsync(query, cancellationToken);

        var path = query.QueryIdentity switch
        {
            ElsaRuntimeStorageManifest.ListAllQuery =>
                ElsaRuntimeStorageManifest.CollectionField,
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery =>
                ElsaRuntimeStorageManifest.WorkflowExecutionIdField,
            ElsaRuntimeStorageManifest.ListByArtifactQuery =>
                ElsaRuntimeStorageManifest.ArtifactIdField,
            ElsaRuntimeStorageManifest.ListByParentActivityExecutionQuery =>
                ElsaRuntimeStorageManifest.ParentActivityExecutionIdField,
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusQuery =>
                ElsaRuntimeStorageManifest.StimulusHashField,
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery =>
                ElsaRuntimeStorageManifest.StimulusTypeField,
            ElsaRuntimeStorageManifest.FindExecutableActivityTemplateByHashQuery =>
                ElsaRuntimeStorageManifest.TemplateHashField,
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByParentQuery =>
                ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField,
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByChildQuery =>
                ElsaRuntimeStorageManifest.ChildWorkflowExecutionIdField,
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByStatusQuery =>
                ElsaRuntimeStorageManifest.StatusField,
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByTestScopeQuery =>
                ElsaRuntimeStorageManifest.TestScopeIdField,
            "list-by-execution-scope" =>
                ElsaRuntimeStorageManifest.ExecutionScopeIdField,
            ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery =>
                ElsaRuntimeStorageManifest.PublicationIdField,
            _ => throw new InvalidOperationException($"Undeclared Runtime test query '{query.QueryIdentity}'.")
        };
        var clause = query.Clauses.Count == 1
            ? query.Clauses[0]
            : throw new InvalidOperationException($"Runtime test query '{query.QueryIdentity}' must have one clause.");
        var comparison = clause.Comparisons.Count == 1
            ? clause.Comparisons[0]
            : throw new InvalidOperationException($"Runtime test query '{query.QueryIdentity}' must have one comparison.");
        if (comparison.Path != path || comparison.Operator != QueryComparisonOperator.Equal || comparison.Values.Count != 1)
            throw new InvalidOperationException($"Runtime test query '{query.QueryIdentity}' has an unexpected shape.");

        var matches = documents.Snapshot(query.DocumentKind)
            .Where(document => Matches(document, comparison))
            .OrderBy(document => document.Id, StringComparer.Ordinal)
            .ToArray();
        IEnumerable<DocumentEnvelope> page = matches.Skip(query.Skip ?? 0);
        if (query.Take is { } take)
            page = page.Take(take);
        return new DocumentQueryResult(page.ToArray(), matches.Length);
    }

    private Task<DocumentQueryResult> QueryOrderedRangeAsync(
        DocumentQuery query,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var comparisons = query.Clauses.SelectMany(clause => clause.Comparisons).ToArray();
        var matches = documents.Snapshot(query.DocumentKind)
            .Where(document => comparisons.All(comparison => Matches(document, comparison)))
            .ToArray();
        Array.Sort(matches, (left, right) => Compare(left, right, query.Order));
        if (query.LatestPerKeyPath is { } latestPerKeyPath)
        {
            matches = matches
                .GroupBy(document => ReadComparable(document, latestPerKeyPath), StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
        }
        return Task.FromResult(TestKeysetContinuations.Page(
            query,
            "runtime-test",
            matches,
            static (document, path) => ReadComparable(document, path),
            "The Runtime test continuation is invalid or belongs to another query."));
    }

    private static bool Matches(DocumentEnvelope document, DocumentQueryComparison comparison)
    {
        var expected = comparison.Values.Single();
        var actual = ReadComparable(document, comparison.Path);
        if (expected is null)
        {
            return comparison.Operator switch
            {
                QueryComparisonOperator.Equal => actual is null,
                QueryComparisonOperator.NotEqual => actual is not null,
                _ => throw new InvalidOperationException(
                    "Only Runtime test equality and inequality comparisons may use null.")
            };
        }
        if (actual is null)
            return false;
        var compared = StringComparer.Ordinal.Compare(actual, NormalizeComparable(comparison.Path, expected));
        return comparison.Operator switch
        {
            QueryComparisonOperator.Equal => compared == 0,
            QueryComparisonOperator.NotEqual => compared != 0,
            QueryComparisonOperator.StartsWith => actual.StartsWith(expected, StringComparison.Ordinal),
            QueryComparisonOperator.GreaterThan => compared > 0,
            QueryComparisonOperator.LessThanOrEqual => compared <= 0,
            _ => throw new InvalidOperationException(
                $"Runtime test range comparison '{comparison.Operator}' is unsupported.")
        };
    }

    private static int Compare(
        DocumentEnvelope left,
        DocumentEnvelope right,
        IReadOnlyList<DocumentQueryOrder> order)
    {
        foreach (var item in order)
        {
            var compared = StringComparer.Ordinal.Compare(
                ReadComparable(left, item.Path),
                ReadComparable(right, item.Path));
            if (compared != 0)
            {
                return item.Direction == PhysicalSortDirection.Descending
                    ? -compared
                    : compared;
            }
        }

        return StringComparer.Ordinal.Compare(left.Id, right.Id);
    }

    private static string? ReadComparable(DocumentEnvelope document, string path)
    {
        if (StringComparer.Ordinal.Equals(path, PhysicalDocumentFieldPaths.Id))
            return document.Id;

        using var content = JsonDocument.Parse(document.ContentJson);
        var value = GetPropertyPath(content.RootElement, path);
        if (value is null || value.Value.ValueKind == JsonValueKind.Null)
            return null;
        if (DateTimeFields.Contains(path))
            return value.Value.GetDateTimeOffset().UtcTicks.ToString("D19", CultureInfo.InvariantCulture);
        if (Int64Fields.Contains(path))
            return value.Value.GetInt64().ToString("D20", CultureInfo.InvariantCulture);
        return value.Value.ValueKind switch
        {
            JsonValueKind.Number => value.Value.GetRawText(),
            JsonValueKind.String => value.Value.GetString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => throw new InvalidOperationException(
                $"Runtime test query field '{path}' has unsupported JSON kind '{value.Value.ValueKind}'.")
        };
    }

    private static string NormalizeComparable(string path, string value) =>
        DateTimeFields.Contains(path)
            ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
                .UtcTicks.ToString("D19", CultureInfo.InvariantCulture)
            : Int64Fields.Contains(path)
                ? long.Parse(value, CultureInfo.InvariantCulture)
                    .ToString("D20", CultureInfo.InvariantCulture)
            : BooleanFields.Contains(path)
                ? bool.Parse(value).ToString()
            : value;

    private static JsonElement? GetPropertyPath(JsonElement root, string path) =>
        path.Split('.').Aggregate<string, JsonElement?>(
            root,
            (current, segment) =>
                current is { ValueKind: JsonValueKind.Object } &&
                current.Value.TryGetProperty(segment, out var child)
                    ? child
                    : null);

    public async Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await QueryAsync(query, cancellationToken)).TotalCount;

    public async Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        (await QueryAsync(query.Page(query.Skip, 1), cancellationToken)).Documents.FirstOrDefault();

    public async Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
        await FirstOrDefaultAsync(query, cancellationToken) is not null;

    private static bool IsOrderedRangeQuery(DocumentQuery query) =>
        query.Order.Count > 0 ||
        query.DocumentKind switch
        {
            ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind =>
                PostCommitOutboxOrderedRangeQueries.Contains(query.QueryIdentity),
            ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind =>
                WorkflowDispatchOrderedRangeQueries.Contains(query.QueryIdentity),
            ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind =>
                RecoveryOrderedRangeQueries.Contains(query.QueryIdentity),
            ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind =>
                query.QueryIdentity is ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery
                    or ElsaRuntimeStorageManifest.ListPendingSchedulerWorkflowExecutionsQuery,
            ElsaRuntimeStorageManifest.DurableTimerDocumentKind =>
                query.QueryIdentity == ElsaRuntimeStorageManifest.ClaimDueDurableTimersQuery,
            ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind =>
                query.QueryIdentity is
                    ElsaRuntimeStorageManifest.PageActivityExecutionStatesByWorkflowExecutionQuery or
                    ElsaRuntimeStorageManifest.PageActivityExecutionStatesByParentQuery,
            ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind =>
                query.QueryIdentity ==
                ElsaRuntimeStorageManifest.PageActivityExecutionInspectionSummariesQuery,
            ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind =>
                query.QueryIdentity is
                    ElsaRuntimeStorageManifest.FindLatestActivityExecutionHierarchyByWorkflowQuery or
                    ElsaRuntimeStorageManifest.PageActivityExecutionHierarchyByScopeQuery,
            ElsaRuntimeStorageManifest.BookmarkStateDocumentKind or
            ElsaRuntimeStorageManifest.DurableValueStateDocumentKind =>
                query.QueryIdentity == ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery &&
                query.Order.Count > 0,
            ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind =>
                query.QueryIdentity is ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery or
                    ElsaRuntimeStorageManifest.ListTriggerBindingsByArtifactQuery or
                    ElsaRuntimeStorageManifest.ListTriggerBindingsByStimulusTypeQuery,
            _ => false
        };

    private static readonly HashSet<string> PostCommitOutboxOrderedRangeQueries =
    [
        ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxQuery,
        ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowQuery,
        ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByIntentKindQuery,
        ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowAndIntentKindQuery,
        ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxQuery,
        ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxByWorkflowQuery,
        ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxByIntentKindQuery,
        ElsaRuntimeStorageManifest.ListClaimablePostCommitOutboxByWorkflowAndIntentKindQuery,
        ElsaRuntimeStorageManifest.ListImmediatePostCommitOutboxQuery,
        ElsaRuntimeStorageManifest.ListImmediatePostCommitOutboxByWorkflowQuery,
        ElsaRuntimeStorageManifest.ListImmediatePostCommitOutboxByIntentKindQuery,
        ElsaRuntimeStorageManifest.ListImmediatePostCommitOutboxByWorkflowAndIntentKindQuery,
        ElsaRuntimeStorageManifest.ListExpiredPostCommitOutboxClaimsQuery,
        ElsaRuntimeStorageManifest.ListExpiredPostCommitOutboxClaimsByWorkflowQuery,
        ElsaRuntimeStorageManifest.ListExpiredPostCommitOutboxClaimsByIntentKindQuery,
        ElsaRuntimeStorageManifest.ListExpiredPostCommitOutboxClaimsByWorkflowAndIntentKindQuery
    ];

    private static readonly HashSet<string> WorkflowDispatchOrderedRangeQueries =
    [
        ElsaRuntimeStorageManifest.PageWorkflowDispatchesByParentQuery,
        ElsaRuntimeStorageManifest.PageWorkflowDispatchesByStatusQuery,
        ElsaRuntimeStorageManifest.PageWorkflowDispatchesByTestScopeQuery,
        ElsaRuntimeStorageManifest.PageWorkflowDispatchesByParentAndStatusQuery,
        ElsaRuntimeStorageManifest.PageWorkflowDispatchesByParentAndTestScopeQuery,
        ElsaRuntimeStorageManifest.PageWorkflowDispatchesByStatusAndTestScopeQuery,
        ElsaRuntimeStorageManifest.PageWorkflowDispatchesByParentStatusAndTestScopeQuery
    ];

    private static readonly HashSet<string> RecoveryOrderedRangeQueries =
    [
        ElsaRuntimeStorageManifest.ListRecoveryDetectedQuery,
        ElsaRuntimeStorageManifest.ListRecoveryDetectedByLeaseOwnerQuery,
        ElsaRuntimeStorageManifest.ListRecoveryDetectedByHeartbeatOwnerQuery,
        ElsaRuntimeStorageManifest.ListRecoveryDetectedOwnerlessQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByLeaseExpiryAndOwnerQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByLeaseAcquisitionAndOwnerQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatQuery,
        ElsaRuntimeStorageManifest.ListRecoveryByHeartbeatAndOwnerQuery
    ];

    private static readonly HashSet<string> DateTimeFields =
    [
        ElsaRuntimeStorageManifest.WorkflowDispatchCreatedAtField,
        ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryScheduledAtField,
        ElsaRuntimeStorageManifest.ExpiresAtField,
        ElsaRuntimeStorageManifest.PostCommitOutboxDeliverableAtField,
        ElsaRuntimeStorageManifest.PostCommitOutboxClaimableAtField,
        ElsaRuntimeStorageManifest.PostCommitOutboxAvailableAtField,
        ElsaRuntimeStorageManifest.PostCommitOutboxVisibleAfterField,
        ElsaRuntimeStorageManifest.PostCommitOutboxRecordedAtField,
        ElsaRuntimeStorageManifest.RecoveryInterruptedAtField,
        ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField,
        ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField,
        ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField,
        ElsaRuntimeStorageManifest.RecurringTriggerScheduleNextOccurrenceField
    ];

    private static readonly HashSet<string> Int64Fields =
    [
        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyExecutionSequenceField,
        ElsaRuntimeStorageManifest.ActivityExecutionInspectionSummaryExecutionSequenceField
    ];

    private static readonly HashSet<string> BooleanFields =
    [
        ElsaRuntimeStorageManifest.ActivityExecutionHierarchyIsScopeRootField,
        ElsaRuntimeStorageManifest.WorkflowTriggerBindingIsActiveField,
        ElsaRuntimeStorageManifest.IsRetiredField,
        ElsaRuntimeStorageManifest.RecurringTriggerScheduleIsActiveField
    ];
}
