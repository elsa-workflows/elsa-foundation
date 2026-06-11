using Groundwork.Core.Indexing;
using Groundwork.Core.Queries;
using Groundwork.Core.Workloads;

namespace Groundwork.Core.Manifests;

public sealed record StorageUnit(
    StorageUnitIdentity Identity,
    string DisplayName,
    WorkloadClassification Workload,
    LifecyclePolicy Lifecycle,
    IdentityPolicy IdentityPolicy,
    TenancyPolicy Tenancy,
    ConcurrencyPolicy Concurrency,
    SerializationPolicy Serialization,
    IReadOnlyList<IndexDeclaration> Indexes,
    IReadOnlyList<PortableQueryDeclaration> Queries,
    PhysicalizationPolicy Physicalization);
