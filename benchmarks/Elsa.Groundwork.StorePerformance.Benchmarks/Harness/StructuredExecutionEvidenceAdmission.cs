namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

public static partial class DiagnosticsNativePlanContract
{
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
    /// Admits migrated structured-log consumer routes from typed callback evidence only. The raw SQL and
    /// native-plan artifacts remain available for unrelated routes, but these routes cannot pass by
    /// reparsing either artifact or by trusting the legacy route summary fields.
    /// </summary>
    internal static void ValidateStructuredEvidence(
        string provider,
        string adapter,
        NativeRouteEvidence route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapter);
        ArgumentNullException.ThrowIfNull(route);

        if (!string.Equals(provider, "sqlite", StringComparison.Ordinal) ||
            !string.Equals(adapter, GroundworkAdapter, StringComparison.Ordinal) ||
            route.RouteIdentity is not ("structured-log-replay" or "structured-log-recent"))
            throw Reject("Structured evidence is only admitted for the SQLite Groundwork structured-log routes.");

        var replay = string.Equals(route.RouteIdentity, "structured-log-replay", StringComparison.Ordinal);
        var specification = For(adapter, route.RouteIdentity);
        if (!string.Equals(route.PlanClassification, IndexSearchPlanClassification, StringComparison.Ordinal) ||
            route.PhysicalCardinality != specification.PhysicalCardinality ||
            route.FiniteLimit != specification.FiniteLimit ||
            route.MaterializedCandidateCount != specification.FiniteLimit ||
            route.HasStorageScopePredicate != true ||
            route.HasRoutePredicate ||
            !string.Equals(route.IndexName, ExpectedPhysicalIndexName("sqlite", specification), StringComparison.Ordinal) ||
            route.NativeFetchLimit != checked(specification.FiniteLimit + 1) ||
            route.ResultShape != RuntimeNativeResultShape.Page ||
            route.ScalarResultCount is not null ||
            route.UsesLatestPerKey)
            throw Reject("Structured evidence route metadata is not the frozen bounded structured-log shape.");

        var evidence = route.StructuredEvidence ?? throw Reject("Structured execution evidence is missing.");
        if (evidence.SchemaVersion != 1 ||
            !string.Equals(evidence.Provider, "SQLite", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(evidence.ProviderVersion) ||
            !string.Equals(evidence.Operation, "BoundedQuery", StringComparison.Ordinal) ||
            !string.Equals(evidence.CommandKind, "Read", StringComparison.Ordinal) ||
            !string.Equals(evidence.Role, "Statement", StringComparison.Ordinal) ||
            !string.Equals(evidence.Outcome, "Succeeded", StringComparison.Ordinal) ||
            evidence.FailureCategory is not null ||
            !string.Equals(evidence.ShapeAvailability, "Collected", StringComparison.Ordinal))
            throw Reject("Structured execution evidence does not prove a successful collected SQLite bounded read.");
        if (evidence.PointRead is not null)
            throw Reject("A bounded structured-log route may not carry point-read evidence.");

        if (evidence.Identity is null ||
            evidence.Target is null ||
            evidence.Identity.CaptureId == Guid.Empty ||
            evidence.Identity.InvocationId == Guid.Empty ||
            evidence.Identity.CommandId == Guid.Empty ||
            evidence.Identity.StatementId == Guid.Empty ||
            evidence.Identity.CommandOrdinal != 0 ||
            evidence.Identity.StatementOrdinal != 0)
            throw Reject("Structured execution identity is missing or outside the single-command route.");

        if (!string.Equals(evidence.Target.LogicalUnitId, "elsa-structured-logs", StringComparison.Ordinal) ||
            evidence.Target.PhysicalTargetId == Guid.Empty ||
            !string.Equals(evidence.Target.ScopeBinding, "Predicate", StringComparison.Ordinal))
            throw Reject("Structured execution target does not prove the scoped structured-log unit.");

        var query = evidence.BoundedQuery ?? throw Reject("The collected bounded-query shape is missing.");
        ValidatePredicate(query.Predicate, replay);
        ValidateOrdering(query.Ordering ?? throw Reject("Structured ordering evidence is missing."), replay);
        if (query.Projection is null ||
            query.Projection.LogicalColumns is null ||
            query.Projection.AllColumns != true || query.Projection.LogicalColumns.Count != 0 ||
            query.NativeOffset is null || query.NativeLimit is null ||
            query.NativeOffset.Kind != "Absent" || query.NativeOffset.Value is not null ||
            query.NativeLimit.Kind != "Explicit" || query.NativeLimit.Value != specification.FiniteLimit + 1 ||
            query.HasContinuation || !query.HasLookahead || query.IncludesTotalCount)
            throw Reject("Structured bounded-query paging or projection facts are not the emitted route shape.");

        var plan = evidence.Plan ?? throw Reject("Structured plan evidence is missing.");
        if (!string.Equals(plan.Availability, "Collected", StringComparison.Ordinal) ||
            !string.Equals(plan.Provenance, "EstimatedExplain", StringComparison.Ordinal) ||
            plan.ChoseExpectedIndex != true ||
            !string.Equals(plan.ExpectedLogicalIndex, specification.IndexName, StringComparison.Ordinal) ||
            plan.ChosenPhysicalIndexId is not Guid chosenPhysicalIndexId ||
            chosenPhysicalIndexId == Guid.Empty ||
            plan.FailureCategory is not null ||
            plan.CollectionCommandCount != 1)
            throw Reject("Structured plan evidence does not prove the collected selected index.");

        var nodes = plan.Nodes ?? throw Reject("Structured winning-plan nodes are missing.");
        if (nodes.Count != 1 || nodes.Any(candidate => candidate is null))
            throw Reject("Structured winning-plan evidence must contain exactly one SQLite access node.");
        var node = nodes[0];
        if (node.Id != 0 ||
            node.ParentId is not null ||
            !string.Equals(node.Operation, "IndexSearch", StringComparison.Ordinal) ||
            node.TargetId != evidence.Target.PhysicalTargetId ||
            node.IndexId != plan.ChosenPhysicalIndexId ||
            !string.Equals(node.LogicalIndexName, specification.IndexName, StringComparison.Ordinal) ||
            node.IsCovering is null ||
            node.SortPurpose is not null)
            throw Reject("Structured winning-plan evidence is not the exact SQLite structured-log index-search shape.");
    }

