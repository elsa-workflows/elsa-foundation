namespace Elsa.Studio.Dashboard.Core.Models;

/// <summary>Trusted server-bound caller scope, never a client-supplied authorization grant.</summary>
public sealed record DashboardRequestScope(string TenantId, string SubjectId, string HostId);

public sealed record DashboardDatasetReference(string Id, string SchemaRevision);

/// <summary>A typed invariant scalar literal, explicit null, or parameter reference.</summary>
public sealed record DashboardValue(string Type, string? Literal = null, string? Parameter = null);

public sealed record DashboardParameterDefinition(
    string Name, string Type, bool Required = false, bool Nullable = false, DashboardValue? Default = null);

/// <summary>One predicate leaf or exactly one nonempty all/any branch.</summary>
public sealed record DashboardPredicate
{
    public string? Field { get; init; }
    public string? Metric { get; init; }
    public string? Operator { get; init; }
    public DashboardValue? Value { get; init; }
    public IReadOnlyList<DashboardValue>? Values { get; init; }
    public IReadOnlyList<DashboardPredicate>? All { get; init; }
    public IReadOnlyList<DashboardPredicate>? Any { get; init; }
}

public sealed record DashboardMetricSelection(
    string Alias, string? Metric = null, string? Revision = null, string? Aggregate = null, string? Field = null);

public sealed record DashboardSort(string Reference, string Direction = "ascending", string Nulls = "last");

public sealed record DashboardTimeRange
{
    public string Field { get; init; } = string.Empty;
    public string? Window { get; init; }
    public DateTimeOffset? From { get; init; }
    public DateTimeOffset? To { get; init; }
    public string TimeZone { get; init; } = "UTC";
    public string WeekStart { get; init; } = "monday";
    public string? Bucket { get; init; }
}

public sealed record DashboardComparison(string Mode, DateTimeOffset? From = null, DateTimeOffset? To = null);

public sealed record DashboardQueryDefinition
{
    public int SchemaVersion { get; init; } = 1;
    public DashboardDatasetReference Dataset { get; init; } = new(string.Empty, string.Empty);
    public string Form { get; init; } = "aggregate";
    public IReadOnlyList<DashboardParameterDefinition> Parameters { get; init; } = [];
    public DashboardPredicate? Filters { get; init; }
    public IReadOnlyList<string> Fields { get; init; } = [];
    public IReadOnlyList<string> GroupBy { get; init; } = [];
    public IReadOnlyList<DashboardMetricSelection> Metrics { get; init; } = [];
    public DashboardTimeRange? Time { get; init; }
    public DashboardComparison? Comparison { get; init; }
    public DashboardPredicate? Having { get; init; }
    public IReadOnlyList<DashboardSort> Sort { get; init; } = [];
    public int Limit { get; init; } = 50;
}

public sealed record DashboardVisualization
{
    public string Kind { get; init; } = "table";
    public string? X { get; init; }
    public IReadOnlyList<string> Y { get; init; } = [];
    public string? Label { get; init; }
}

public sealed record DashboardQueryRequest
{
    public DashboardQueryDefinition Query { get; init; } = new();
    public DashboardVisualization Visualization { get; init; } = new();
    public IReadOnlyDictionary<string, DashboardValue> ParameterValues { get; init; }
        = new Dictionary<string, DashboardValue>(StringComparer.Ordinal);
}

public sealed record DashboardDatasetOwner(string DatasetId, string OwnerModuleId);

public sealed record DashboardFieldDefinition(
    string Id, string Label, string Type, IReadOnlyList<string> FilterOperators,
    bool Nullable = false, bool Groupable = false, bool Sortable = false, bool Projectable = true,
    string? Unit = null, string? Description = null,
    IReadOnlyList<string>? Aggregates = null, bool BusinessDimension = false);

public sealed record DashboardMetricDefinition(
    string Id, string Revision, string Label, string Unit, string Description,
    bool Comparable = false, IReadOnlyList<string>? RequiredFields = null);

public sealed record DashboardRelationshipDefinition(string Id, string Description, string Cardinality);

public sealed record DashboardDatasetTimeSupport(
    IReadOnlyList<string> Fields, IReadOnlyList<string> Windows, IReadOnlyList<string> Buckets,
    IReadOnlyList<string> ComparisonModes);

