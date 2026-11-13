using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Events;
using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;
using Elsa.Studio.Dashboard.Core.Resources;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Inspection.Core;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Dashboard.Queries;

internal sealed class WorkflowDashboardQuerySource(
    IWorkflowExecutionStateStore executions,
    IActivityInspectionContextAsync inspection,
    IDashboardActorContext actors,
    IHttpContextAccessor http,
    IPermissionAuthorizationService permissions,
    IWorkflowDefinitionAuthorizer? definitions = null,
    IWorkflowLibraryStore? library = null)
    : IDashboardDatasetExtension, IDashboardQueryValidationExtension, IDashboardQueryExecutionExtension, IDashboardQueryDrillExtension
{
    private const string DatasetId = "elsa.workflows.runs";
    private const string SchemaRevision = "runs-v1";
    private const string OwnerId = "Elsa.Workflows.Dashboard.Queries";
    private static readonly byte[] SigningKey = RandomNumberGenerator.GetBytes(32);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string[] Equality = ["eq", "ne", "in", "notIn", "contains", "startsWith"];
    private static readonly string[] Ordered = ["eq", "ne", "gt", "gte", "lt", "lte", "in", "notIn", "isNull", "isNotNull"];

    public IReadOnlyCollection<DashboardDatasetOwner> Ownership => [new(DatasetId, OwnerId)];

    public async ValueTask CollectAsync(DashboardDatasetsCollecting context, CancellationToken cancellationToken)
    {
        if (await CanReadAsync(context.Scope, null, cancellationToken)) context.Datasets.Add(Declaration());
    }

    public async ValueTask ValidateAsync(DashboardQueryValidating context, CancellationToken cancellationToken)
    {
        var problems = new List<DashboardQueryProblem>();
        if (!await CanReadAsync(context.Query.Scope, context.Query.Request.Query, cancellationToken))
            problems.Add(new("DATASET_UNAVAILABLE", "query.dataset", "Workflow question data is unavailable in the current scope."));
        var query = context.Query.Request.Query;
        if (query.Form == "aggregate" && (query.Time?.Field != "startedAt" || context.Query.Periods.Count == 0))
            problems.Add(new("UNSUPPORTED_QUERY", "query.time", "Workflow aggregates require an explicit started-at cohort window."));
        if (query.GroupBy.Contains("startedAt", StringComparer.Ordinal) && (query.Time?.Bucket is null || query.Comparison is not null))
            problems.Add(new("UNSUPPORTED_QUERY", "query.groupBy", "Time grouping requires an explicit hour/day bucket and currently supports a single period. Use definition/folder grouping for comparisons."));
        if (query.GroupBy.Any(field => field is "completedAt" or "durationMs"))
            problems.Add(new("UNSUPPORTED_QUERY", "query.groupBy", "This field is not a supported workflow grouping dimension."));
        context.Results.Add(new(problems.Count == 0, problems, Interpretation(context.Query)));
    }

    public async ValueTask ExecuteAsync(DashboardQueryExecuting context, CancellationToken cancellationToken)
    {
        await RequireReadAsync(context.Query, cancellationToken);
        var rows = await ReadAsync(context.Query, context.Limits, cancellationToken);
        context.Results.Add(BuildResult(context.Query, rows, context.Limits, cancellationToken));
    }

    public async ValueTask DrillAsync(DashboardQueryDrilling context, CancellationToken cancellationToken)
    {
        await RequireReadAsync(context.Query, cancellationToken);
        var descriptor = context.Request.Descriptor;
        if (!FixedEquals(descriptor.Token, Sign(DrillMaterial(context.Query, descriptor.GroupValues))))
            throw InvalidDrill();
        var fields = new List<string> { "executionId", "definitionId", "status", "runKind", "startedAt", "completedAt", "durationMs" };
        if (NeedsOrganization(context.Query.Request.Query)) fields.AddRange(["currentFolderId", "currentFolder"]);
        var selectedPeriod = context.Query.Periods.SingleOrDefault(period => period.Role == descriptor.Period);
        var recordProjection = context.Query.Request.Query with
        {
            Form = "records", Fields = fields, GroupBy = [], Metrics = [], Having = null, Sort = [],
            Comparison = null, Limit = context.Request.PageSize,
            Time = selectedPeriod is null ? null : new()
            {
                Field = "startedAt", From = selectedPeriod.From, To = selectedPeriod.To
            }
        };
        // Aggregate authority does not imply permission to disclose its underlying record fields.
        if (!await CanReadAsync(context.Query.Scope, recordProjection, cancellationToken))
            throw new DashboardQueryException("DATASET_UNAVAILABLE", "Workflow record details are unavailable in the current scope.");
        var rows = await ReadAsync(context.Query, context.Limits, cancellationToken);
        var computed = BuildResult(context.Query, rows, context.Limits, cancellationToken);
        var key = GroupKey(context.Query.Request.Query.GroupBy, descriptor.GroupValues);
        if (!computed.Rows.Any(row => row.Key == key)) throw InvalidDrill();
        var selected = rows.Where(row => InPeriod(row, context.Query.Periods, descriptor.Period) &&
                                       GroupKey(context.Query.Request.Query.GroupBy, GroupValues(context.Query, row)) == key)
            .OrderBy(row => row.State.WorkflowExecutionId, StringComparer.Ordinal).ToArray();
        var binding = Sign(DrillMaterial(context.Query, descriptor.GroupValues) + ":" + descriptor.Period);
        var offset = ReadCursor(context.Request.Cursor, binding);
        if (offset > selected.Length) throw InvalidDrill();
        var page = selected.Skip(offset).Take(context.Request.PageSize).ToArray();
        var result = Records(context.Query, page, fields);
        context.Results.Add(result with { NextCursor = offset + page.Length < selected.Length ? WriteCursor(offset + page.Length, binding) : null });
    }

    private async ValueTask<bool> CanReadAsync(DashboardRequestScope scope, DashboardQueryDefinition? query, CancellationToken cancellationToken)
    {
        var actor = await actors.GetAsync(cancellationToken);
        var principal = http.HttpContext?.User;
        if (principal?.Identity?.IsAuthenticated != true || actor.TenantId != scope.TenantId || actor.UserId != scope.SubjectId ||
            inspection.TenantScope != "tenant:" + scope.TenantId || string.IsNullOrWhiteSpace(inspection.AuditSubject) ||
            inspection.AuditSubject != (actor.AuthenticationSubjectId ?? actor.UserId)) return false;
        var decision = await permissions.AuthorizeAsync(new(principal, WorkflowDashboardQueryPermissions.Read, scope.TenantId,
            query is null ? new DashboardDatasetReference(DatasetId, SchemaRevision) : query), cancellationToken);
        return decision.Succeeded;
    }

    private async ValueTask RequireReadAsync(DashboardQueryContext query, CancellationToken cancellationToken)
    {
        if (!await CanReadAsync(query.Scope, query.Request.Query, cancellationToken))
            throw new DashboardQueryException("DATASET_UNAVAILABLE", "Workflow question data is unavailable in the current scope.");
    }

    private DashboardDatasetDeclaration Declaration()
    {
        var fields = new List<DashboardFieldDefinition>
        {
            new("executionId", "Run", "string", Equality, Sortable: true),
            new("definitionId", "Workflow definition", "string", Equality, Groupable: true, Sortable: true),
            new("status", "Current status", "string", ["eq", "ne", "in", "notIn"], Groupable: true, Sortable: true),
            new("runKind", "Run kind", "string", ["eq", "ne", "in", "notIn"], Groupable: true, Sortable: true,
                Description: "unknown is legacy/unclassified; excluding testRun does not imply published production runs."),
            new("startedAt", "Started at", "instant", Ordered, Nullable: true, Groupable: true, Sortable: true),
            new("completedAt", "Completed at", "instant", Ordered, Nullable: true, Sortable: true),
            new("durationMs", "Elapsed terminal duration", "decimal", Ordered, Nullable: true, Sortable: true, Unit: "milliseconds",
                Description: "Valid start/completion pairs of completed, faulted or cancelled runs. Missing or inverted pairs are ineligible; count counts eligible values and other aggregates return null without eligible values.",
                Aggregates: ["count", "sum", "average", "min", "max"])
        };
        if (definitions is not null && library is not null)
        {
            fields.Add(new("currentFolderId", "Current folder identity", "string", [.. Equality, "isNull", "isNotNull"], Nullable: true, Groupable: true, Sortable: true));
            fields.Add(new("currentFolder", "Current folder path", "string", [.. Equality, "isNull", "isNotNull"], Nullable: true, Groupable: true, Sortable: true,
                Description: "Current authorized Design folder path, not the folder when the run started. Null means genuinely unfiled; unreadable organization excludes the run before aggregation."));
        }
        return new()
        {
            Id = DatasetId, OwnerModuleId = OwnerId, SchemaRevision = SchemaRevision,
            Title = "Retained workflow runs", Description = "Current outcomes of retained, individually authorized workflow runs. Aggregate windows select actual start timestamps.",
            Fields = fields,
            Metrics = [
                new("workflow.started-count", "v1", "Started runs", "runs", "Eligible runs started in the selected cohort, including incomplete and explicitly selected run kinds.", true, ["startedAt"]),
                new("workflow.failed-count", "v1", "Currently faulted runs", "runs", "Runs in the started cohort whose current status is faulted.", true, ["startedAt", "status"]),
                new("workflow.failure-rate", "started-current-status-v1", "Current failure rate", "percent", "100 × currently faulted / eligible started runs; incomplete runs remain in the denominator. No denominator is null. Delta is percentage points; comparisons use exact ratios before display rounding.", true, ["startedAt", "status"]),
                new("workflow.average-duration-ms", "v1", "Average elapsed terminal duration", "milliseconds", "Average elapsed milliseconds for valid start/completion pairs of terminal runs in the started cohort; missing/inverted timestamps are excluded. No eligible duration is null.", true, ["startedAt", "completedAt", "durationMs"]),
                new("workflow.eligible-duration-count", "v1", "Eligible duration count", "runs", "Number of valid terminal start/completion pairs contributing to duration metrics.", true, ["durationMs"])
            ],
            Time = new(["startedAt"], ["absolute", "calendarWeekToDate", "last7Days", "last30Days"], ["hour", "day"], ["absolute", "previousCalendarWeekSameElapsed"]),
            Relationships = definitions is not null && library is not null ? [new("workflow.current-organization", "Current individually readable Design organization; no historical folder attribution.", "many-to-one")] : [],
            SupportsDrill = true, SupportsExport = false,
            CoverageDescription = "Unknown collection/retention coverage. Deleted runs and history not captured by this runtime are not reconstructed."
        };
    }

    private async ValueTask<IReadOnlyList<SourceRow>> ReadAsync(DashboardQueryContext query, DashboardQueryLimits limits, CancellationToken cancellationToken)
    {
        var result = new List<SourceRow>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var cursors = new HashSet<string>(StringComparer.Ordinal);
        var organization = new Dictionary<string, WorkflowOrganization?>(StringComparer.Ordinal);
        var needOrganization = NeedsOrganization(query.Request.Query);
        var examined = 0;
        string? cursor = null;
        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            // History From/To means effective update time, so it MUST NOT stand in for started-cohort selection.
            var page = await executions.QueryPageAsync(new(100, TenantId: query.Scope.TenantId, Cursor: cursor), cancellationToken);
            if (page.Items.Count > 100 || page.HasNext != (page.NextCursor is not null)) throw InvalidSource();
            foreach (var state in page.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++examined > limits.MaximumSourceRows) throw Limit();
                if (!ids.Add(state.WorkflowExecutionId)) throw InvalidSource();
                if (state.TenantId != query.Scope.TenantId || !await inspection.CanInspectStructureAsync(state, cancellationToken)) continue;
                if (!Enum.IsDefined(state.Status) || !Enum.IsDefined(state.RunKind)) throw InvalidSource();
                if (query.Periods.Count != 0 && !query.Periods.Any(period => state.StartedAt >= period.From && state.StartedAt < period.To)) continue;
                WorkflowOrganization? currentOrganization = null;
                var definitionId = state.PinnedSource?.DefinitionId ?? state.PinnedExecutable.DefinitionId;
                if (needOrganization)
                {
                    if (definitions is null || library is null)
                        throw new DashboardQueryException("DATASET_UNAVAILABLE", "Current workflow organization is unavailable.");
                    if (!organization.TryGetValue(definitionId, out currentOrganization))
                    {
                        if (++examined > limits.MaximumSourceRows) throw Limit();
                        currentOrganization = await definitions.CanReadAsync(definitionId, cancellationToken)
                            ? await library.GetOrganizationAsync(definitionId, cancellationToken) : null;
                        if (currentOrganization is not null)
                        {
                            examined = checked(examined + currentOrganization.FolderBreadcrumb.Count + currentOrganization.Tags.Count);
                            if (examined > limits.MaximumSourceRows) throw Limit();
                        }
                        organization.Add(definitionId, currentOrganization);
                    }
                    if (currentOrganization is null) continue;
                }
                var row = new SourceRow(state, Values(state, definitionId, currentOrganization));
                if (WorkflowQueryValues.Matches(query.Request.Query.Filters, field => row.Values[field], query)) result.Add(row);
            }
            cursor = page.NextCursor;
            if (cursor is not null && !cursors.Add(cursor)) throw InvalidSource();
        } while (cursor is not null);
        return result;
    }

    private static IReadOnlyDictionary<string, DashboardValue> Values(WorkflowExecutionState state, string definitionId, WorkflowOrganization? organization)
    {
        decimal? duration = state.Status.IsTerminal() && state.StartedAt is { } started && state.CompletedAt is { } completed && completed >= started
            ? (completed - started).Ticks / 10000m : null;
        return new Dictionary<string, DashboardValue>(StringComparer.Ordinal)
        {
            ["executionId"] = WorkflowQueryValues.String(state.WorkflowExecutionId),
            ["definitionId"] = WorkflowQueryValues.String(definitionId),
            ["status"] = WorkflowQueryValues.String(state.Status.ToString().ToLowerInvariant()),
            ["runKind"] = WorkflowQueryValues.String(state.RunKind switch { WorkflowRunKind.TestRun => "testRun", WorkflowRunKind.PublishedRun => "publishedRun", WorkflowRunKind.BackgroundWeaverRun => "backgroundWeaverRun", _ => "unknown" }),
            ["startedAt"] = WorkflowQueryValues.Instant(state.StartedAt),
            ["completedAt"] = WorkflowQueryValues.Instant(state.CompletedAt),
            ["durationMs"] = WorkflowQueryValues.Decimal(duration),
            ["currentFolderId"] = WorkflowQueryValues.String(organization?.FolderId),
            ["currentFolder"] = WorkflowQueryValues.String(organization?.FolderId is null ? null : "/" + string.Join("/", organization.FolderBreadcrumb.Select(folder => folder.Name)))
        };
    }

    private DashboardQueryResult BuildResult(DashboardQueryContext context, IReadOnlyList<SourceRow> rows, DashboardQueryLimits limits, CancellationToken cancellationToken)
    {
        var query = context.Request.Query;
        if (query.Form == "records")
            return Records(context, SortRecords(rows, query, cancellationToken).Take(query.Limit).ToArray(), query.Fields);
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        if (query.GroupBy.Count == 0) groups.Add(GroupKey(query.GroupBy, new Dictionary<string, DashboardValue>()), new(new Dictionary<string, DashboardValue>()));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = GroupValues(context, row);
            var key = GroupKey(query.GroupBy, values);
            if (!groups.TryGetValue(key, out var group))
            {
                if (groups.Count >= limits.MaximumGroups) throw Limit();
                group = new(values); groups.Add(key, group);
            }
            foreach (var period in context.Periods)
                if (InPeriod(row, context.Periods, period.Role))
                    (period.Role == "current" ? group.Current : group.Baseline).Add(row);
        }
        var calculated = groups.Select(pair => Calculate(context, pair.Key, pair.Value, cancellationToken)).Where(group => Having(query.Having, group.Metrics, context)).ToList();
        calculated.Sort((left, right) => { cancellationToken.ThrowIfCancellationRequested(); return CompareGroups(query, left, right); });
        var columns = query.GroupBy.Select(field => Column(context, field)).ToList();
        foreach (var metric in query.Metrics)
        {
            var declaration = metric.Metric is null ? null : context.Dataset.Metrics.Single(candidate => candidate.Id == metric.Metric);
            var unit = metric.Aggregate == "count" ? "runs" : declaration?.Unit ?? context.Dataset.Fields.Single(field => field.Id == metric.Field).Unit;
            var label = declaration?.Label ?? metric.Aggregate + " " + metric.Field;
            columns.Add(new(CurrentColumn(context, metric.Alias), label, "decimal", unit));
            if (query.Comparison is not null)
            {
                columns.Add(new("baseline." + metric.Alias, "Baseline " + label, "decimal", unit));
                columns.Add(new("delta." + metric.Alias, "Change in " + label, "decimal", unit == "percent" ? "percentage points" : unit));
            }
        }
        return BaseResult(context) with
        {
            Columns = columns,
            Rows = calculated.Take(query.Limit).Select(group => new DashboardResultRow(group.Key,
                columns.ToDictionary(column => column.Id, column => group.Values.TryGetValue(column.Id, out var value)
                    ? Cell(value, column.Type, "unfiled") : MetricCell(group.Metrics[MetricReference(column.Id)], query, column.Id), StringComparer.Ordinal),
                Descriptor(context, group.GroupValues))).ToArray()
        };
    }

    private static ComputedGroup Calculate(DashboardQueryContext context, string key, Group group, CancellationToken cancellationToken)
    {
        var metrics = new Dictionary<string, WorkflowQueryNumber?>(StringComparer.Ordinal);
        foreach (var selection in context.Request.Query.Metrics)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = Metric(selection, group.Current);
            var baseline = Metric(selection, group.Baseline);
            metrics.Add(selection.Alias, current);
            metrics.Add("baseline." + selection.Alias, baseline);
            metrics.Add("delta." + selection.Alias, current is not null && baseline is not null ? current.Subtract(baseline) : null);
        }
        return new(key, group.Values, metrics);
    }

    private static WorkflowQueryNumber? Metric(DashboardMetricSelection selection, IReadOnlyCollection<SourceRow> rows)
    {
        if (selection.Metric == "workflow.started-count") return WorkflowQueryNumber.From(rows.Count);
        if (selection.Metric == "workflow.failed-count") return WorkflowQueryNumber.From(rows.Count(row => row.State.Status == WorkflowExecutionStatus.Faulted));
        if (selection.Metric == "workflow.failure-rate") return rows.Count == 0 ? null : new(rows.Count(row => row.State.Status == WorkflowExecutionStatus.Faulted) * 100, rows.Count);
        var field = selection.Metric is null ? selection.Field! : "durationMs";
        var values = rows.Select(row => row.Values[field]).Where(value => value.Type != "null")
            .Select(value => decimal.Parse(value.Literal!, CultureInfo.InvariantCulture)).ToArray();
        var aggregate = selection.Metric switch { "workflow.eligible-duration-count" => "count", "workflow.average-duration-ms" => "average", _ => selection.Aggregate };
        if (aggregate == "count") return WorkflowQueryNumber.From(values.Length);
        if (values.Length == 0) return null;
        return aggregate switch
        {
            "sum" => WorkflowQueryNumber.From(values.Sum()),
            "average" => new(WorkflowQueryNumber.From(values.Sum()).Numerator, WorkflowQueryNumber.From(values.Sum()).Denominator * values.Length),
            "min" => WorkflowQueryNumber.From(values.Min()),
            "max" => WorkflowQueryNumber.From(values.Max()),
            _ => throw InvalidSource()
        };
    }

    private static bool Having(DashboardPredicate? predicate, IReadOnlyDictionary<string, WorkflowQueryNumber?> values, DashboardQueryContext context)
    {
        if (predicate is null) return true;
        if (predicate.All is not null) return predicate.All.All(child => Having(child, values, context));
        if (predicate.Any is not null) return predicate.Any.Any(child => Having(child, values, context));
        var actual = values[MetricReference(predicate.Metric!)];
        if (predicate.Operator == "isNull") return actual is null;
        if (predicate.Operator == "isNotNull") return actual is not null;
        if (actual is null) return false;
        var comparison = actual.CompareTo(WorkflowQueryNumber.From(decimal.Parse(WorkflowQueryValues.Resolve(predicate.Value!, context).Literal!, CultureInfo.InvariantCulture)));
        return predicate.Operator switch { "eq" => comparison == 0, "ne" => comparison != 0, "gt" => comparison > 0, "gte" => comparison >= 0, "lt" => comparison < 0, "lte" => comparison <= 0, _ => false };
    }

    private static IReadOnlyList<SourceRow> SortRecords(IReadOnlyList<SourceRow> source, DashboardQueryDefinition query, CancellationToken cancellationToken)
    {
        var rows = source.ToList();
        rows.Sort((left, right) =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var sort in query.Sort)
            {
                var comparison = SortValues(left.Values[sort.Reference], right.Values[sort.Reference], sort);
                if (comparison != 0) return comparison;
            }
            return StringComparer.Ordinal.Compare(left.State.WorkflowExecutionId, right.State.WorkflowExecutionId);
        });
        return rows;
    }

    private static int CompareGroups(DashboardQueryDefinition query, ComputedGroup left, ComputedGroup right)
    {
        foreach (var sort in query.Sort)
        {
            int comparison;
            if (left.Values.TryGetValue(sort.Reference, out var leftValue)) comparison = SortValues(leftValue, right.Values[sort.Reference], sort);
            else
            {
                var a = left.Metrics[MetricReference(sort.Reference)]; var b = right.Metrics[MetricReference(sort.Reference)];
                if (a is null || b is null) comparison = a is null && b is null ? 0 : (a is null ? -1 : 1) * (sort.Nulls == "first" ? 1 : -1);
                else comparison = a.CompareTo(b) * (sort.Direction == "ascending" ? 1 : -1);
            }
            if (comparison != 0) return comparison;
        }
        return StringComparer.Ordinal.Compare(left.Key, right.Key);
    }

    private static int SortValues(DashboardValue left, DashboardValue right, DashboardSort sort)
    {
        var comparison = WorkflowQueryValues.Compare(left, right);
        return left.Type == "null" || right.Type == "null" ? comparison * (sort.Nulls == "first" ? 1 : -1)
            : comparison * (sort.Direction == "ascending" ? 1 : -1);
    }

    private DashboardQueryResult Records(DashboardQueryContext context, IReadOnlyList<SourceRow> rows, IReadOnlyList<string> fields) => BaseResult(context) with
    {
        Columns = fields.Select(field => Column(context, field)).ToArray(),
        Rows = rows.Select(row => new DashboardResultRow(row.State.WorkflowExecutionId,
            fields.ToDictionary(field => field, field => Cell(row.Values[field], context.Dataset.Fields.Single(item => item.Id == field).Type, "valueUnavailable"), StringComparer.Ordinal))).ToArray()
    };

    private static IReadOnlyDictionary<string, DashboardValue> GroupValues(DashboardQueryContext context, SourceRow row) =>
        context.Request.Query.GroupBy.ToDictionary(field => field, field => field == "startedAt" && context.Request.Query.Time?.Bucket is { } bucket
            ? Bucket(row.Values[field], bucket, context.Request.Query.Time.TimeZone) : row.Values[field], StringComparer.Ordinal);

    private static DashboardValue Bucket(DashboardValue value, string bucket, string timezone)
    {
        if (value.Type == "null") return value;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.Parse(value.Literal!, CultureInfo.InvariantCulture), zone);
        var start = bucket == "hour" ? new DateTimeOffset(local.Year, local.Month, local.Day, local.Hour, 0, 0, local.Offset)
            : new DateTimeOffset(local.Date, zone.GetUtcOffset(local.Date));
        return WorkflowQueryValues.Instant(start);
    }

    private static bool InPeriod(SourceRow row, IReadOnlyList<DashboardEffectivePeriod> periods, string role)
    {
        if (periods.Count == 0) return role == "current";
        var period = periods.Single(item => item.Role == role);
        return row.State.StartedAt >= period.From && row.State.StartedAt < period.To;
    }

    private static string GroupKey(IReadOnlyList<string> keys, IReadOnlyDictionary<string, DashboardValue> values) =>
        Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(keys.Select(key => new { key, values[key].Type, values[key].Literal }), Json))).ToLowerInvariant();

    private static DashboardResultCell Cell(DashboardValue value, string type, string nullReason) =>
        new(type, value.Literal, value.Type == "null" ? nullReason : null);

    private static DashboardResultCell MetricCell(WorkflowQueryNumber? value, DashboardQueryDefinition query, string reference)
    {
        if (value is not null) return new("decimal", value.Format());
        var alias = MetricReference(reference);
        if (alias.StartsWith("baseline.", StringComparison.Ordinal)) alias = alias[9..];
        if (alias.StartsWith("delta.", StringComparison.Ordinal)) alias = alias[6..];
        return new("decimal", null, query.Metrics.Single(metric => metric.Alias == alias).Metric == "workflow.failure-rate" ? "noEligibleDenominator" : "noEligibleValues");
    }

    private static string MetricReference(string reference) => reference.StartsWith("current.", StringComparison.Ordinal) ? reference[8..] : reference;
    private static string CurrentColumn(DashboardQueryContext context, string alias) => context.Request.Visualization.Y.Contains("current." + alias, StringComparer.Ordinal) || context.Request.Visualization.X == "current." + alias ? "current." + alias : alias;
    private static DashboardResultColumn Column(DashboardQueryContext context, string field)
    {
        var declaration = context.Dataset.Fields.Single(item => item.Id == field);
        return new(field, declaration.Label, declaration.Type, declaration.Unit);
    }

    private static DashboardQueryResult BaseResult(DashboardQueryContext context) => new()
    {
        DatasetId = DatasetId, SchemaRevision = SchemaRevision, QueryFingerprint = context.QueryFingerprint,
        Interpretation = Interpretation(context), Periods = context.Periods,
        Coverage = new("unknown", "Results cover retained, individually authorized runs only. Collection/retention completeness is not attested; deleted or uncaptured runs are absent.")
    };

    private static string Interpretation(DashboardQueryContext context) =>
        (context.Periods.Count == 0 ? "Retained runs after explicit filters and current run/organization authorization. " :
            "Current outcomes of retained runs started in each displayed [from,to) cohort, after current run and requested organization authorization and explicit filters. ") +
        "Failure rate is faulted/started including incomplete runs; no denominator is null. Change is percentage points, not relative growth. " +
        "Durations use valid terminal start/completion pairs; other pairs are excluded. Unknown run kind is not production attribution. " +
        (NeedsOrganization(context.Request.Query) ? "Folder grouping uses current readable Design organization, not historical folders. " : string.Empty) +
        "Collection/retention coverage is unknown.";

    private static bool NeedsOrganization(DashboardQueryDefinition query) => query.Fields.Concat(query.GroupBy)
        .Concat(query.Sort.Select(sort => sort.Reference)).Concat(FilterFields(query.Filters)).Any(field => field is "currentFolder" or "currentFolderId");

    private static IEnumerable<string> FilterFields(DashboardPredicate? predicate)
    {
        if (predicate is null) yield break;
        if (predicate.Field is { } field) yield return field;
        foreach (var child in predicate.All ?? predicate.Any ?? [])
            foreach (var name in FilterFields(child)) yield return name;
    }

    private static DashboardDrillDescriptor Descriptor(DashboardQueryContext context, IReadOnlyDictionary<string, DashboardValue> values)
    {
        var token = Sign(DrillMaterial(context, values));
        return new(DatasetId, SchemaRevision, context.QueryFingerprint, "current", values, context.Periods, context.EvaluatedAt, token);
    }

    // The proof admits either explicitly bound period; the cursor adds the chosen period separately.
    private static string DrillMaterial(DashboardQueryContext context, IReadOnlyDictionary<string, DashboardValue> values) =>
        context.QueryFingerprint + ":" + GroupKey(context.Request.Query.GroupBy, values);
    private static string Sign(string value) => Convert.ToBase64String(HMACSHA256.HashData(SigningKey, Encoding.UTF8.GetBytes(value)));
    private static bool FixedEquals(string? left, string right)
    {
        if (left is null || left.Length > 4096) return false;
        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    }

    private static string WriteCursor(int offset, string binding) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Cursor(offset, binding, Sign(binding + ":" + offset.ToString(CultureInfo.InvariantCulture))), Json));
    private static int ReadCursor(string? value, string binding)
    {
        if (value is null) return 0;
        try
        {
            var cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(value), Json);
            if (cursor is null || cursor.Offset < 0 || cursor.Binding != binding || !FixedEquals(cursor.Signature, Sign(binding + ":" + cursor.Offset.ToString(CultureInfo.InvariantCulture)))) throw InvalidDrill();
            return cursor.Offset;
        }
        catch (Exception exception) when (exception is JsonException or FormatException)
        { throw new DashboardQueryException("INVALID_DRILL", "This drill cursor is invalid. Refresh and try again.", exception); }
    }

    private static DashboardQueryException Limit() => new("QUERY_LIMIT", "The workflow question exceeds its bounded traversal or grouping cost. Narrow its filters or ask the module owner for a supported projection.");
    private static DashboardQueryException InvalidSource() => new("WORKFLOW_QUERY_SOURCE_INVALID", "Retained workflow data could not be read consistently. Retry or contact the runtime owner.");
    private static DashboardQueryException InvalidDrill() => new("INVALID_DRILL", "This group changed or its drill proof is unavailable. Refresh the question and select the group again.");
    private sealed record SourceRow(WorkflowExecutionState State, IReadOnlyDictionary<string, DashboardValue> Values);
    private sealed record Group(IReadOnlyDictionary<string, DashboardValue> Values)
    {
        public List<SourceRow> Current { get; } = [];
        public List<SourceRow> Baseline { get; } = [];
    }
    private sealed record ComputedGroup(string Key, IReadOnlyDictionary<string, DashboardValue> Values, IReadOnlyDictionary<string, WorkflowQueryNumber?> Metrics)
    {
        public IReadOnlyDictionary<string, DashboardValue> GroupValues => Values;
    }
    private sealed record Cursor(int Offset, string Binding, string Signature);
}