    private static void ValidatePredicate(StructuredConjunctionPredicate predicate, bool replay)
    {
        var facts = predicate?.Facts ?? throw Reject("Structured predicate evidence is missing.");
        if (facts.Count != (replay ? 3 : 1) || facts.Any(fact => fact is null))
            throw Reject(replay
                ? "Structured predicate evidence must contain the complete scope and replay bounds."
                : "Structured recent evidence must contain only its emitted scope predicate.");

        RequireFact(facts, "__groundwork_scope", "Equal", "String", "Ordinal", "NotApplicable", "Scope");
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
        string valueType,
        string comparison,
        string boundInclusivity,
        string bindingRole)
    {
        var matches = facts.Where(fact => fact is not null &&
                string.Equals(fact.LogicalColumn, column, StringComparison.Ordinal) &&
                string.Equals(fact.Operator, @operator, StringComparison.Ordinal) &&
                string.Equals(fact.ValueType, valueType, StringComparison.Ordinal) &&
                string.Equals(fact.Comparison, comparison, StringComparison.Ordinal) &&
                string.Equals(fact.BoundInclusivity, boundInclusivity, StringComparison.Ordinal) &&
                string.Equals(fact.BindingRole, bindingRole, StringComparison.Ordinal))
            .ToArray();
        if (matches.Length != 1 || matches[0].BindingId == Guid.Empty)
            throw Reject("Structured predicate evidence is missing a unique complete binding fact.");
    }

    private static void ValidateOrdering(IReadOnlyList<StructuredOrderTerm> ordering, bool replay)
    {
        if (ordering.Count != 1 || ordering.Any(term => term is null))
            throw Reject("Structured ordering evidence must contain exactly one emitted term.");
        var term = ordering[0];
        if (term.Transforms is null ||
            !string.Equals(term.LogicalColumn, "sequence", StringComparison.Ordinal) ||
            !string.Equals(term.Direction, replay ? "Ascending" : "Descending", StringComparison.Ordinal) ||
            term.NullPlacement is not null ||
            term.Transforms.Count != 0 ||
            !string.Equals(term.Comparison, "Exact", StringComparison.Ordinal))
            throw Reject("Structured ordering evidence is not the emitted structured-log sequence ordering.");
    }

    private static PerformanceContractException Reject(string detail) =>
        new($"Structured diagnostics evidence rejected: {detail}");
}
