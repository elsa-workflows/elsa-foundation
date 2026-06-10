namespace Groundwork.Core.Workloads;

public sealed record WorkloadClassification(
    WorkloadFamily Family,
    WorkloadCandidateCategory CandidateCategory);

public enum WorkloadFamily
{
    MetadataConfiguration,
    CatalogAuthoredData,
    RuntimeDefinedBusinessData,
    RuntimeContinuationState,
    OperationalStream,
    Projection,
    AuditTrail
}

public enum WorkloadCandidateCategory
{
    GroundworkDefault,
    GroundworkWithPhysicalization,
    BenchmarkGated,
    SpecializedProvider
}
