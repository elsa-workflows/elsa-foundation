using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Acme.Processing.Core;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Events;
using Elsa.Studio.Dashboard.Core.Exceptions;
using Elsa.Studio.Dashboard.Core.Models;

namespace Acme.Processing;

internal sealed class ProcessingDashboardSource(IProcessingRecordReader records, ProcessingAccess access)
    : IDashboardDatasetExtension, IDashboardQueryValidationExtension, IDashboardQueryExecutionExtension, IDashboardQueryDrillExtension
{
    private const string Dataset = "acme.processing.records";
    private const string Revision = "processing-v1";
    private static readonly byte[] ProofKey = RandomNumberGenerator.GetBytes(32);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public IReadOnlyCollection<DashboardDatasetOwner> Ownership => [new(Dataset, "Acme.Processing")];

    public async ValueTask CollectAsync(DashboardDatasetsCollecting context, CancellationToken cancellationToken)
    {
        if (!await AdmittedAsync(context.Scope, null, cancellationToken)) return;
        var fields = new List<DashboardFieldDefinition>
        {
            new("recordId", "Processing record", "string", ["eq", "ne", "in", "startsWith"], Sortable: true),
            new("status", "Current processing outcome", "string", ["eq", "ne", "in", "notIn"], Groupable: true, Sortable: true),
            new("startedAt", "Processing started", "instant", ["gt", "gte", "lt", "lte"], Sortable: true),
            new("completedAt", "Processing completed", "instant", ["gt", "gte", "lt", "lte", "isNull", "isNotNull"], Nullable: true, Sortable: true),
            new("processingTimeMs", "Elapsed processing time", "decimal", ["gt", "gte", "lt", "lte", "eq", "isNull", "isNotNull"], Nullable: true, Sortable: true,
                Unit: "milliseconds", Description: "Elapsed time of a finished record with a valid start/completion pair; processing and uncaptured/inverted completion are ineligible.",
                Aggregates: ["count", "sum", "average", "min", "max"])
        };
        if (await access.PermissionAsync(ProcessingPermissions.CategoryRead, null, cancellationToken))
            fields.Add(new("customerCategory", "Customer category", "string", ["eq", "ne", "in", "notIn", "contains", "startsWith"], Groupable: true, Sortable: true,
                Description: "Explicit business field owned by Acme.Processing, captured when its ordinary create capability accepts the processing record. Not a workflow variable.", BusinessDimension: true));
        context.Datasets.Add(new()
        {
            Id = Dataset, OwnerModuleId = "Acme.Processing", SchemaRevision = Revision, Title = "Acme processing records",
            Description = "Reference business records deliberately exposing customer category and processing-time semantics under current record/field authority.",
            Fields = fields, Metrics = [
                new("processing.record-count", "v1", "Processing records", "records", "Current authorized records started in the displayed cohort, including records still processing.", true),
                new("processing.finished-count", "v1", "Finished processing records", "records", "Completed or failed records; records still processing are excluded.", true, ["status"]),
                new("processing.average-time-ms", "finished-valid-pairs-v1", "Average elapsed processing time", "milliseconds", "Mean elapsed milliseconds of completed/failed records with valid completion timestamps. Missing/inverted pairs are excluded; no eligible values is null.", true, ["processingTimeMs"]),
                new("processing.failure-rate", "finished-denominator-v1", "Finished processing failure rate", "percent", "100 × failed / finished records in the started cohort. In-flight records are excluded from this module's denominator; delta is percentage points, and no finished denominator is null.", true, ["status"])
            ],
            Time = new(["startedAt"], ["absolute", "calendarWeekToDate", "last7Days", "last30Days"], [], ["absolute", "previousCalendarWeekSameElapsed"]),
            SupportsDrill = true, SupportsExport = false,
            CoverageDescription = "Reference store coverage is unknown and the default store is ephemeral. No durable business-history completeness is attested."
        });
    }

    public async ValueTask ValidateAsync(DashboardQueryValidating context, CancellationToken cancellationToken)
    {
        var problems = new List<DashboardQueryProblem>();
        if (!await AdmittedAsync(context.Query.Scope, context.Query.Request.Query, cancellationToken))
            problems.Add(new("DATASET_UNAVAILABLE", "query.dataset", "Processing questions are unavailable in the current scope."));
        if (context.Query.Request.Query.Form == "aggregate" && context.Query.Periods.Count == 0)
            problems.Add(new("UNSUPPORTED_QUERY", "query.time", "Processing aggregates require an explicit started cohort."));
        context.Results.Add(new(problems.Count == 0, problems, Meaning));
    }

    public async ValueTask ExecuteAsync(DashboardQueryExecuting context, CancellationToken cancellationToken)
    {
        var rows = await ReadAsync(context.Query, context.Limits, cancellationToken);
        context.Results.Add(Compute(context.Query, rows, context.Limits, cancellationToken));
    }

    public async ValueTask DrillAsync(DashboardQueryDrilling context, CancellationToken cancellationToken)
    {
        var proof = Proof(context.Query, context.Request.Descriptor.GroupValues);
        if (!EqualProof(context.Request.Descriptor.Token, proof)) throw InvalidDrill();
        var rows = await ReadAsync(context.Query, context.Limits, cancellationToken);
        var aggregate = Compute(context.Query, rows, context.Limits, cancellationToken);
        var key = GroupKey(context.Query.Request.Query.GroupBy, context.Request.Descriptor.GroupValues);
        if (!aggregate.Rows.Any(row => row.Key == key)) throw InvalidDrill();
        var selected = rows.Where(row => InPeriod(row, context.Query, context.Request.Descriptor.Period) &&
            GroupKey(context.Query.Request.Query.GroupBy, Groups(row, context.Query)) == key).OrderBy(row => row.Record.Id, StringComparer.Ordinal).ToArray();
        var binding = proof + ":" + context.Request.Descriptor.Period;
        var offset = Offset(context.Request.Cursor, binding);
        if (offset > selected.Length) throw InvalidDrill();
        var page = selected.Skip(offset).Take(context.Request.PageSize).ToArray();
        var fields = new List<string> { "recordId", "status", "startedAt", "completedAt", "processingTimeMs" };
        if (UsesCategory(context.Query.Request.Query)) fields.Add("customerCategory");
        context.Results.Add(RecordResult(context.Query, page, fields) with
        { NextCursor = offset + page.Length < selected.Length ? Cursor(offset + page.Length, binding) : null });
    }

    private async ValueTask<bool> AdmittedAsync(DashboardRequestScope scope, DashboardQueryDefinition? query, CancellationToken cancellationToken)
    {
        var actor = await access.ActorAsync(cancellationToken);
        return actor.TenantId == scope.TenantId && actor.SubjectId == scope.SubjectId &&
            await access.PermissionAsync(ProcessingPermissions.Query, query, cancellationToken);
    }

    private async ValueTask<IReadOnlyList<Row>> ReadAsync(DashboardQueryContext context, DashboardQueryLimits limits, CancellationToken cancellationToken)
    {
        if (!await AdmittedAsync(context.Scope, context.Request.Query, cancellationToken))
            throw new DashboardQueryException("DATASET_UNAVAILABLE", "Processing questions are unavailable in the current scope.");
        var actor = await access.ActorAsync(cancellationToken);
        var category = UsesCategory(context.Request.Query);
        var rows = new List<Row>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var continuations = new HashSet<string>(StringComparer.Ordinal);
        var examined = 0;
        string? after = null;
        do
        {
            var page = await records.ReadPageAsync(actor.TenantId, 100, after, cancellationToken);
            if (page.Items.Count > 100 || page.Items.Count == 0 && page.NextId is not null) throw InvalidSource();
            foreach (var record in page.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (++examined > limits.MaximumSourceRows) throw Limit();
                if (!seen.Add(record.Id)) throw InvalidSource();
                if (!await access.CanReadAsync(record, actor, cancellationToken)) continue;
                if (category && !await access.PermissionAsync(ProcessingPermissions.CategoryRead, record, cancellationToken)) continue;
                if (context.Periods.Count != 0 && !context.Periods.Any(period => record.StartedAt >= period.From && record.StartedAt < period.To)) continue;
                decimal? duration = Finished(record) && record.CompletedAt is { } completed && completed >= record.StartedAt ? (completed - record.StartedAt).Ticks / 10000m : null;
                var values = new Dictionary<string, DashboardValue>(StringComparer.Ordinal)
                {
                    ["recordId"] = new("string", record.Id), ["customerCategory"] = new("string", record.CustomerCategory),
                    ["status"] = new("string", record.Status), ["startedAt"] = new("instant", record.StartedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)),
                    ["completedAt"] = record.CompletedAt is { } end ? new("instant", end.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)) : new("null"),
                    ["processingTimeMs"] = duration is { } elapsed ? new("decimal", elapsed.ToString(CultureInfo.InvariantCulture)) : new("null")
                };
                if (Filter(context.Request.Query.Filters, values, context)) rows.Add(new(record, values));
            }
            after = page.NextId;
            if (after is not null && !continuations.Add(after)) throw InvalidSource();
        } while (after is not null);
        return rows;
    }

    private static DashboardQueryResult Compute(DashboardQueryContext context, IReadOnlyList<Row> rows, DashboardQueryLimits limits, CancellationToken cancellationToken)
    {
        var query = context.Request.Query;
        if (query.Form == "records")
        {
            var sorted = rows.ToList();
            sorted.Sort((a, b) => { cancellationToken.ThrowIfCancellationRequested(); return Sort(query, a.Values, b.Values, null, null, a.Record.Id, b.Record.Id); });
            return RecordResult(context, sorted.Take(query.Limit).ToArray(), query.Fields);
        }
        var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
        if (query.GroupBy.Count == 0) groups.Add(GroupKey(query.GroupBy, new Dictionary<string, DashboardValue>()), new(new Dictionary<string, DashboardValue>()));
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = Groups(row, context); var key = GroupKey(query.GroupBy, values);
            if (!groups.TryGetValue(key, out var group))
            {
                if (groups.Count >= limits.MaximumGroups) throw Limit();
                group = new(values); groups.Add(key, group);
            }
            if (InPeriod(row, context, "current")) group.Current.Add(row);
            if (query.Comparison is not null && InPeriod(row, context, "baseline")) group.Baseline.Add(row);
        }
        var computed = new List<Computed>();
        foreach (var pair in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metrics = new Dictionary<string, Fraction?>(StringComparer.Ordinal);
            foreach (var selection in query.Metrics)
            {
                var current = Metric(selection, pair.Value.Current); var baseline = Metric(selection, pair.Value.Baseline);
                metrics.Add(selection.Alias, current); metrics.Add("baseline." + selection.Alias, baseline);
                metrics.Add("delta." + selection.Alias, current is not null && baseline is not null ? current.Subtract(baseline) : null);
            }
            if (Having(query.Having, metrics, context)) computed.Add(new(pair.Key, pair.Value.Values, metrics));
        }
        computed.Sort((a, b) => { cancellationToken.ThrowIfCancellationRequested(); return Sort(query, a.Values, b.Values, a.Metrics, b.Metrics, a.Key, b.Key); });
        var columns = query.GroupBy.Select(field => Column(context, field)).ToList();
        foreach (var selection in query.Metrics)
        {
            var definition = selection.Metric is { } name ? context.Dataset.Metrics.Single(metric => metric.Id == name) : null;
            var unit = selection.Aggregate == "count" ? "records" : definition?.Unit ?? "milliseconds";
            columns.Add(new(CurrentName(context, selection.Alias), definition?.Label ?? selection.Aggregate + " processing time", "decimal", unit));
            if (query.Comparison is not null)
            {
                columns.Add(new("baseline." + selection.Alias, "Baseline " + selection.Alias, "decimal", unit));
                columns.Add(new("delta." + selection.Alias, "Change in " + selection.Alias, "decimal", unit == "percent" ? "percentage points" : unit));
            }
        }
        return Result(context) with
        {
            Columns = columns,
            Rows = computed.Take(query.Limit).Select(group => new DashboardResultRow(group.Key,
                columns.ToDictionary(column => column.Id, column => group.Values.TryGetValue(column.Id, out var scalar) ? Cell(scalar, column.Type) :
                    MetricCell(group.Metrics[Reference(column.Id)], query, column.Id), StringComparer.Ordinal),
                new(Dataset, Revision, context.QueryFingerprint, "current", group.Values, context.Periods, context.EvaluatedAt, Proof(context, group.Values)))).ToArray()
        };
    }

    private static Fraction? Metric(DashboardMetricSelection selection, IReadOnlyCollection<Row> rows)
    {
        if (selection.Metric == "processing.record-count") return Fraction.From(rows.Count);
        var finished = rows.Count(row => Finished(row.Record));
        if (selection.Metric == "processing.finished-count") return Fraction.From(finished);
        if (selection.Metric == "processing.failure-rate") return finished == 0 ? null : new(rows.Count(row => row.Record.Status == "failed") * 100, finished);
        var values = rows.Select(row => row.Values["processingTimeMs"]).Where(value => value.Type != "null").Select(value => decimal.Parse(value.Literal!, CultureInfo.InvariantCulture)).ToArray();
        var operation = selection.Metric == "processing.average-time-ms" ? "average" : selection.Aggregate;
        if (operation == "count") return Fraction.From(values.Length);
        if (values.Length == 0) return null;
        var sum = Fraction.From(values.Sum());
        return operation switch { "sum" => sum, "average" => new(sum.Numerator, sum.Denominator * values.Length), "min" => Fraction.From(values.Min()), "max" => Fraction.From(values.Max()), _ => throw InvalidSource() };
    }

    private static bool Filter(DashboardPredicate? predicate, IReadOnlyDictionary<string, DashboardValue> values, DashboardQueryContext context)
    {
        if (predicate is null) return true;
        if (predicate.All is not null) return predicate.All.All(child => Filter(child, values, context));
        if (predicate.Any is not null) return predicate.Any.Any(child => Filter(child, values, context));
        var actual = values[predicate.Field!];
        if (predicate.Operator == "isNull") return actual.Type == "null";
        if (predicate.Operator == "isNotNull") return actual.Type != "null";
        DashboardValue Resolve(DashboardValue value) => value.Parameter is { } name ? context.Parameters[name] : value;
        if (predicate.Operator is "in" or "notIn")
        {
            var exists = predicate.Values!.Any(value => Compare(actual, Resolve(value)) == 0);
            return predicate.Operator == "in" ? exists : !exists;
        }
        var expected = Resolve(predicate.Value!);
        if (predicate.Operator is "contains" or "startsWith") return actual.Literal is { } text && expected.Literal is { } term &&
            (predicate.Operator == "contains" ? text.Contains(term, StringComparison.Ordinal) : text.StartsWith(term, StringComparison.Ordinal));
        if (predicate.Operator == "eq") return Compare(actual, expected) == 0;
        if (predicate.Operator == "ne") return Compare(actual, expected) != 0;
        return Predicate(predicate.Operator!, actual.Type == "null" || expected.Type == "null" ? null : Compare(actual, expected));
    }

    private static bool Having(DashboardPredicate? predicate, IReadOnlyDictionary<string, Fraction?> values, DashboardQueryContext context)
    {
        if (predicate is null) return true;
        if (predicate.All is not null) return predicate.All.All(child => Having(child, values, context));
        if (predicate.Any is not null) return predicate.Any.Any(child => Having(child, values, context));
        var actual = values[Reference(predicate.Metric!)];
        if (predicate.Operator == "isNull") return actual is null;
        if (predicate.Operator == "isNotNull") return actual is not null;
        var value = predicate.Value!.Parameter is { } name ? context.Parameters[name] : predicate.Value;
        return Predicate(predicate.Operator!, actual?.CompareTo(Fraction.From(decimal.Parse(value.Literal!, CultureInfo.InvariantCulture))));
    }

    private static bool Predicate(string operation, int? comparison) => comparison is { } value && operation switch
    { "eq" => value == 0, "ne" => value != 0, "gt" => value > 0, "gte" => value >= 0, "lt" => value < 0, "lte" => value <= 0, _ => false };

    private static int Compare(DashboardValue left, DashboardValue right)
    {
        if (left.Type == "null" || right.Type == "null") return left.Type == right.Type ? 0 : left.Type == "null" ? -1 : 1;
        if (left.Type is "decimal" or "integer") return decimal.Parse(left.Literal!, CultureInfo.InvariantCulture).CompareTo(decimal.Parse(right.Literal!, CultureInfo.InvariantCulture));
        if (left.Type == "instant") return DateTimeOffset.Parse(left.Literal!, CultureInfo.InvariantCulture).CompareTo(DateTimeOffset.Parse(right.Literal!, CultureInfo.InvariantCulture));
        return StringComparer.Ordinal.Compare(left.Literal, right.Literal);
    }

    private static int Sort(DashboardQueryDefinition query, IReadOnlyDictionary<string, DashboardValue> a, IReadOnlyDictionary<string, DashboardValue> b,
        IReadOnlyDictionary<string, Fraction?>? am, IReadOnlyDictionary<string, Fraction?>? bm, string aKey, string bKey)
    {
        foreach (var sort in query.Sort)
        {
            int comparison;
            bool nullA, nullB;
            if (a.TryGetValue(sort.Reference, out var av))
            {
                var bv = b[sort.Reference]; nullA = av.Type == "null"; nullB = bv.Type == "null"; comparison = Compare(av, bv);
            }
            else
            {
                var avm = am![Reference(sort.Reference)]; var bvm = bm![Reference(sort.Reference)];
                nullA = avm is null; nullB = bvm is null; comparison = avm?.CompareTo(bvm) ?? (bvm is null ? 0 : -1);
            }
            if (nullA || nullB) comparison = nullA == nullB ? 0 : (nullA ? -1 : 1) * (sort.Nulls == "first" ? 1 : -1);
            else comparison *= sort.Direction == "ascending" ? 1 : -1;
            if (comparison != 0) return comparison;
        }
        return StringComparer.Ordinal.Compare(aKey, bKey);
    }

    private static DashboardQueryResult RecordResult(DashboardQueryContext context, IReadOnlyList<Row> rows, IReadOnlyList<string> fields) => Result(context) with
    {
        Columns = fields.Select(field => Column(context, field)).ToArray(),
        Rows = rows.Select(row => new DashboardResultRow(row.Record.Id, fields.ToDictionary(field => field, field => Cell(row.Values[field], context.Dataset.Fields.Single(item => item.Id == field).Type), StringComparer.Ordinal))).ToArray()
    };
    private static DashboardResultCell Cell(DashboardValue value, string type) => new(type, value.Literal, value.Type == "null" ? "notCapturedOrIneligible" : null);
    private static DashboardResultCell MetricCell(Fraction? number, DashboardQueryDefinition query, string reference)
    {
        var alias = Reference(reference); if (alias.StartsWith("baseline.", StringComparison.Ordinal)) alias = alias[9..]; if (alias.StartsWith("delta.", StringComparison.Ordinal)) alias = alias[6..];
        return new("decimal", number?.Format(), number is not null ? null : query.Metrics.Single(metric => metric.Alias == alias).Metric == "processing.failure-rate" ? "noEligibleDenominator" : "noEligibleValues");
    }
    private static DashboardResultColumn Column(DashboardQueryContext context, string field)
    { var declaration = context.Dataset.Fields.Single(item => item.Id == field); return new(field, declaration.Label, declaration.Type, declaration.Unit); }
    private static IReadOnlyDictionary<string, DashboardValue> Groups(Row row, DashboardQueryContext context) => context.Request.Query.GroupBy.ToDictionary(field => field, field => row.Values[field], StringComparer.Ordinal);
    private static string GroupKey(IReadOnlyList<string> keys, IReadOnlyDictionary<string, DashboardValue> values) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(keys.Select(key => new { key, values[key].Type, values[key].Literal }), Json))).ToLowerInvariant();
    private static bool InPeriod(Row row, DashboardQueryContext context, string role) => context.Periods.Count == 0 ? role == "current" : context.Periods.Any(period => period.Role == role && row.Record.StartedAt >= period.From && row.Record.StartedAt < period.To);
    private static bool Finished(ProcessingRecord record) => record.Status is "completed" or "failed";
    private static string Reference(string reference) => reference.StartsWith("current.", StringComparison.Ordinal) ? reference[8..] : reference;
    private static string CurrentName(DashboardQueryContext context, string alias) => context.Request.Visualization.Y.Contains("current." + alias, StringComparer.Ordinal) || context.Request.Visualization.X == "current." + alias ? "current." + alias : alias;
    private static DashboardQueryResult Result(DashboardQueryContext context) => new()
    {
        DatasetId = Dataset, SchemaRevision = Revision, QueryFingerprint = context.QueryFingerprint, Periods = context.Periods, Interpretation = Meaning,
        Coverage = new("unknown", "The reference store is ephemeral and collection/retention completeness is unknown. Results describe currently authorized retained records only.")
    };
    private const string Meaning = "Acme-owned records started in the displayed cohort, after current record and requested category authority. Processing time uses valid finished start/completion pairs. Failure rate uses finished records only, excluding in-flight work; no finished denominator is null and change is percentage points. Category is an explicitly captured business field. Reference store coverage is unknown.";
    private static bool UsesCategory(DashboardQueryDefinition query) => query.Fields.Concat(query.GroupBy).Concat(query.Sort.Select(sort => sort.Reference)).Concat(FilterFields(query.Filters)).Contains("customerCategory", StringComparer.Ordinal);
    private static IEnumerable<string> FilterFields(DashboardPredicate? predicate)
    { if (predicate?.Field is { } field) yield return field; foreach (var child in predicate?.All ?? predicate?.Any ?? []) foreach (var result in FilterFields(child)) yield return result; }
    private static string Proof(DashboardQueryContext context, IReadOnlyDictionary<string, DashboardValue> values) => Sign(context.QueryFingerprint + ":" + GroupKey(context.Request.Query.GroupBy, values));
    private static string Sign(string value) => Convert.ToBase64String(HMACSHA256.HashData(ProofKey, Encoding.UTF8.GetBytes(value)));
    private static bool EqualProof(string? left, string right) => left is not null && left.Length <= 4096 && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));
    private static string Cursor(int offset, string binding) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new CursorValue(offset, binding, Sign(binding + ":" + offset.ToString(CultureInfo.InvariantCulture))), Json));
    private static int Offset(string? cursor, string binding)
    {
        if (cursor is null) return 0;
        try
        {
            var value = JsonSerializer.Deserialize<CursorValue>(Convert.FromBase64String(cursor), Json);
            if (value is null || value.Offset < 0 || value.Binding != binding || !EqualProof(value.Signature, Sign(binding + ":" + value.Offset.ToString(CultureInfo.InvariantCulture)))) throw InvalidDrill();
            return value.Offset;
        }
        catch (Exception exception) when (exception is FormatException or JsonException) { throw new DashboardQueryException("INVALID_DRILL", "The processing cursor is invalid. Refresh the question.", exception); }
    }
    private static DashboardQueryException InvalidDrill() => new("INVALID_DRILL", "This processing group changed or its proof is unavailable. Refresh the question.");
    private static DashboardQueryException InvalidSource() => new("PROCESSING_SOURCE_INVALID", "The processing source could not provide a valid finite result.");
    private static DashboardQueryException Limit() => new("QUERY_LIMIT", "The processing question exceeds its source/group bound. Narrow the question.");
    private sealed record Row(ProcessingRecord Record, IReadOnlyDictionary<string, DashboardValue> Values);
    private sealed record Group(IReadOnlyDictionary<string, DashboardValue> Values) { public List<Row> Current { get; } = []; public List<Row> Baseline { get; } = []; }
    private sealed record Computed(string Key, IReadOnlyDictionary<string, DashboardValue> Values, IReadOnlyDictionary<string, Fraction?> Metrics);
    private sealed record CursorValue(int Offset, string Binding, string Signature);
    private sealed record Fraction(BigInteger Numerator, BigInteger Denominator) : IComparable<Fraction>
    {
        public static Fraction From(decimal value)
        {
            var bits = decimal.GetBits(value); var numerator = (BigInteger)(uint)bits[0] + ((BigInteger)(uint)bits[1] << 32) + ((BigInteger)(uint)bits[2] << 64);
            return new((bits[3] & int.MinValue) == 0 ? numerator : -numerator, BigInteger.Pow(10, (bits[3] >> 16) & 255));
        }
        public Fraction Subtract(Fraction other) => new(Numerator * other.Denominator - other.Numerator * Denominator, Denominator * other.Denominator);
        public int CompareTo(Fraction? other) => other is null ? 1 : (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
        public string Format()
        {
            const long precision = 1000000000000;
            var scaled = BigInteger.DivRem(Numerator * precision, Denominator, out var remainder);
            if (BigInteger.Abs(remainder) * 2 >= Denominator) scaled += Numerator.Sign;
            var whole = BigInteger.DivRem(scaled, precision, out var fraction);
            return ((decimal)whole + (decimal)fraction / precision).ToString("0.############", CultureInfo.InvariantCulture);
        }
    }
}