/// <summary>Metadata already filtered by the owning module for the current caller.</summary>
public sealed record DashboardDatasetDeclaration
{
    public string Id { get; init; } = string.Empty;
    public string OwnerModuleId { get; init; } = string.Empty;
    public string SchemaRevision { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string Availability { get; init; } = "ready";
    public string? UnavailableReason { get; init; }
    public IReadOnlyList<DashboardFieldDefinition> Fields { get; init; } = [];
    public IReadOnlyList<DashboardMetricDefinition> Metrics { get; init; } = [];
    public IReadOnlyList<DashboardRelationshipDefinition> Relationships { get; init; } = [];
    public DashboardDatasetTimeSupport? Time { get; init; }
    public IReadOnlyList<string> Forms { get; init; } = ["aggregate", "records"];
    public bool SupportsDrill { get; init; }
    public bool SupportsExport { get; init; }
    public string CoverageDescription { get; init; } = "Collection and retention coverage is unknown.";
}

public sealed record DashboardQueryProblem(string Code, string Path, string Message);

public sealed record DashboardQueryValidation(
    bool IsValid, IReadOnlyList<DashboardQueryProblem> Problems, string? Interpretation = null);

public sealed record DashboardEffectivePeriod(string Role, DateTimeOffset From, DateTimeOffset To, string TimeZone);

public sealed record DashboardResultColumn(string Id, string Label, string Type, string? Unit = null);

public sealed record DashboardResultCell(string Type, string? Value, string? NullReason = null);

public sealed record DashboardDrillDescriptor(
    string DatasetId, string SchemaRevision, string QueryFingerprint, string Period,
    IReadOnlyDictionary<string, DashboardValue> GroupValues,
    IReadOnlyList<DashboardEffectivePeriod>? Periods = null, DateTimeOffset? ObservedAt = null,
    string? Token = null);

public sealed record DashboardResultRow(
    string Key, IReadOnlyDictionary<string, DashboardResultCell> Values, DashboardDrillDescriptor? Drill = null);

public sealed record DashboardCoverage(string Status, string Description, DateTimeOffset? From = null, DateTimeOffset? To = null);

public sealed record DashboardQueryResult
{
    public string Outcome { get; init; } = "succeeded";
    public string? ErrorCode { get; init; }
    public string? Message { get; init; }
    public string QueryFingerprint { get; init; } = string.Empty;
    public string DatasetId { get; init; } = string.Empty;
    public string SchemaRevision { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public string Interpretation { get; init; } = string.Empty;
    public DashboardCoverage Coverage { get; init; } = new("unknown", "Collection and retention coverage is unknown.");
    public IReadOnlyList<DashboardEffectivePeriod> Periods { get; init; } = [];
    public IReadOnlyList<DashboardResultColumn> Columns { get; init; } = [];
    public IReadOnlyList<DashboardResultRow> Rows { get; init; } = [];
    public string? NextCursor { get; init; }
}

public sealed record DashboardDrillRequest(
    DashboardQueryRequest Question, DashboardDrillDescriptor Descriptor, int PageSize = 25, string? Cursor = null);

/// <summary>One validated request, its admitted declaration and resolved literal parameter values.</summary>
public sealed record DashboardQueryContext(
    DashboardRequestScope Scope, DashboardQueryRequest Request, DashboardDatasetDeclaration Dataset,
    IReadOnlyDictionary<string, DashboardValue> Parameters, string QueryFingerprint,
    DateTimeOffset EvaluatedAt, IReadOnlyList<DashboardEffectivePeriod> Periods);

/// <summary>Shared hard limits; module implementations must also bound traversal and aggregation.</summary>
public sealed class DashboardQueryLimits
{
    public int MaximumDefinitionBytes { get; init; } = 65536;
    public int MaximumSourceRows { get; init; } = 100000;
    public int MaximumGroups { get; init; } = 200;
    public int MaximumResultRows { get; init; } = 200;
    public int MaximumConcurrentReads { get; init; } = 2;
    public TimeSpan ExecutionTimeout { get; init; } = TimeSpan.FromSeconds(10);
}
