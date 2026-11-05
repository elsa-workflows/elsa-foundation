using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Events.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Events;
using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Management.Query;

internal sealed class DashboardQueryService(
    IInlineEventPublisher events, DashboardReadGate gate, DashboardQueryLimits limits, TimeProvider clock) : IDashboardQueryService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 32
    };

    public async ValueTask<IReadOnlyList<DashboardDatasetDeclaration>> DiscoverAsync(DashboardRequestScope scope, CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        using var admission = gate.Enter(scope, ValidateLimits());
        using var deadline = new CancellationTokenSource(limits.ExecutionTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try { return await DiscoverCoreAsync(scope, linked.Token); }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw Timeout(exception); }
    }

    public async ValueTask<DashboardQueryValidation> ValidateAsync(DashboardRequestScope scope, DashboardQueryRequest request, CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        using var admission = gate.Enter(scope, ValidateLimits());
        using var deadline = new CancellationTokenSource(limits.ExecutionTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            var (_, validation) = await PrepareAsync(scope, request, clock.GetUtcNow(), linked.Token);
            return validation;
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw Timeout(exception); }
    }

    public async ValueTask<DashboardQueryResult> ExecuteAsync(DashboardRequestScope scope, DashboardQueryRequest request, CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        using var admission = gate.Enter(scope, ValidateLimits());
        using var deadline = new CancellationTokenSource(limits.ExecutionTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            var (context, validation) = await PrepareAsync(scope, request, clock.GetUtcNow(), linked.Token);
            RequireValid(context, validation);
            var executing = new DashboardQueryExecuting(context!, limits);
            await PublishAsync(executing, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return AdmitResult(Single(executing.Results), context!, context!.Request.Query.Limit);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw Timeout(exception); }
    }

    public async ValueTask<DashboardQueryResult> DrillAsync(DashboardRequestScope scope, DashboardDrillRequest request, CancellationToken cancellationToken = default)
    {
        ValidateScope(scope);
        using var admission = gate.Enter(scope, ValidateLimits());
        using var deadline = new CancellationTokenSource(limits.ExecutionTimeout, clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            var now = clock.GetUtcNow();
            if (request?.Descriptor is not { } descriptor || descriptor.ObservedAt is not { } observedAt || observedAt > now || now - observedAt > TimeSpan.FromDays(1) ||
                request.PageSize is < 1 or > 100 || request.Cursor?.Length > 4096 || descriptor.Token?.Length > 4096 || descriptor.GroupValues is null || descriptor.GroupValues.Count > 2)
                throw InvalidDrill();
            var (context, validation) = await PrepareAsync(scope, request.Question, observedAt, linked.Token);
            RequireValid(context, validation);
            if (!context!.Dataset.SupportsDrill || descriptor.DatasetId != context.Dataset.Id || descriptor.SchemaRevision != context.Dataset.SchemaRevision ||
                descriptor.QueryFingerprint != context.QueryFingerprint || descriptor.Periods is null || !descriptor.Periods.SequenceEqual(context.Periods) ||
                !context.Periods.Any(period => period.Role == descriptor.Period) && !(context.Periods.Count == 0 && descriptor.Period == "current") ||
                descriptor.GroupValues.Count != context.Request.Query.GroupBy.Count ||
                descriptor.GroupValues.Keys.Any(key => !context.Request.Query.GroupBy.Contains(key, StringComparer.Ordinal)))
                throw InvalidDrill();
            var drilling = new DashboardQueryDrilling(context, request, limits);
            await PublishAsync(drilling, linked.Token);
            linked.Token.ThrowIfCancellationRequested();
            return AdmitResult(Single(drilling.Results), context, request.PageSize);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && deadline.IsCancellationRequested)
        { throw Timeout(exception); }
    }

    private async ValueTask<(DashboardQueryContext? Context, DashboardQueryValidation Validation)> PrepareAsync(
        DashboardRequestScope scope, DashboardQueryRequest request, DateTimeOffset evaluatedAt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var shapeProblems = DashboardQueryValidator.Shape(request, limits);
        if (shapeProblems.Count != 0) return (null, new(false, shapeProblems));
        byte[] bytes;
        DashboardQueryRequest snapshot;
        try
        {
            bytes = JsonSerializer.SerializeToUtf8Bytes(request, Json);
            if (bytes.Length > limits.MaximumDefinitionBytes)
                return (null, new(false, [new("QUERY_LIMIT", "", "The question exceeds its serialized size bound.")]));
            snapshot = JsonSerializer.Deserialize<DashboardQueryRequest>(bytes, Json)!;
        }
        catch (JsonException exception)
        { throw new DashboardQueryException("INVALID_QUERY", "The query could not be read as a bounded definition.", exception); }
        var datasets = await DiscoverCoreAsync(scope, cancellationToken);
        var dataset = datasets.SingleOrDefault(item => item.Id == snapshot.Query.Dataset.Id);
        if (dataset is null)
            return (null, new(false, [new("DATASET_UNAVAILABLE", "query.dataset", "This dataset is unavailable in the current scope.")]));
        var declarationProblems = DashboardQueryValidator.Declaration(snapshot, dataset);
        if (declarationProblems.Count != 0) return (null, new(false, declarationProblems));
        IReadOnlyList<DashboardEffectivePeriod> periods;
        try { periods = DashboardPeriods.Resolve(snapshot.Query, evaluatedAt); }
        catch (DashboardQueryException exception)
        { return (null, new(false, [new(exception.Code, "query.time", exception.Message)])); }
        var parameters = DashboardQueryValidator.Parameters(snapshot);
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { scope, request = snapshot, parameters, evaluatedAt, periods }, Json))).ToLowerInvariant();
        var context = new DashboardQueryContext(scope, snapshot, dataset, parameters, fingerprint, evaluatedAt, periods);
        var validating = new DashboardQueryValidating(context);
        await PublishAsync(validating, cancellationToken);
        var validation = Single(validating.Results);
        if (validation.Problems is null || validation.Problems.Count > 50 || validation.IsValid && validation.Problems.Count != 0)
            throw new DashboardQueryException("INVALID_SOURCE_RESULT", "The dataset returned an invalid validation outcome.");
        return (context, validation);
    }

    private async ValueTask<IReadOnlyList<DashboardDatasetDeclaration>> DiscoverCoreAsync(DashboardRequestScope scope, CancellationToken cancellationToken)
    {
        var collecting = new DashboardDatasetsCollecting(scope);
        await PublishAsync(collecting, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (collecting.Datasets.Count > 100) throw new DashboardQueryException("DISCOVERY_LIMIT", "The dataset catalog exceeds its configured bound.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dataset in collecting.Datasets)
        {
            if (dataset is null || !DashboardQueryValidator.Identifier(dataset.Id) || !ids.Add(dataset.Id) ||
                !DashboardQueryValidator.Identifier(dataset.SchemaRevision) || !DashboardQueryValidator.Identifier(dataset.OwnerModuleId) ||
                dataset.Fields is null || dataset.Fields.Count > 128 || dataset.Metrics is null || dataset.Metrics.Count > 32 ||
                dataset.Relationships is null || dataset.Relationships.Count > 16 || dataset.Forms is null ||
                dataset.Availability is not ("ready" or "unavailable") || dataset.Title?.Length > 200 || dataset.Description?.Length > 2000 ||
                dataset.Fields.Any(field => field is null || !DashboardQueryValidator.Identifier(field.Id) || field.FilterOperators is null) ||
                dataset.Fields.Select(field => field.Id).Distinct(StringComparer.Ordinal).Count() != dataset.Fields.Count ||
                dataset.Metrics.Any(metric => metric is null || !DashboardQueryValidator.Identifier(metric.Id) || !DashboardQueryValidator.Identifier(metric.Revision)) ||
                dataset.Metrics.Select(metric => metric.Id).Distinct(StringComparer.Ordinal).Count() != dataset.Metrics.Count)
                throw new DashboardQueryException("INVALID_DATASET_DECLARATION", "A dataset supplied an invalid or ambiguous declaration.");
        }
        try
        {
            if (JsonSerializer.SerializeToUtf8Bytes(collecting.Datasets, Json).Length > 262144)
                throw new DashboardQueryException("DISCOVERY_LIMIT", "The dataset catalog exceeds its serialized bound.");
            return collecting.Datasets.OrderBy(dataset => dataset.Id, StringComparer.Ordinal)
                .Select(dataset => JsonSerializer.Deserialize<DashboardDatasetDeclaration>(JsonSerializer.SerializeToUtf8Bytes(dataset, Json), Json)!).ToArray();
        }
        catch (JsonException exception)
        { throw new DashboardQueryException("INVALID_DATASET_DECLARATION", "A dataset declaration could not be read.", exception); }
    }

    private DashboardQueryResult AdmitResult(DashboardQueryResult result, DashboardQueryContext context, int maximumRows)
    {
        if (result is null || result.Rows is null || result.Columns is null || result.Rows.Count > maximumRows || result.Columns.Count > 16 ||
            result.Coverage is null || result.Coverage.Status is not ("complete" or "incomplete" or "unknown") ||
            result.Outcome is not ("succeeded" or "unavailable" or "incompatible" or "denied" or "limitExceeded" or "cancelled" or "failed") ||
            result.Outcome != "succeeded" && result.Rows.Count != 0 ||
            result.DatasetId.Length != 0 && result.DatasetId != context.Dataset.Id ||
            result.SchemaRevision.Length != 0 && result.SchemaRevision != context.Dataset.SchemaRevision ||
            result.QueryFingerprint.Length != 0 && result.QueryFingerprint != context.QueryFingerprint)
            throw new DashboardQueryException("INVALID_SOURCE_RESULT", "The dataset returned a result outside the validated query contract.");
        var columns = new HashSet<string>(StringComparer.Ordinal);
        foreach (var column in result.Columns)
            if (column is null || !DashboardQueryValidator.Identifier(column.Id) || !columns.Add(column.Id))
                throw new DashboardQueryException("INVALID_SOURCE_RESULT", "The dataset returned invalid result columns.");
        foreach (var row in result.Rows)
            if (row is null || row.Values is null || row.Values.Count != columns.Count || row.Values.Keys.Any(key => !columns.Contains(key)) ||
                row.Values.Values.Any(cell => cell is null || cell.Value is null && string.IsNullOrWhiteSpace(cell.NullReason)))
                throw new DashboardQueryException("INVALID_SOURCE_RESULT", "The dataset returned invalid result values.");
        return result with
        {
            QueryFingerprint = context.QueryFingerprint,
            DatasetId = context.Dataset.Id,
            SchemaRevision = context.Dataset.SchemaRevision,
            GeneratedAt = clock.GetUtcNow(),
            Periods = context.Periods
        };
    }

    private async Task PublishAsync(IEvent context, CancellationToken cancellationToken)
    {
        try { await events.Publish(context, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (DashboardQueryException) { throw; }
        catch (Exception exception)
        { throw new DashboardQueryException("QUERY_SOURCE_FAILED", "The dataset could not complete this operation. Retry or contact its module owner.", exception); }
    }

    private static T Single<T>(ICollection<T> results)
    {
        if (results.Count != 1 || results.Single() is not { } result)
            throw new DashboardQueryException("DATASET_OWNER_CONFLICT", "The query operation did not produce exactly one authoritative outcome.");
        return result;
    }

    private static void RequireValid(DashboardQueryContext? context, DashboardQueryValidation validation)
    {
        if (context is null || !validation.IsValid)
            throw new DashboardQueryException(validation.Problems.FirstOrDefault()?.Code ?? "INVALID_QUERY", validation.Problems.FirstOrDefault()?.Message ?? "This query is unsupported.");
    }

    private int ValidateLimits()
    {
        if (limits.MaximumConcurrentReads is < 1 or > 2 || limits.MaximumSourceRows is < 1 or > 100000 || limits.MaximumGroups is < 1 or > 200 ||
            limits.MaximumResultRows is < 1 or > 200 || limits.MaximumDefinitionBytes is < 1 or > 65536 ||
            limits.ExecutionTimeout <= TimeSpan.Zero || limits.ExecutionTimeout > TimeSpan.FromSeconds(10))
            throw new DashboardQueryException("INVALID_QUERY_LIMITS", "Dashboard query limits must stay within the supported hard bounds.");
        return limits.MaximumConcurrentReads;
    }

    private static void ValidateScope(DashboardRequestScope scope)
    {
        if (scope is null || !ScopePart(scope.TenantId) || !ScopePart(scope.SubjectId) || !ScopePart(scope.HostId))
            throw new DashboardQueryException("QUERY_SCOPE_REQUIRED", "A trusted subject, tenant and host scope are required.");
        static bool ScopePart(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 256 && !value.Any(char.IsControl);
    }

    private static DashboardQueryException InvalidDrill() => new("INVALID_DRILL", "This drill request is expired, changed or unavailable. Refresh the question and select the group again.");
    private static DashboardQueryException Timeout(Exception cause) => new("QUERY_TIMEOUT", "The dashboard read exceeded its execution deadline. Narrow the question or retry.", cause);
}
