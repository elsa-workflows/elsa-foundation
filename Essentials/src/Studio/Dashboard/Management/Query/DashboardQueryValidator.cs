using System.Globalization;
using System.Text;
using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Management.Query;

internal static class DashboardQueryValidator
{
    private static readonly HashSet<string> ScalarTypes = ["string", "boolean", "integer", "decimal", "instant", "date"];
    private static readonly HashSet<string> Operators = ["eq", "ne", "gt", "gte", "lt", "lte", "in", "notIn", "contains", "startsWith", "isNull", "isNotNull"];
    private static readonly HashSet<string> HavingOperators = ["eq", "ne", "gt", "gte", "lt", "lte", "isNull", "isNotNull"];

    public static IReadOnlyList<DashboardQueryProblem> Shape(DashboardQueryRequest request, DashboardQueryLimits limits)
    {
        var problems = new List<DashboardQueryProblem>();
        void Add(string path, string message) => problems.Add(new("INVALID_QUERY", path, message));
        if (request?.Query is not { } query || request.Visualization is null || request.ParameterValues is null)
        {
            Add("", "Query, visualization and parameter values are required.");
            return problems;
        }
        if (query.Dataset is null || !Identifier(query.Dataset.Id) || !Identifier(query.Dataset.SchemaRevision))
            Add("query.dataset", "A bounded dataset identity and schema revision are required.");
        if (query.SchemaVersion != 1) Add("query.schemaVersion", "This query schema version is unsupported.");
        if (query.Form is not ("aggregate" or "records")) Add("query.form", "Choose aggregate or records.");
        if (query.Parameters is null || query.Parameters.Count > 12 || query.Fields is null || query.Fields.Count > 12 ||
            query.GroupBy is null || query.GroupBy.Count > 2 || query.Metrics is null || query.Metrics.Count > 4 ||
            query.Sort is null || query.Sort.Count > 3 || request.ParameterValues.Count > 12 ||
            request.Visualization.Y is null || request.Visualization.Y.Count > 4)
        {
            Add("query", "The query exceeds its parameter, field, group, metric or sort limits.");
            return problems;
        }
        if (query.Limit is < 1 || query.Limit > limits.MaximumResultRows || query.Form == "records" && query.Limit > 100)
            Add("query.limit", "The requested output limit is outside the supported bounds.");
        if (query.Fields.Any(field => !Identifier(field)) || query.Fields.Distinct(StringComparer.Ordinal).Count() != query.Fields.Count)
            Add("query.fields", "Projected fields must have distinct bounded identities.");
        if (query.GroupBy.Any(field => !Identifier(field)) || query.GroupBy.Distinct(StringComparer.Ordinal).Count() != query.GroupBy.Count)
            Add("query.groupBy", "Grouping fields must have distinct bounded identities.");
        if (query.Form == "aggregate" && (query.Metrics.Count == 0 || query.Fields.Count != 0))
            Add("query.metrics", "An aggregate query requires metrics and uses group keys instead of record projections.");
        if (query.Form == "records" && (query.Fields.Count == 0 || query.GroupBy.Count != 0 || query.Metrics.Count != 0 || query.Having is not null || query.Comparison is not null))
            Add("query", "A record query requires explicit fields and cannot contain aggregation or comparison clauses.");

        var parameters = new Dictionary<string, DashboardParameterDefinition>(StringComparer.Ordinal);
        foreach (var parameter in query.Parameters)
        {
            if (parameter is null || !Identifier(parameter.Name) || !ScalarTypes.Contains(parameter.Type) || !parameters.TryAdd(parameter.Name, parameter))
            {
                Add("query.parameters", "Parameter names must be distinct and scalar types supported.");
                continue;
            }
            if (parameter.Default is not null)
                Value(parameter.Default, parameter.Type, parameter.Nullable, parameters: null, "query.parameters.default", problems);
        }
        foreach (var parameter in request.ParameterValues)
        {
            if (!parameters.TryGetValue(parameter.Key, out var definition)) Add("parameterValues", "An undeclared parameter was supplied.");
            else Value(parameter.Value, definition.Type, definition.Nullable, null, "parameterValues", problems);
        }
        foreach (var parameter in parameters.Values)
            if (parameter.Required && !request.ParameterValues.ContainsKey(parameter.Name) && parameter.Default is null)
                Add("parameterValues", "A required parameter has no supplied value or explicit default.");

        var aliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var metric in query.Metrics)
        {
            if (metric is null || !Identifier(metric.Alias) || metric.Alias.Contains('.') || !aliases.Add(metric.Alias))
            {
                Add("query.metrics", "Metric aliases must be distinct bounded identities without a period.");
                continue;
            }
            var named = metric.Metric is not null;
            if (named && (!Identifier(metric.Metric) || !Identifier(metric.Revision) || metric.Aggregate is not null || metric.Field is not null) ||
                !named && (metric.Revision is not null || metric.Aggregate is not ("count" or "sum" or "average" or "min" or "max") || !Identifier(metric.Field)))
                Add("query.metrics", "Choose an exact module metric revision or one supported field aggregate.");
        }
        var sourceLeaves = 0;
        PredicateShape(query.Filters, false, 1, ref sourceLeaves, parameters, "query.filters", problems);
        var havingLeaves = 0;
        PredicateShape(query.Having, true, 1, ref havingLeaves, parameters, "query.having", problems);
        foreach (var name in ReferencedParameters(query.Filters).Concat(ReferencedParameters(query.Having)).Distinct(StringComparer.Ordinal))
            if (parameters.TryGetValue(name, out var parameter) && !request.ParameterValues.ContainsKey(name) && parameter.Default is null)
                Add("parameterValues", "A referenced parameter needs an explicit value or default.");
        if (query.GroupBy.Any(aliases.Contains)) Add("query.metrics", "Metric aliases cannot duplicate group-field identities.");
        foreach (var sort in query.Sort)
            if (sort is null || !Identifier(sort.Reference) || sort.Direction is not ("ascending" or "descending") || sort.Nulls is not ("first" or "last"))
                Add("query.sort", "Sorting requires a bounded reference, direction and null policy.");
        if (query.Time is { } time && (!Identifier(time.Field) || !Identifier(time.TimeZone) || time.WeekStart != "monday" ||
            time.Bucket is not (null or "hour" or "day") || time.Window is not (null or "absolute" or "calendarWeekToDate" or "last7Days" or "last30Days")))
            Add("query.time", "The requested time field, range, timezone or bucket is unsupported.");
        if (query.Comparison is { } comparison && (query.Time is null || comparison.Mode is not ("absolute" or "previousCalendarWeekSameElapsed")))
            Add("query.comparison", "A comparison requires a supported time window and baseline mode.");

