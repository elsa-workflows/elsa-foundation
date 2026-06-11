using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Core.Workloads;

namespace Elsa.Persistence.Groundwork.Secrets;

public static class SecretsGroundworkManifestFactory
{
    public static StorageManifest Create() =>
        new(
            new StorageManifestIdentity("elsa.secrets"),
            new StorageManifestOwner("elsa.persistence.groundwork"),
            new StorageManifestVersion("1.0.0"),
            [
                new StorageUnit(
                    new StorageUnitIdentity("elsaSecret"),
                    "Elsa secret",
                    new WorkloadClassification(WorkloadFamily.MetadataConfiguration, WorkloadCandidateCategory.GroundworkDefault),
                    LifecyclePolicy.Mutable,
                    IdentityPolicy.StringId(),
                    TenancyPolicy.None,
                    ConcurrencyPolicy.Optimistic(),
                    SerializationPolicy.Json(),
                    [
                        new IndexDeclaration(
                            "by-name",
                            [new IndexField("name")],
                            IndexValueKind.Keyword,
                            true,
                            true,
                            MissingValueBehavior.Excluded,
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                        new IndexDeclaration(
                            "by-scope",
                            [new IndexField("scope")],
                            IndexValueKind.Keyword,
                            false,
                            true,
                            MissingValueBehavior.Excluded,
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                    ],
                    [
                        new PortableQueryDeclaration(
                            "find-by-name",
                            "by-name",
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                            QuerySortSupport.None,
                            QueryPagingSupport.None),
                        new PortableQueryDeclaration(
                            "list-by-scope",
                            "by-scope",
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                            QuerySortSupport.Both,
                            QueryPagingSupport.Offset)
                    ],
                    PhysicalizationPolicy.Portable)
            ],
            new HashSet<string> { "schema-history", "optimistic-concurrency" },
            ["Secrets-like validation manifest for the opt-in Elsa Groundwork bridge."]);
}
