namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

/// <summary>
/// Admits the trace-detail constituents from typed Groundwork observations: the two primary-key reads
/// through their point-read evidence (catalog uniqueness witness and native key search) and the two
/// bounded page sequences through per-page bounded-query evidence, including the emitted keyset
/// continuation predicate of every page after the first. No rule reads command text or plan syntax.
/// </summary>
public static partial class DiagnosticsNativePlanContract
{
    internal const string PrimaryKeyReadClassification = "primary-key-read";

    internal static string LogicalUnitIdForTable(string tableName) => tableName switch
    {
        "elsa_otel_trace_summaries_v3" => "elsa-otel-trace-summaries-v3",
        "elsa_otel_spans_v2" => "elsa-otel-spans-v2",
        "elsa_otel_logs_v2" => "elsa-otel-logs-v2",
        "elsa_otel_resources_v2" => "elsa-otel-resources-v2",
        _ => throw Reject($"Table '{tableName}' has no structured evidence unit.")
    };

    internal static DiagnosticsNativeRouteSpec RouteSpecificationFor(DiagnosticsTraceDetailConstituentSpec specification) =>
        new(
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            specification.Ordering.Count == 0 ? null : specification.Ordering[0].Column,
            specification.PredicateColumn,
            specification.PhysicalCardinality,
            specification.FiniteLimit,
            specification.StorageScopeRequired,
            false,
            specification.Ordering,
            []);

