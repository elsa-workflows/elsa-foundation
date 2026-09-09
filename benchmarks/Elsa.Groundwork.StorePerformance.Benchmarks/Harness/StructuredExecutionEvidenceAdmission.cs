namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

public static partial class DiagnosticsNativePlanContract
{
    /// <summary>
    /// The diagnostics routes whose admission reads typed callback evidence instead of provider plan
    /// text, per provider. A route is listed only where Groundwork reports both a collected bounded-query
    /// shape and a collected plan for it (observed on 0.4.0-preview.22: persisted ordinal identity keys and
    /// the renderer's computed sort fields are typed since valence-works/groundwork-v2#432). The bounded
    /// resource routes admit the same scan-and-sort exception the raw path grants for the frozen 128-row
    /// catalog, proven from typed plan nodes and sort keys (#1594).
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> StructuredEvidenceRoutesByProvider =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["sqlite"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "structured-log-recent", "structured-log-replay", "metrics-by-last-seen", "logs-by-last-seen",
                "resources-by-last-seen", "resources-by-status", "resources-by-service"
            },
            ["postgresql"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "structured-log-recent", "structured-log-replay", "metrics-by-last-seen", "logs-by-last-seen",
                "traces-by-last-seen", "resources-by-last-seen", "resources-by-status", "resources-by-service"
            },
            ["sqlserver"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "structured-log-recent", "structured-log-replay", "metrics-by-last-seen", "logs-by-last-seen",
                "traces-by-last-seen", "resources-by-last-seen", "resources-by-status", "resources-by-service"
            },
            ["mongodb"] = new HashSet<string>(StringComparer.Ordinal)
            {
                "structured-log-recent", "structured-log-replay", "metrics-by-last-seen", "logs-by-last-seen",
                "traces-by-last-seen", "resources-by-last-seen", "resources-by-status", "resources-by-service"
            }
        };

    internal static bool IsStructuredEvidenceRoute(string provider, string adapter, string route) =>
        string.Equals(adapter, GroundworkAdapter, StringComparison.Ordinal) &&
        StructuredEvidenceRoutesByProvider.TryGetValue(provider, out var routes) &&
        routes.Contains(route);

    internal static void ValidateStructuredEvidence(
        string provider,
        string adapter,
        NativeRouteEvidence route,
        string expectedProviderVersion)
    {
        ValidateStructuredEvidence(provider, adapter, route);
        if (string.IsNullOrWhiteSpace(expectedProviderVersion) ||
            !string.Equals(route.StructuredEvidence!.ProviderVersion, expectedProviderVersion, StringComparison.Ordinal))
            throw Reject("Structured execution provider version must exactly match the requested and observed provider version.");
    }

    /// <summary>
    /// Admits a migrated route from typed callback evidence only. The raw SQL and native-plan artifacts
    /// remain available for unmigrated routes, but a migrated route cannot pass by reparsing either
    /// artifact or by trusting the legacy route summary fields. Every check is derived from the route
    /// specification, so a provider that emits a different shape fails closed rather than being excused.
    /// </summary>
    internal static void ValidateStructuredEvidence(
        string provider,
        string adapter,
        NativeRouteEvidence route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        ArgumentNullException.ThrowIfNull(route);
        if (!IsStructuredEvidenceRoute(provider, adapter, route.RouteIdentity))
            throw Reject($"Structured evidence is not admitted for route '{route.RouteIdentity}' on provider '{provider}'.");
        var evidence = route.StructuredEvidence ?? throw Reject("Structured execution evidence is missing.");
        var specification = For(adapter, route.RouteIdentity);
        var replay = string.Equals(route.RouteIdentity, "structured-log-replay", StringComparison.Ordinal);
        var scopePredicate = ExpectedStorageScopePredicate(provider, specification);
        var nativeFetchLimit = ExpectedNativeFetchLimit(specification);
        var boundedScanSort = IsBoundedScanSortPlan(evidence.Plan, provider, adapter, specification);
        var expectedClassification = boundedScanSort ? BoundedCatalogScanSortPlanClassification : IndexSearchPlanClassification;
        var metadataMismatch =
            !string.Equals(route.PlanClassification, expectedClassification, StringComparison.Ordinal) ? "plan classification"
            : route.PhysicalCardinality != specification.PhysicalCardinality ? "physical cardinality"
            : route.FiniteLimit != specification.FiniteLimit ? "finite limit"
            : route.MaterializedCandidateCount != specification.FiniteLimit ? "materialized candidate count"
            : route.HasStorageScopePredicate != scopePredicate ? "storage scope predicate"
            : route.HasRoutePredicate != (specification.PredicateColumn is not null) ? "route predicate"
            : !string.Equals(route.IndexName, ExpectedPhysicalIndexName(provider, specification), StringComparison.Ordinal) ? "index name"
            : route.NativeFetchLimit != nativeFetchLimit ? "native fetch limit"
            : route.ResultShape != RuntimeNativeResultShape.Page ? "result shape"
            : route.ScalarResultCount is not null ? "scalar result count"
            : route.UsesLatestPerKey ? "latest-per-key"
            : null;
        if (metadataMismatch is not null)
            throw Reject($"Structured evidence route metadata is not the frozen bounded route shape ({metadataMismatch}).");
        if (evidence.SchemaVersion != 1 ||
            !string.Equals(evidence.Provider, ProviderDisplayName(provider), StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(evidence.ProviderVersion) ||
            !string.Equals(evidence.Operation, "BoundedQuery", StringComparison.Ordinal) ||
            !string.Equals(evidence.CommandKind, "Read", StringComparison.Ordinal) ||
            !string.Equals(evidence.Role, "Statement", StringComparison.Ordinal) ||
            !string.Equals(evidence.Outcome, "Succeeded", StringComparison.Ordinal) ||
            evidence.FailureCategory is not null ||
            !string.Equals(evidence.ShapeAvailability, "Collected", StringComparison.Ordinal))
            throw Reject($"Structured execution evidence does not prove a successful collected {ProviderDisplayName(provider)} bounded read.");
        if (evidence.PointRead is not null)
            throw Reject("A bounded route may not carry point-read evidence.");
        if (evidence.Identity is null ||
            evidence.Target is null ||
            evidence.Identity.CaptureId == Guid.Empty ||
            evidence.Identity.InvocationId == Guid.Empty ||
            evidence.Identity.CommandId == Guid.Empty ||
            evidence.Identity.StatementId == Guid.Empty ||
            evidence.Identity.CommandOrdinal != 0 ||
            evidence.Identity.StatementOrdinal != 0)
            throw Reject("Structured execution identity is missing or outside the single-command route.");
        if (!string.Equals(evidence.Target.LogicalUnitId, LogicalUnitIdFor(route.RouteIdentity), StringComparison.Ordinal) ||
            evidence.Target.PhysicalTargetId == Guid.Empty ||
            !string.Equals(evidence.Target.ScopeBinding, scopePredicate ? "Predicate" : "PhysicalTarget", StringComparison.Ordinal))
            throw Reject("Structured execution target does not prove the scoped storage unit.");
        var query = evidence.BoundedQuery ?? throw Reject("The collected bounded-query shape is missing.");
        ValidatePredicate(query.Predicate, specification, scopePredicate, replay);
        ValidateOrdering(query.Ordering ?? throw Reject("Structured ordering evidence is missing."), specification);
        if (query.Projection is null ||
            query.Projection.LogicalColumns is null ||
            query.Projection.AllColumns != true || query.Projection.LogicalColumns.Count != 0 ||
            query.NativeOffset is null || query.NativeLimit is null ||
            !IsNoOffset(query.NativeOffset) ||
            query.NativeLimit.Kind != "Explicit" || query.NativeLimit.Value != nativeFetchLimit ||
            query.HasContinuation || !query.HasLookahead || query.IncludesTotalCount)
            throw Reject("Structured bounded-query paging or projection facts are not the emitted route shape.");
        if (boundedScanSort)
            ValidateBoundedScanSortPlan(evidence.Plan!, evidence.Target.PhysicalTargetId, provider, specification, nativeFetchLimit);
        else
            ValidatePlan(evidence.Plan, evidence.Target.PhysicalTargetId, provider, specification, nativeFetchLimit);
    }

    /// <summary>
    /// The frozen 128-row resource catalog may legitimately be scanned, or read through an index that does
    /// not carry its ordering, and then sorted; the raw path admits that exact shape per provider, and the
    /// typed path admits it only when the collected plan of a bounded resource route contains a sort.
    /// </summary>
    private static bool IsBoundedScanSortPlan(
        StructuredPlanEvidence? plan,
        string provider,
        string adapter,
        DiagnosticsNativeRouteSpec specification) =>
        plan is { Availability: "Collected", Nodes: { } nodes } &&
        IsBoundedResourceRoute(provider, adapter, specification) &&
        nodes.Any(node => node is not null && SortOperations.Contains(node.Operation));

    /// <summary>
    /// The indexes a bounded resource route may read through before sorting: its declared index, or for
    /// the status route on MongoDB the captured status-only index the raw path already admits.
    /// </summary>
    private static bool IsAdmissibleBoundedScanIndex(string provider, DiagnosticsNativeRouteSpec specification, string? logicalIndexName) =>
        string.Equals(logicalIndexName, specification.IndexName, StringComparison.Ordinal) ||
        (provider == "mongodb" &&
         specification.RouteIdentity == "resources-by-status" &&
         string.Equals(logicalIndexName, MongoStatusOnlyResourceIndex, StringComparison.Ordinal));

    internal static string ClassifyStructuredPlan(
        string provider,
        string adapter,
        DiagnosticsNativeRouteSpec specification,
        StructuredPlanEvidence? plan) =>
        IsBoundedScanSortPlan(plan, provider, adapter, specification)
            ? BoundedCatalogScanSortPlanClassification
            : IndexSearchPlanClassification;

    private static readonly IReadOnlySet<string> SortOperations =
        new HashSet<string>(StringComparer.Ordinal) { "Sort", "TopNSort" };

    private static readonly IReadOnlySet<string> ScanOperations =
        new HashSet<string>(StringComparer.Ordinal) { "TableScan", "IndexScan", "IndexSearch" };

    /// <summary>
    /// Native work a provider adds around a bounded catalog scan and sort: the ordinal string keys are
    /// computed by PostgreSQL as aggregate-over-function subplans and by MongoDB as compute stages.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> BoundedScanSortSupportByProvider =
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.Ordinal)
        {
            ["postgresql"] = new HashSet<string>(StringComparer.Ordinal) { "Limit", "Materialize", "Projection", "Aggregate", "FunctionScan" },
            // SQL Server applies the scope predicate as a Filter over the scan and computes the collated keys in a Compute Scalar.
            ["sqlserver"] = new HashSet<string>(StringComparer.Ordinal) { "Limit", "Materialize", "Projection", "Compute", "Filter" },
            ["mongodb"] = new HashSet<string>(StringComparer.Ordinal) { "Limit", "Materialize", "Projection", "Compute" }
        };

    /// <summary>
    /// A bounded scan-and-sort plan proves exactly one scan of the statement's target, exactly one sort
    /// whose observed native keys are the route's complete ordering with the ordinal transforms the
    /// route's string columns require, no spill, and no bound other than the route's lookahead limit.
    /// </summary>
    private static void ValidateBoundedScanSortPlan(
        StructuredPlanEvidence plan,
        Guid targetId,
        string provider,
        DiagnosticsNativeRouteSpec specification,
        int nativeFetchLimit)
    {
        if (!string.Equals(plan.Provenance, ExpectedPlanProvenance(provider), StringComparison.Ordinal) ||
            !string.Equals(plan.ExpectedLogicalIndex, specification.IndexName, StringComparison.Ordinal) ||
            plan.FailureCategory is not null ||
            plan.CollectionCommandCount is null or < 1)
            throw Reject("Structured plan evidence does not prove a collected bounded catalog scan.");
        var nodes = plan.Nodes ?? throw Reject("Structured winning-plan nodes are missing.");
        if (nodes.Count == 0 || nodes.Any(candidate => candidate is null))
            throw Reject("Structured winning-plan evidence is empty.");
        if (!BoundedScanSortSupportByProvider.TryGetValue(provider, out var support))
            throw Reject($"Provider '{provider}' has no bounded catalog scan contract.");
        var scans = nodes.Where(node => ScanOperations.Contains(node.Operation)).ToArray();
        if (scans.Length != 1 || scans[0].TargetId != targetId)
            throw Reject("A bounded catalog scan must contain exactly one scan of the statement target.");
        if (scans[0].Operation != "TableScan" && !IsAdmissibleBoundedScanIndex(provider, specification, scans[0].LogicalIndexName))
            throw Reject("A bounded catalog index read is not through an admitted index.");
        var sorts = nodes.Where(node => SortOperations.Contains(node.Operation)).ToArray();
        if (sorts.Length != 1)
            throw Reject("A bounded catalog scan must contain exactly one sort.");
        foreach (var node in nodes)
        {
            if (!ReferenceEquals(node, scans[0]) && !ReferenceEquals(node, sorts[0]) && !support.Contains(node.Operation))
                throw Reject($"Structured winning-plan evidence carries unexpected native work '{node.Operation}'.");
            if (node.Details is not { } details)
                continue;
            if (details.Spill?.Spilled == true)
                throw Reject("Structured winning-plan evidence observed a spill.");
            if (string.Equals(details.NativeLimit.Kind, "Explicit", StringComparison.Ordinal) &&
                details.NativeLimit.Value != nativeFetchLimit)
                throw Reject("Structured winning-plan evidence observed a native bound other than the route's lookahead limit.");
        }
        var keys = sorts[0].Details?.NativeSortKeys ?? throw Reject("The bounded catalog sort did not observe its native sort keys.");
        var expected = specification.EffectiveOrdering;
        if (keys.Count != expected.Count)
            throw Reject("The bounded catalog sort keys are not the route's complete ordering.");
        for (var index = 0; index < expected.Count; index++)
        {
            var key = keys[index];
            var column = expected[index];
            var ordinal = IsOrdinalStringOrderColumn(column.Column);
            var direction = column.Direction == RuntimeNativeOrderDirection.Descending ? "Descending" : "Ascending";
            if (key is null ||
                !string.Equals(key.LogicalColumn, column.Column, StringComparison.Ordinal) ||
                !string.Equals(key.Direction, direction, StringComparison.Ordinal) ||
                key.Transforms is null ||
                key.Transforms.Any(transform => transform is not ("OrdinalStringKey" or "PhysicalSearchKey")) ||
                (ordinal ? key.Transforms.Count != 1 : key.Transforms.Count != 0))
                throw Reject($"Bounded catalog sort key {index} is not the route's ordering term.");
        }
    }

    /// <summary>
    /// A route emits no offset. A provider whose paging syntax always states a row offset before its
    /// row count (SQL Server) reports an explicit zero, which is the same fact.
    /// </summary>
    private static bool IsNoOffset(StructuredNativeBound offset) =>
        offset.Kind == "Absent" && offset.Value is null ||
        offset.Kind == "Explicit" && offset.Value == 0;

    private static string ProviderDisplayName(string provider) => provider switch
    {
        "sqlite" => "SQLite",
        "postgresql" => "PostgreSQL",
        "sqlserver" => "SQL Server",
        "mongodb" => "MongoDB",
        _ => throw Reject($"Provider '{provider}' has no structured evidence contract.")
    };

    private static string ExpectedPlanProvenance(string provider) => provider switch
    {
        "sqlite" or "postgresql" => "EstimatedExplain",
        "sqlserver" or "mongodb" => "ExplainReplay",
        _ => throw Reject($"Provider '{provider}' has no structured plan contract.")
    };

    internal static string LogicalUnitIdFor(string route) => route switch
    {
        "structured-log-recent" or "structured-log-replay" => "elsa-structured-logs",
        "traces-by-last-seen" => "elsa-otel-trace-summaries-v3",
        "metrics-by-last-seen" => "elsa-otel-metric-points-v2",
        "logs-by-last-seen" => "elsa-otel-logs-v2",
        "resources-by-last-seen" or "resources-by-status" or "resources-by-service" => "elsa-otel-resources-v2",
        _ => throw Reject($"Route '{route}' has no structured evidence unit.")
    };

    private static void ValidatePredicate(
        StructuredConjunctionPredicate? predicate,
        DiagnosticsNativeRouteSpec specification,
        bool scopePredicate,
        bool replay)
    {
        var facts = predicate?.Facts ?? throw Reject("Structured predicate evidence is missing.");
        if (facts.Any(fact => fact is null))
            throw Reject("Structured predicate evidence contains a null fact.");
        var expected = (scopePredicate ? 1 : 0) + (specification.PredicateColumn is null ? 0 : 1) + (replay ? 2 : 0);
        if (facts.Count != expected)
            throw Reject("Structured predicate evidence must contain exactly the emitted scope and route predicates.");
        if (scopePredicate)
            RequireFact(facts, "__groundwork_scope", "Equal", "String", "Ordinal", "NotApplicable", "Scope");
        if (specification.PredicateColumn is { } column)
            RequireFact(facts, column, "Equal", null, null, "NotApplicable", "Caller");
        if (replay)
        {
            RequireFact(facts, "sequence", "LowerBound", "Int64", "Exact", "Exclusive", "Caller");
            RequireFact(facts, "sequence", "UpperBound", "Int64", "Exact", "Inclusive", "Caller");
        }
    }

    private static void RequireFact(
        IReadOnlyList<StructuredPredicateFact> facts,
        string column,
        string @operator,
        string? valueType,
        string? comparison,
        string boundInclusivity,
        string bindingRole)
    {
        var matches = facts.Where(fact => fact is not null &&
                string.Equals(fact.LogicalColumn, column, StringComparison.Ordinal) &&
                string.Equals(fact.Operator, @operator, StringComparison.Ordinal) &&
                (valueType is null || string.Equals(fact.ValueType, valueType, StringComparison.Ordinal)) &&
                (comparison is null || string.Equals(fact.Comparison, comparison, StringComparison.Ordinal)) &&
                string.Equals(fact.BoundInclusivity, boundInclusivity, StringComparison.Ordinal) &&
                string.Equals(fact.BindingRole, bindingRole, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || matches[0].BindingId == Guid.Empty)
            throw Reject($"Structured predicate evidence is missing a unique complete binding fact for '{column}'.");
    }

    private static readonly IReadOnlySet<string> OrdinalOrderingTransforms =
        new HashSet<string>(StringComparer.Ordinal) { "OrdinalStringKey", "PhysicalSearchKey" };

    /// <summary>
    /// The emitted ordering must be the route's complete ordering in order and direction. An ordinal
    /// string column is emitted with the ordinal comparison and one of the provider's ordinal transforms;
    /// every other column is emitted exactly, without transforms or a null placement.
    /// </summary>
    private static void ValidateOrdering(IReadOnlyList<StructuredOrderTerm> ordering, DiagnosticsNativeRouteSpec specification)
    {
        var expected = specification.EffectiveOrdering;
        if (ordering.Count != expected.Count || ordering.Any(term => term is null))
            throw Reject("Structured ordering evidence does not contain the route's complete ordering.");
        for (var index = 0; index < expected.Count; index++)
        {
            var term = ordering[index];
            var column = expected[index];
            var ordinal = IsOrdinalStringOrderColumn(column.Column);
            if (term.Transforms is null ||
                !string.Equals(term.LogicalColumn, column.Column, StringComparison.Ordinal) ||
                !string.Equals(term.Direction, column.Direction == RuntimeNativeOrderDirection.Descending ? "Descending" : "Ascending", StringComparison.Ordinal) ||
                term.NullPlacement is not null ||
                (ordinal
                    ? !string.Equals(term.Comparison, "Ordinal", StringComparison.Ordinal) || term.Transforms.Count > 1 || term.Transforms.Any(transform => !OrdinalOrderingTransforms.Contains(transform))
                    : !string.Equals(term.Comparison, "Exact", StringComparison.Ordinal) || term.Transforms.Count != 0))
                throw Reject($"Structured ordering evidence term {index} is not the emitted route ordering.");
        }
    }

    private static readonly IReadOnlySet<string> AccessOperations =
        new HashSet<string>(StringComparer.Ordinal) { "IndexSearch", "IndexScan" };

    private static readonly IReadOnlySet<string> PassThroughOperations =
        new HashSet<string>(StringComparer.Ordinal) { "Limit", "Materialize", "Projection" };

    /// <summary>
    /// An index-search route proves exactly one access node on the expected logical index against the
    /// statement's target, with only limit, fetch and projection work around it: no sort, no scan, no
    /// filter, no observed spill, and no observed bound other than the route's own lookahead limit.
    /// </summary>
    private static void ValidatePlan(
        StructuredPlanEvidence? plan,
        Guid targetId,
        string provider,
        DiagnosticsNativeRouteSpec specification,
        int nativeFetchLimit)
    {
        if (plan is null)
            throw Reject("Structured plan evidence is missing.");
        if (!string.Equals(plan.Availability, "Collected", StringComparison.Ordinal) ||
            !string.Equals(plan.Provenance, ExpectedPlanProvenance(provider), StringComparison.Ordinal) ||
            plan.ChoseExpectedIndex != true ||
            !string.Equals(plan.ExpectedLogicalIndex, specification.IndexName, StringComparison.Ordinal) ||
            plan.ChosenPhysicalIndexId is not Guid chosenPhysicalIndexId ||
            chosenPhysicalIndexId == Guid.Empty ||
            plan.FailureCategory is not null ||
            plan.CollectionCommandCount is null or < 1)
            throw Reject("Structured plan evidence does not prove the collected selected index.");
        var nodes = plan.Nodes ?? throw Reject("Structured winning-plan nodes are missing.");
        if (nodes.Count == 0 || nodes.Any(candidate => candidate is null))
            throw Reject("Structured winning-plan evidence is empty.");
        var access = nodes.Where(node => AccessOperations.Contains(node.Operation)).ToArray();
        if (access.Length != 1)
            throw Reject("Structured winning-plan evidence must contain exactly one index access node.");
        var accessNode = access[0];
        if (accessNode.TargetId != targetId ||
            accessNode.IndexId != chosenPhysicalIndexId ||
            !string.Equals(accessNode.LogicalIndexName, specification.IndexName, StringComparison.Ordinal) ||
            accessNode.SortPurpose is not null)
            throw Reject("Structured winning-plan access node is not the expected index against the statement target.");
        foreach (var node in nodes)
        {
            if (!ReferenceEquals(node, accessNode) && !PassThroughOperations.Contains(node.Operation))
                throw Reject($"Structured winning-plan evidence carries unexpected native work '{node.Operation}'.");
            if (node.SortPurpose is not null)
                throw Reject("An index-search route must not observe a sort purpose.");
            if (node.Details is not { } details)
                continue;
            if (details.Spill?.Spilled == true)
                throw Reject("Structured winning-plan evidence observed a spill.");
            if (details.NativeSortKeys is not null)
                throw Reject("An index-search route must not observe native sort keys.");
            if (string.Equals(details.NativeLimit.Kind, "Explicit", StringComparison.Ordinal) &&
                details.NativeLimit.Value != nativeFetchLimit)
                throw Reject("Structured winning-plan evidence observed a native bound other than the route's lookahead limit.");
        }
    }

    private static PerformanceContractException Reject(string detail) =>
        new($"Structured diagnostics evidence rejected: {detail}");
}
