namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Provider-neutral reference to admitted native-plan evidence. Providers adapt their physical plan result
/// to this record only after validating provider-specific cardinality, predicates, limits, and evidence kind.
/// </summary>
public sealed record ProviderNativePlanEvidence(
    string RouteIdentity,
    string EvidenceDigest,
    string PlanClassification,
    string IndexName,
    int FiniteLimit);
