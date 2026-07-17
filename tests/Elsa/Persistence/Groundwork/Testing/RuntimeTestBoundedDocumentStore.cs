using System.Globalization;
using System.Text.Json;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Testing;

/// <summary>
/// Adapts standalone provider fixtures to Runtime's admitted bounded-query contract. Production hosts obtain
/// the equivalent route-bound query runtime from their provider initializer.
/// </summary>
public sealed class RuntimeTestBoundedDocumentStore(IDocumentStore documents) : IBoundedDocumentStore
{
    public async Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
    {
        if (IsOrderedRangeQuery(query))
            return await QueryOrderedRangeAsync(query, cancellationToken);

        var (index, path) = query.QueryIdentity switch
        {
            ElsaRuntimeStorageManifest.ListAllQuery =>
                (ElsaRuntimeStorageManifest.ByCollectionIndex, ElsaRuntimeStorageManifest.CollectionField),
            ElsaRuntimeStorageManifest.ListByWorkflowExecutionQuery =>
                (ElsaRuntimeStorageManifest.ByWorkflowExecutionIndex, ElsaRuntimeStorageManifest.WorkflowExecutionIdField),
            ElsaRuntimeStorageManifest.ListByArtifactQuery =>
                (ElsaRuntimeStorageManifest.ByArtifactIndex, ElsaRuntimeStorageManifest.ArtifactIdField),
            ElsaRuntimeStorageManifest.ListByParentActivityExecutionQuery =>
                (ElsaRuntimeStorageManifest.ByParentActivityExecutionIndex, ElsaRuntimeStorageManifest.ParentActivityExecutionIdField),
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusQuery =>
                (ElsaRuntimeStorageManifest.ByStimulusIndex, ElsaRuntimeStorageManifest.StimulusHashField),
            ElsaRuntimeStorageManifest.ListBookmarksByStimulusTypeQuery =>
                (ElsaRuntimeStorageManifest.ByStimulusTypeIndex, ElsaRuntimeStorageManifest.StimulusTypeField),
            ElsaRuntimeStorageManifest.FindExecutableActivityTemplateByHashQuery =>
                (ElsaRuntimeStorageManifest.ByTemplateHashIndex, ElsaRuntimeStorageManifest.TemplateHashField),
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByParentQuery =>
                (ElsaRuntimeStorageManifest.ByParentWorkflowExecutionIndex, ElsaRuntimeStorageManifest.ParentWorkflowExecutionIdField),
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByChildQuery =>
                (ElsaRuntimeStorageManifest.ByChildWorkflowExecutionIndex, ElsaRuntimeStorageManifest.ChildWorkflowExecutionIdField),
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByStatusQuery =>
                (ElsaRuntimeStorageManifest.ByStatusIndex, ElsaRuntimeStorageManifest.StatusField),
            ElsaRuntimeStorageManifest.ListWorkflowDispatchesByTestScopeQuery =>
                (ElsaRuntimeStorageManifest.ByTestScopeIndex, ElsaRuntimeStorageManifest.TestScopeIdField),
            "list-by-execution-scope" =>
                (ElsaRuntimeStorageManifest.ByExecutionScopeIndex, ElsaRuntimeStorageManifest.ExecutionScopeIdField),
            ElsaRuntimeStorageManifest.ListTriggerBindingsByPublicationQuery =>
                (ElsaRuntimeStorageManifest.ByPublicationIndex, ElsaRuntimeStorageManifest.PublicationIdField),
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

#pragma warning disable GW0004
        var matches = await documents.QueryAsync(
            new DocumentStoreQuery(query.DocumentKind, index, comparison.Values[0]!),
            cancellationToken);
#pragma warning restore GW0004
        IEnumerable<DocumentEnvelope> page = matches.Skip(query.Skip ?? 0);
        if (query.Take is { } take)
            page = page.Take(take);
        return new DocumentQueryResult(page.ToArray(), matches.Count);
    }

    private async Task<DocumentQueryResult> QueryOrderedRangeAsync(
        DocumentQuery query,
        CancellationToken cancellationToken)
    {
#pragma warning disable GW0004
        var all = await documents.QueryAsync(new PortableDocumentQuery(query.DocumentKind), cancellationToken);
#pragma warning restore GW0004
        var comparisons = query.Clauses.SelectMany(clause => clause.Comparisons).ToArray();
        var matches = all.Documents
            .Where(document => comparisons.All(comparison => Matches(document, comparison)))
            .ToArray();
        Array.Sort(matches, (left, right) => Compare(left, right, query.Order));
        IEnumerable<DocumentEnvelope> page = matches.Skip(query.Skip ?? 0);
        if (query.Take is { } take)
            page = page.Take(take);
        return new DocumentQueryResult(page.ToArray(), matches.Length);
    }

    private static bool Matches(DocumentEnvelope document, DocumentQueryComparison comparison)
    {
        var expected = comparison.Values.Single();
        var actual = ReadComparable(document, comparison.Path);
        if (comparison.Operator == QueryComparisonOperator.Equal && expected is null)
            return actual is null;
        if (expected is null)
            throw new InvalidOperationException("Only Runtime test equality comparisons may use null.");
        if (actual is null)
            return false;
        var compared = StringComparer.Ordinal.Compare(actual, NormalizeComparable(comparison.Path, expected));
        return comparison.Operator switch
        {
            QueryComparisonOperator.Equal => compared == 0,
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
        var compared = order
            .Select(item => StringComparer.Ordinal.Compare(
                ReadComparable(left, item.Path),
                ReadComparable(right, item.Path)))
            .FirstOrDefault(result => result != 0);
        return compared != 0
            ? compared
            : StringComparer.Ordinal.Compare(left.Id, right.Id);
    }

    private static string? ReadComparable(DocumentEnvelope document, string path)
    {
        using var content = JsonDocument.Parse(document.ContentJson);
        var value = GetPropertyPath(content.RootElement, path);
        if (value is null || value.Value.ValueKind == JsonValueKind.Null)
            return null;
        if (DateTimeFields.Contains(path))
            return value.Value.GetDateTimeOffset().UtcTicks.ToString("D19", CultureInfo.InvariantCulture);
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
            _ => false
        };

    private static readonly HashSet<string> PostCommitOutboxOrderedRangeQueries =
    [
        ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxQuery,
        ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowQuery,
        ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByIntentKindQuery,
        ElsaRuntimeStorageManifest.ListDeliverablePostCommitOutboxByWorkflowAndIntentKindQuery,
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
        ElsaRuntimeStorageManifest.PostCommitOutboxAvailableAtField,
        ElsaRuntimeStorageManifest.PostCommitOutboxVisibleAfterField,
        ElsaRuntimeStorageManifest.PostCommitOutboxRecordedAtField,
        ElsaRuntimeStorageManifest.RecoveryInterruptedAtField,
        ElsaRuntimeStorageManifest.RecoveryLeaseAcquiredAtField,
        ElsaRuntimeStorageManifest.RecoveryLeaseExpiresAtField,
        ElsaRuntimeStorageManifest.RecoveryHeartbeatRecordedAtField
    ];
}
