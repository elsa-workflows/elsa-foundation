using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.Queries;
using Groundwork.Core.Workloads;

namespace Groundwork.Sqlite.Tests;

internal static class SqliteTestManifests
{
    public static StorageManifest MetadataManifest() =>
        new(
            new StorageManifestIdentity("configuration.documents"),
            new StorageManifestOwner("sample.application"),
            new StorageManifestVersion("1.0.0"),
            [
                new StorageUnit(
                    new StorageUnitIdentity("configurationDocument"),
                    "Configuration document",
                    new WorkloadClassification(WorkloadFamily.MetadataConfiguration, WorkloadCandidateCategory.GroundworkDefault),
                    LifecyclePolicy.Mutable,
                    IdentityPolicy.StringId(),
                    TenancyPolicy.None,
                    ConcurrencyPolicy.Optimistic(),
                    SerializationPolicy.Json(),
                    [
                        new IndexDeclaration(
                            "by-key",
                            [new IndexField("key")],
                            IndexValueKind.Keyword,
                            true,
                            true,
                            MissingValueBehavior.Excluded,
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                        new IndexDeclaration(
                            "by-category",
                            [new IndexField("category")],
                            IndexValueKind.String,
                            false,
                            true,
                            MissingValueBehavior.Excluded,
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal })
                    ],
                    [
                        new PortableQueryDeclaration(
                            "find-by-key",
                            "by-key",
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                            QuerySortSupport.None,
                            QueryPagingSupport.None),
                        new PortableQueryDeclaration(
                            "list-by-category",
                            "by-category",
                            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
                            QuerySortSupport.Both,
                            QueryPagingSupport.Offset)
                    ],
                    PhysicalizationPolicy.Portable)
            ],
            new HashSet<string> { "schema-history", "optimistic-concurrency" },
            []);

    public static ProviderIdentity Provider { get; } = new("groundwork-sqlite", "1.0.0");
}