    /// <summary>Validates one constituent's typed evidence against its specification and the observed provider version.</summary>
    public static void ValidateStructuredTraceDetailConstituent(
        string provider,
        string adapter,
        DiagnosticsTraceDetailConstituentEvidence constituent,
        string expectedProviderVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        ArgumentNullException.ThrowIfNull(constituent);
        var specification = TraceDetailConstituents(adapter).SingleOrDefault(item =>
            string.Equals(item.RouteIdentity, constituent.RouteIdentity, StringComparison.Ordinal)) ??
            throw Reject($"Trace-detail evidence names unknown constituent '{constituent.RouteIdentity}'.");
        if (constituent.RawPlanReference is not "" || constituent.RawPlanSha256 is not "" || constituent.CommandText is not "")
            throw Reject($"Typed trace-detail constituent '{constituent.RouteIdentity}' must not carry raw plan or command text.");
        var evidence = constituent.StructuredEvidence ?? throw Reject($"Trace-detail constituent '{constituent.RouteIdentity}' has no structured execution evidence.");
        if (constituent.PhysicalCardinality != specification.PhysicalCardinality ||
            constituent.FiniteLimit != specification.FiniteLimit ||
            constituent.PublicRowBound != specification.PublicRowBound ||
            constituent.MaxInvocationCount != specification.MaxInvocationCount ||
            constituent.HasStorageScopePredicate != ExpectedStorageScopePredicate(provider, specification.StorageScopeRequired) ||
            !constituent.HasRoutePredicate)
            throw Reject($"Trace-detail constituent '{constituent.RouteIdentity}' has unbound cardinality, limit, fanout, scope, or predicate facts.");
        var unit = LogicalUnitIdForTable(specification.TableName);
        if (specification.OperationKind == DiagnosticsTraceDetailOperationKind.PrimaryKeyRead)
        {
            if (constituent.PlanClassification != PrimaryKeyReadClassification || constituent.PhysicalIndexName is not "" ||
                constituent.Pages is { Count: > 0 })
                throw Reject($"Trace-detail point read '{constituent.RouteIdentity}' must not claim a secondary index or continuation page.");
            if (constituent.ObservedCommandCount <= 0 || constituent.ObservedCommandCount > specification.MaxInvocationCount ||
                constituent.MaterializedCandidateCount != constituent.ObservedCommandCount ||
                constituent.MaterializedCandidateCount > constituent.PublicRowBound)
                throw Reject($"Trace-detail point read '{constituent.RouteIdentity}' has unbound fanout or materialization counts.");
            ValidatePointReadEvidence(provider, evidence, unit, specification, expectedProviderVersion);
            return;
        }
        var expectedPages = checked((specification.PublicRowBound + specification.FiniteLimit - 1) / specification.FiniteLimit);
        var pages = constituent.Pages ?? [];
        if (constituent.PlanClassification != IndexSearchPlanClassification ||
            !string.Equals(constituent.PhysicalIndexName, ExpectedPhysicalIndexName(provider, RouteSpecificationFor(specification)), StringComparison.Ordinal) ||
            constituent.ObservedCommandCount != specification.MaxInvocationCount ||
            constituent.MaterializedCandidateCount != specification.PublicRowBound ||
            constituent.ObservedCommandCount != expectedPages ||
            pages.Count != expectedPages - 1)
            throw Reject($"Trace-detail query '{constituent.RouteIdentity}' does not carry exactly its frozen page sequence.");
        ValidateBoundedPageEvidence(provider, adapter, evidence, unit, specification, pageIndex: 0, expectedProviderVersion);
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index] ?? throw Reject($"Trace-detail query '{constituent.RouteIdentity}' has a null page entry.");
            if (page.PageIndex != index + 1 || page.RawPlanReference is not "" || page.RawPlanSha256 is not "" || page.CommandText is not "")
                throw Reject($"Trace-detail query '{constituent.RouteIdentity}' page {index + 1} is out of sequence or carries raw plan or command text.");
            var pageEvidence = page.StructuredEvidence ?? throw Reject($"Trace-detail query '{constituent.RouteIdentity}' page {page.PageIndex} has no structured execution evidence.");
            ValidateBoundedPageEvidence(provider, adapter, pageEvidence, unit, specification, page.PageIndex, expectedProviderVersion);
        }
    }

    private static void ValidateObservation(
        string provider,
        StructuredExecutionEvidence evidence,
        string operation,
        string unit,
        string expectedProviderVersion)
    {
        if (!string.Equals(evidence.Operation, operation, StringComparison.Ordinal) ||
            !string.Equals(evidence.Outcome, "Succeeded", StringComparison.Ordinal) ||
            !string.Equals(evidence.ShapeAvailability, "Collected", StringComparison.Ordinal) ||
            evidence.Target is null || !string.Equals(evidence.Target.LogicalUnitId, unit, StringComparison.Ordinal))
            throw Reject($"Trace-detail evidence is not a collected, succeeded {operation} of '{unit}'.");
        // The observation must come from the provider and the exact server version this run measured;
        // staged evidence from another provider or version is not this run's proof.
        if (!string.Equals(evidence.Provider, ProviderDisplayName(provider), StringComparison.OrdinalIgnoreCase))
            throw Reject($"Trace-detail evidence was not observed on {ProviderDisplayName(provider)}.");
        if (string.IsNullOrWhiteSpace(expectedProviderVersion) ||
            !string.Equals(evidence.ProviderVersion, expectedProviderVersion, StringComparison.Ordinal))
            throw Reject("Trace-detail evidence did not identify the observed provider version.");
    }

    /// <summary>
    /// A point read proves its key equality (plus the scope binding relational providers emit, or the
    /// physical scope target MongoDB addresses), the catalog's uniqueness witness over exactly that key
    /// with scope, and a native plan that is one key search of the target.
    /// </summary>
    private static void ValidatePointReadEvidence(
        string provider,
        StructuredExecutionEvidence evidence,
        string unit,
        DiagnosticsTraceDetailConstituentSpec specification,
        string expectedProviderVersion)
    {
        ValidateObservation(provider, evidence, "PointRead", unit, expectedProviderVersion);
        var scopePredicate = ExpectedStorageScopePredicate(provider, specification.StorageScopeRequired);
        var expectedBinding = scopePredicate ? "Predicate" : "PhysicalTarget";
        if (!string.Equals(evidence.Target.ScopeBinding, expectedBinding, StringComparison.Ordinal))
            throw Reject($"Trace-detail point read '{specification.RouteIdentity}' does not bind its scope as '{expectedBinding}'.");
        var read = evidence.PointRead ?? throw Reject($"Trace-detail point read '{specification.RouteIdentity}' has no point-read evidence.");
        var keys = read.KeyBounds.Where(bound => bound.BindingRole == "Key").ToArray();
        var scopes = read.KeyBounds.Where(bound => bound.BindingRole == "Scope").ToArray();
        if (keys.Length != 1 || !string.Equals(keys[0].LogicalColumn, specification.PredicateColumn, StringComparison.Ordinal) ||
            keys[0].ValueType != "String" || scopes.Length != (scopePredicate ? 1 : 0) ||
            read.KeyBounds.Count != keys.Length + scopes.Length)
            throw Reject($"Trace-detail point read '{specification.RouteIdentity}' does not bind exactly its key and scope.");
        if (!read.MaterializerReadsAtMostOne || read.LockMode != "None")
            throw Reject($"Trace-detail point read '{specification.RouteIdentity}' is not an unlocked at-most-one read.");
        if (read.Uniqueness.Status != "Observed" ||
            !read.Uniqueness.EnforcedKeyColumns.SequenceEqual([specification.PredicateColumn], StringComparer.Ordinal) ||
            !read.Uniqueness.IncludesScopeBinding)
            throw Reject($"Trace-detail point read '{specification.RouteIdentity}' has no catalog witness that scope and its key are unique.");
        var plan = evidence.Plan ?? throw Reject("Trace-detail point read plan evidence is missing.");
        if (plan.Availability != "Collected" || !string.Equals(plan.Provenance, ExpectedPlanProvenance(provider), StringComparison.Ordinal) ||
            plan.FailureCategory is not null)
            throw Reject($"Trace-detail point read '{specification.RouteIdentity}' did not collect its native plan.");
        var nodes = plan.Nodes ?? throw Reject("Trace-detail point read plan nodes are missing.");
        var access = nodes.Where(node => node.TargetId is not null).ToArray();
        // The key search is the primary-key search, or the seek on the primary key's own index where a
        // provider exposes that index without a logical name (PostgreSQL); a declared index is not it.
        if (access.Length != 1 || access[0].TargetId != evidence.Target.PhysicalTargetId ||
            !(access[0].Operation == "PrimaryKeySearch" || (access[0].Operation == "IndexSearch" && access[0].LogicalIndexName is null)))
            throw Reject($"Trace-detail point read '{specification.RouteIdentity}' is not one key search of its target.");
        var passThrough = provider == "sqlserver" ? SqlServerPassThroughOperations : PassThroughOperations;
        foreach (var node in nodes)
        {
            if (!ReferenceEquals(node, access[0]) && !passThrough.Contains(node.Operation))
                throw Reject($"Trace-detail point read '{specification.RouteIdentity}' carries unexpected native work '{node.Operation}'.");
            if (node.Details?.Spill?.Spilled == true || node.Details?.NativeSortKeys is not null)
                throw Reject($"Trace-detail point read '{specification.RouteIdentity}' observed a sort or spill.");
        }
    }

    /// <summary>
    /// Every page is the route's bounded read of the trace's rows in the frozen ordering with the
    /// lookahead fetch limit and the expected index search; every page after the first carries the
    /// emitted keyset continuation predicate, lexicographic branches or a native tuple, over exactly
    /// the ordering terms in order.
    /// </summary>
    private static void ValidateBoundedPageEvidence(
        string provider,
        string adapter,
        StructuredExecutionEvidence evidence,
        string unit,
        DiagnosticsTraceDetailConstituentSpec specification,
        int pageIndex,
        string expectedProviderVersion)
    {
        ValidateObservation(provider, evidence, "BoundedQuery", unit, expectedProviderVersion);
        var routeSpecification = RouteSpecificationFor(specification);
        var scopePredicate = ExpectedStorageScopePredicate(provider, specification.StorageScopeRequired);
        var nativeFetchLimit = ExpectedNativeFetchLimit(routeSpecification);
        var query = evidence.BoundedQuery ?? throw Reject($"Trace-detail page {pageIndex} of '{specification.RouteIdentity}' has no bounded-query evidence.");
        ValidatePredicate(query.Predicate, routeSpecification, scopePredicate, replay: false);
        ValidateOrdering(query.Ordering, routeSpecification);
        if (query.NativeLimit.Kind != "Explicit" || query.NativeLimit.Value != nativeFetchLimit || !query.HasLookahead ||
            query.IncludesTotalCount || !IsNoOffset(query.NativeOffset))
            throw Reject($"Trace-detail page {pageIndex} of '{specification.RouteIdentity}' does not fetch the route's lookahead page.");
        if (query.HasContinuation != pageIndex > 0 || (query.Continuation is not null) != pageIndex > 0)
            throw Reject($"Trace-detail page {pageIndex} of '{specification.RouteIdentity}' misreports its continuation.");
        if (query.Continuation is { } continuation)
            ValidateContinuation(continuation, routeSpecification.EffectiveOrdering, specification.RouteIdentity, pageIndex);
        try
        {
            ValidatePlan(evidence.Plan, evidence.Target.PhysicalTargetId, provider, routeSpecification, nativeFetchLimit);
        }
        catch (PerformanceContractException exception)
        {
            throw new PerformanceContractException($"{exception.Message} (page {pageIndex})");
        }
    }

    private static void ValidateContinuation(
        StructuredContinuationPredicate continuation,
        IReadOnlyList<RuntimeNativeOrderTerm> ordering,
        string routeIdentity,
        int pageIndex)
    {
        static string BoundOperator(RuntimeNativeOrderTerm term) =>
            term.Direction == RuntimeNativeOrderDirection.Descending ? "UpperBound" : "LowerBound";
        // A fact on an ordinal identity term must compare strings ordinally; every other term compares
        // its value exactly. A mismatched type or comparison is a different ordering, not this route's.
        static bool HasTermSemantics(StructuredPredicateFact fact, RuntimeNativeOrderTerm term) =>
            IsOrdinalStringOrderColumn(term.Column)
                ? fact.ValueType == "String" && fact.Comparison == "Ordinal"
                : fact.Comparison == "Exact";
        static bool IsBound(StructuredPredicateFact fact, RuntimeNativeOrderTerm term) =>
            fact is not null &&
            string.Equals(fact.LogicalColumn, term.Column, StringComparison.Ordinal) &&
            fact.Operator == BoundOperator(term) &&
            fact.BoundInclusivity == "Exclusive" &&
            fact.BindingRole == "Continuation" &&
            fact.BindingId is not null &&
            HasTermSemantics(fact, term);
        switch (continuation.Form)
        {
            case "Lexicographic":
                if (continuation.Branches.Count != ordering.Count || continuation.TupleBounds.Count != 0)
                    throw Reject($"Trace-detail page {pageIndex} of '{routeIdentity}' does not continue on every ordering term.");
                for (var index = 0; index < ordering.Count; index++)
                {
                    var branch = continuation.Branches[index] ?? throw Reject($"Trace-detail page {pageIndex} of '{routeIdentity}' has a null continuation branch.");
                    if (branch.Equalities.Count != index ||
                        branch.Equalities.Where((equality, position) => equality is null ||
                            !string.Equals(equality.LogicalColumn, ordering[position].Column, StringComparison.Ordinal) ||
                            equality.Operator != "Equal" || equality.BindingRole != "Continuation" || equality.BindingId is null ||
                            !HasTermSemantics(equality, ordering[position])).Any() ||
                        !IsBound(branch.Boundary, ordering[index]))
                        throw Reject($"Trace-detail page {pageIndex} of '{routeIdentity}' continuation branch {index} is not the route's keyset branch.");
                }
                return;
            case "Tuple":
                if (continuation.Branches.Count != 0 || continuation.TupleBounds.Count != ordering.Count ||
                    continuation.TupleBounds.Where((bound, position) => !IsBound(bound, ordering[position])).Any())
                    throw Reject($"Trace-detail page {pageIndex} of '{routeIdentity}' tuple continuation is not the route's ordering.");
                return;
            default:
                throw Reject($"Trace-detail page {pageIndex} of '{routeIdentity}' has an unknown continuation form '{continuation.Form}'.");
        }
    }
}