        var visualization = request.Visualization;
        if (visualization.Kind is not ("bar" or "line" or "table" or "list" or "metric") ||
            visualization.X is not null && !Identifier(visualization.X) ||
            visualization.Y.Any(reference => !Identifier(reference)) || visualization.Label?.EnumerateRunes().Count() > 200)
            Add("visualization", "Choose a supported visualization with bounded column references and label.");
        if (visualization.Kind is "bar" or "line" && (visualization.X is null || visualization.Y.Count == 0))
            Add("visualization", "A chart requires an X column and one or more numeric Y columns.");
        if (visualization.Kind == "metric" && (visualization.Y.Count != 1 || query.Form != "aggregate" || query.GroupBy.Count != 0))
            Add("visualization", "A single-value metric requires one metric column without grouping.");
        return problems;
    }

    public static IReadOnlyList<DashboardQueryProblem> Declaration(DashboardQueryRequest request, DashboardDatasetDeclaration dataset)
    {
        var problems = new List<DashboardQueryProblem>();
        void Add(string path, string message) => problems.Add(new("UNSUPPORTED_QUERY", path, message));
        var query = request.Query;
        var fields = dataset.Fields.ToDictionary(field => field.Id, StringComparer.Ordinal);
        var metrics = dataset.Metrics.ToDictionary(metric => metric.Id, StringComparer.Ordinal);
        if (dataset.Availability != "ready") Add("query.dataset", "This dataset is currently unavailable.");
        if (dataset.SchemaRevision != query.Dataset.SchemaRevision) Add("query.dataset.schemaRevision", "The saved dataset schema requires repair before use.");
        if (!dataset.Forms.Contains(query.Form, StringComparer.Ordinal)) Add("query.form", "This dataset does not support this query form.");
        foreach (var field in query.Fields)
            if (!fields.TryGetValue(field, out var declaration) || !declaration.Projectable)
                Add("query.fields", "A projected field is unavailable in the current scope.");
        foreach (var field in query.GroupBy)
            if (!fields.TryGetValue(field, out var declaration) || !declaration.Groupable)
                Add("query.groupBy", "A grouping field is unavailable in the current scope.");
        var comparable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var selection in query.Metrics)
        {
            if (selection.Metric is { } metric)
            {
                if (!metrics.TryGetValue(metric, out var declaration) || declaration.Revision != selection.Revision)
                    Add("query.metrics", "A module metric or its exact meaning is unavailable.");
                else
                {
                    if (declaration.RequiredFields?.Any(field => !fields.ContainsKey(field)) == true)
                        Add("query.metrics", "A required metric field is unavailable in the current scope.");
                    if (declaration.Comparable) comparable.Add(selection.Alias);
                }
            }
            else if (!fields.TryGetValue(selection.Field!, out var field) || field.Aggregates?.Contains(selection.Aggregate!, StringComparer.Ordinal) != true ||
                     selection.Aggregate is "sum" or "average" && field.Type is not ("integer" or "decimal"))
                Add("query.metrics", "The field does not expose this aggregate in the current scope.");
            else comparable.Add(selection.Alias);
        }
        if (query.Comparison is not null && query.Metrics.Any(metric => !comparable.Contains(metric.Alias)))
            Add("query.comparison", "One or more metrics do not support period comparisons.");
        PredicateDeclaration(query.Filters, false, request, fields, comparable, problems);
        PredicateDeclaration(query.Having, true, request, fields, comparable, problems);

        var outputs = new HashSet<string>(query.Form == "records" ? query.Fields : query.GroupBy, StringComparer.Ordinal);
        foreach (var metric in query.Metrics)
        {
            outputs.Add(metric.Alias);
            outputs.Add("current." + metric.Alias);
            if (query.Comparison is not null)
            {
                outputs.Add("baseline." + metric.Alias);
                outputs.Add("delta." + metric.Alias);
            }
        }
        foreach (var sort in query.Sort)
            if (!outputs.Contains(sort.Reference) || fields.TryGetValue(sort.Reference, out var field) && !field.Sortable)
                Add("query.sort", "A sort reference is unavailable or not part of the output.");
        if (request.Visualization.X is { } x && !outputs.Contains(x) || request.Visualization.Y.Any(y => !outputs.Contains(y)))
            Add("visualization", "A visualization column is not part of the validated result.");
        foreach (var y in request.Visualization.Y)
            if (request.Visualization.Kind is "bar" or "line" or "metric" && fields.TryGetValue(y, out var field) && field.Type is not ("integer" or "decimal"))
                Add("visualization.y", "Numeric visualizations require numeric columns.");
        if (query.Time is { } time && (dataset.Time is not { } support || !support.Fields.Contains(time.Field, StringComparer.Ordinal) ||
            !support.Windows.Contains(time.Window ?? "absolute", StringComparer.Ordinal) ||
            time.Bucket is not null && !support.Buckets.Contains(time.Bucket, StringComparer.Ordinal) ||
            query.Comparison is { } comparison && !support.ComparisonModes.Contains(comparison.Mode, StringComparer.Ordinal)))
            Add("query.time", "The dataset does not support the requested time interpretation.");
        return problems;
    }

    public static IReadOnlyDictionary<string, DashboardValue> Parameters(DashboardQueryRequest request)
    {
        var values = new SortedDictionary<string, DashboardValue>(StringComparer.Ordinal);
        foreach (var parameter in request.Query.Parameters)
        {
            if (request.ParameterValues.TryGetValue(parameter.Name, out var value)) values.Add(parameter.Name, value);
            else if (parameter.Default is { } fallback) values.Add(parameter.Name, fallback);
        }
        return values;
    }

    private static void PredicateShape(DashboardPredicate? predicate, bool having, int depth, ref int leaves,
        IReadOnlyDictionary<string, DashboardParameterDefinition> parameters, string path, List<DashboardQueryProblem> problems)
    {
        if (predicate is null) return;
        var maximum = having ? 10 : 20;
        if (depth > 3 || predicate.All?.Count > maximum || predicate.Any?.Count > maximum)
        {
            problems.Add(new("QUERY_LIMIT", path, "The predicate exceeds its depth or leaf bound."));
            return;
        }
        if (predicate.All is not null || predicate.Any is not null)
        {
            var children = predicate.All ?? predicate.Any!;
            if (predicate.All is not null && predicate.Any is not null || children.Count == 0 || predicate.Field is not null ||
                predicate.Metric is not null || predicate.Operator is not null || predicate.Value is not null || predicate.Values is not null)
            {
                problems.Add(new("INVALID_QUERY", path, "A branch must contain exactly one nonempty all/any collection."));
                return;
            }
            foreach (var child in children)
            {
                if (child is null) problems.Add(new("INVALID_QUERY", path, "A predicate child cannot be null."));
                else PredicateShape(child, having, depth + 1, ref leaves, parameters, path, problems);
            }
            return;
        }
        if (++leaves > maximum)
        {
            problems.Add(new("QUERY_LIMIT", path, "The predicate exceeds its leaf bound."));
            return;
        }
        if (!(having ? HavingOperators : Operators).Contains(predicate.Operator ?? string.Empty) ||
            !Identifier(having ? predicate.Metric : predicate.Field) || (having ? predicate.Field : predicate.Metric) is not null)
        {
            problems.Add(new("INVALID_QUERY", path, "A predicate requires one supported field or metric operator."));
            return;
        }
        if (predicate.Operator is "isNull" or "isNotNull")
        {
            if (predicate.Value is not null || predicate.Values is not null)
                problems.Add(new("INVALID_QUERY", path, "A null test cannot contain a value."));
        }
        else if (predicate.Operator is "in" or "notIn")
        {
            if (predicate.Value is not null || predicate.Values is null || predicate.Values.Count is < 1 or > 100)
                problems.Add(new("INVALID_QUERY", path, "A membership predicate requires between 1 and 100 values."));
            else foreach (var value in predicate.Values) Value(value, null, true, parameters, path, problems);
        }
        else
        {
            if (predicate.Values is not null) problems.Add(new("INVALID_QUERY", path, "This predicate requires one scalar value."));
            Value(predicate.Value, having ? "decimal" : null, !having, parameters, path, problems);
        }
    }

    private static void PredicateDeclaration(DashboardPredicate? predicate, bool having, DashboardQueryRequest request,
        IReadOnlyDictionary<string, DashboardFieldDefinition> fields, HashSet<string> comparable, List<DashboardQueryProblem> problems)
    {
        if (predicate is null) return;
        if (predicate.All is not null || predicate.Any is not null)
        {
            foreach (var child in predicate.All ?? predicate.Any!) PredicateDeclaration(child, having, request, fields, comparable, problems);
            return;
        }
        var parameters = request.Query.Parameters.ToDictionary(parameter => parameter.Name, StringComparer.Ordinal);
        DashboardValue? Resolve(DashboardValue? value) => value?.Parameter is { } name
            ? request.ParameterValues.GetValueOrDefault(name) ?? parameters.GetValueOrDefault(name)?.Default
            : value;
        if (having)
        {
            var reference = predicate.Metric!;
            var dot = reference.IndexOf('.');
            var role = dot < 0 ? "current" : reference[..dot];
            var alias = dot < 0 ? reference : reference[(dot + 1)..];
            if (role is not ("current" or "baseline" or "delta") || !request.Query.Metrics.Any(metric => metric.Alias == alias) ||
                role != "current" && (request.Query.Comparison is null || !comparable.Contains(alias)))
                problems.Add(new("UNSUPPORTED_QUERY", "query.having", "A comparison/metric reference is unavailable."));
            if (predicate.Operator is not ("isNull" or "isNotNull"))
                Value(Resolve(predicate.Value), "decimal", false, null, "query.having", problems);
            return;
        }
        if (!fields.TryGetValue(predicate.Field!, out var field) || !field.FilterOperators.Contains(predicate.Operator!, StringComparer.Ordinal))
        {
            problems.Add(new("UNSUPPORTED_QUERY", "query.filters", "A filter field/operator is unavailable in the current scope."));
            return;
        }
        if (predicate.Value is not null) Value(Resolve(predicate.Value), field.Type, field.Nullable, null, "query.filters", problems);
        if (predicate.Values is not null)
            foreach (var value in predicate.Values) Value(Resolve(value), field.Type, field.Nullable, null, "query.filters", problems);
    }

    private static void Value(DashboardValue? value, string? expected, bool nullable,
        IReadOnlyDictionary<string, DashboardParameterDefinition>? parameters, string path, List<DashboardQueryProblem> problems)
    {
        bool valid;
        if (value is null) valid = false;
        else if (value.Parameter is not null)
            valid = value.Literal is null && parameters is not null && parameters.TryGetValue(value.Parameter, out var definition) &&
                    definition.Type == value.Type && (expected is null || expected == value.Type || expected == "decimal" && value.Type == "integer");
        else if (value.Type == "null") valid = nullable && value.Literal is null;
        else
        {
            valid = ScalarTypes.Contains(value.Type) && value.Literal is not null && value.Literal.Length <= 512 &&
                    (expected is null || expected == value.Type || expected == "decimal" && value.Type == "integer") && value.Type switch
                    {
                        "string" => value.Literal.EnumerateRunes().Count() <= 256,
                        "boolean" => value.Literal is "true" or "false",
                        "integer" => long.TryParse(value.Literal, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
                        "decimal" => decimal.TryParse(value.Literal, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out _),
                        "instant" => DateTimeOffset.TryParse(value.Literal, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _) &&
                                     (value.Literal.EndsWith('Z') || value.Literal.Length >= 6 && value.Literal[^3] == ':' && value.Literal[^6] is '+' or '-'),
                        "date" => DateOnly.TryParseExact(value.Literal, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
                        _ => false
                    };
        }
        if (!valid) problems.Add(new("INVALID_VALUE", path, "A value does not match its declared scalar type or parameter."));
    }

    internal static bool Identifier(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 128 &&
                                                    !value.Any(character => char.IsControl(character) || char.IsWhiteSpace(character));

    private static IEnumerable<string> ReferencedParameters(DashboardPredicate? predicate, int depth = 1)
    {
        if (predicate is null || depth > 3) yield break;
        if (predicate.Value?.Parameter is { } name) yield return name;
        foreach (var value in predicate.Values?.Take(100) ?? [])
            if (value?.Parameter is { } item) yield return item;
        foreach (var child in (predicate.All ?? predicate.Any ?? []).Take(20))
            foreach (var reference in ReferencedParameters(child, depth + 1)) yield return reference;
    }
}
