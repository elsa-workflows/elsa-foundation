using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Serialization;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Spec 106 admission probes against the exact public Groundwork package family.
/// These tests deliberately describe the present capability boundary; they do not
/// fabricate an OpenIddict storage shape when the public API cannot express it.
/// </summary>
public sealed class OpenIddictGroundworkCapabilityProbeTests
{
    private const string ExpectedGroundworkVersion = "0.0.1-preview.81";

    [Fact]
    public void Repository_restores_one_exact_preview_81_package_family()
    {
        var packageDocument = XDocument.Load(Path.Combine(RepoRoot, "Directory.Packages.props"));
        var packageVersions = packageDocument.Descendants("PackageVersion")
            .Select(element => (
                Name: (string?)element.Attribute("Include"),
                Version: (string?)element.Attribute("Version")))
            .Where(item => item.Name?.StartsWith("Groundwork.", StringComparison.Ordinal) == true)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "Groundwork.Core",
                "Groundwork.DiagnosticRecords",
                "Groundwork.Documents",
                "Groundwork.MongoDb",
                "Groundwork.PostgreSql",
                "Groundwork.SqlServer",
                "Groundwork.Sqlite"
            },
            packageVersions.Select(item => item.Name).Order(StringComparer.Ordinal));
        Assert.All(packageVersions, item => Assert.Equal(ExpectedGroundworkVersion, item.Version));
    }

    [Fact]
    public void Repository_tool_matches_the_preview_81_package_family()
    {
        using var toolDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot, ".config", "dotnet-tools.json")));
        var toolVersion = toolDocument.RootElement
            .GetProperty("tools")
            .GetProperty("groundwork.tool")
            .GetProperty("version")
            .GetString();

        Assert.Equal(ExpectedGroundworkVersion, toolVersion);
    }

    [Fact]
    public void Scalar_entity_shape_compiles_with_typed_index_route_and_bounded_delete()
    {
        var physical = PhysicalTableDefinition.PhysicalEntityTable(
            "openiddict_tokens",
            [
                new ProjectedColumnDefinition(
                    "status",
                    "status",
                    PortablePhysicalType.String,
                    Length: 32,
                    IsNullable: false),
                new ProjectedColumnDefinition(
                    "expiration",
                    "expiration",
                    PortablePhysicalType.DateTime,
                    IsNullable: true)
            ],
            indexes:
            [
                new PhysicalIndexDefinition(
                    "openiddict-tokens-by-status-expiration",
                    [
                        new PhysicalIndexColumnDefinition("storage_scope", 0),
                        new PhysicalIndexColumnDefinition("status", 1),
                        new PhysicalIndexColumnDefinition("expiration", 2)
                    ],
                    missingValueBehavior: MissingValueBehavior.Excluded)
            ]);
        var index = new LogicalIndexDeclaration(
            "openiddict-tokens-by-status-expiration",
            [
                new IndexField("status", IndexValueKind.Keyword),
                new IndexField("expiration", IndexValueKind.DateTime)
            ],
            IndexValueKind.Keyword,
            isUnique: false,
            MissingValueBehavior.Excluded);
        var query = new BoundedQueryDeclaration(
            "prunable-openiddict-tokens",
            index.Identity,
            new HashSet<PortableQueryOperation>
            {
                PortableQueryOperation.Equal,
                PortableQueryOperation.LessThanOrEqual
            },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            supportsTotalCount: true,
            sortFields:
            [
                new BoundedQuerySortField("expiration", PhysicalSortDirection.Ascending)
            ],
            predicateFields:
            [
                new BoundedQueryPredicateField(
                    "status",
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal }),
                new BoundedQueryPredicateField(
                    "expiration",
                    new HashSet<PortableQueryOperation> { PortableQueryOperation.LessThanOrEqual })
            ]);
        var mutation = new BoundedMutationDeclaration(
            "prune-openiddict-tokens",
            query.Identity,
            BoundedMutationAction.Delete());

        Assert.Equal(PhysicalStorageForm.PhysicalEntityTable, physical.Form);
        Assert.Equal("openiddict-tokens-by-status-expiration", index.Identity);
        Assert.Equal("prunable-openiddict-tokens", query.Identity);
        Assert.Equal("prune-openiddict-tokens", mutation.Identity);
    }

    [Fact]
    public void Physical_entity_table_cannot_express_the_required_linked_multivalue_shape()
    {
        var factories = typeof(PhysicalTableDefinition)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.ReturnType == typeof(PhysicalTableDefinition))
            .ToArray();
        var entityParameters = factories
            .Where(method => method.Name == nameof(PhysicalTableDefinition.PhysicalEntityTable))
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name)
            .Where(name => name is not null)
            .ToArray();
        var linkedDocumentParameters = factories
            .Where(method =>
                method.Name == nameof(PhysicalTableDefinition.SharedDocuments) ||
                method.Name == nameof(PhysicalTableDefinition.DedicatedDocumentTable))
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.Name)
            .Where(name => name is not null)
            .ToArray();

        Assert.DoesNotContain(entityParameters, name =>
            name!.Contains("linked", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(linkedDocumentParameters, name =>
            name!.Contains("linked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Codec_CAS_unit_of_work_mutation_evidence_and_readiness_are_public()
    {
        var documentStoreMethods = new[] { typeof(IDocumentStore) }
            .Concat(typeof(IDocumentStore).GetInterfaces())
            .SelectMany(type => type.GetMethods())
            .ToArray();

        Assert.NotNull(typeof(VersionedJsonDocumentCodec));
        Assert.Contains(documentStoreMethods, method => method.Name == "SaveAsync");
        Assert.Contains(documentStoreMethods, method => method.Name == "BeginAsync");
        Assert.Contains(typeof(IDocumentUnitOfWork).GetMethods(), method => method.Name == "CommitAsync");
        Assert.Contains(typeof(IDocumentUnitOfWork).GetMethods(), method => method.Name == "RollbackAsync");
        Assert.NotNull(typeof(PhysicalMutationDocumentStore));
        Assert.NotNull(typeof(PhysicalDocumentMutationExplanation));
        Assert.NotNull(typeof(GroundworkPhysicalSchemaManifestSource)
            .GetMethod(nameof(GroundworkPhysicalSchemaManifestSource.InspectRuntimeAdmissionAsync)));
    }

    [Theory]
    [MemberData(nameof(MandatoryProviderReports))]
    public void Every_required_provider_package_statically_declares_atomic_CAS_and_query_capabilities(
        string providerKey,
        ProviderCapabilityReport report)
    {
        Assert.Equal(providerKey, NormalizeProviderName(report.Provider.Name));
        Assert.Contains(WellKnownCapabilities.AtomicCommit, report.SupportedCapabilities);
        Assert.Contains(WellKnownCapabilities.AtomicCommit, report.EvidencedCapabilities);
        Assert.Contains(ConcurrencyKind.Optimistic, report.SupportedConcurrencyModes);
        Assert.Contains(PortableQueryOperation.Equal, report.SupportedQueryOperations);
        Assert.Contains(PortableQueryOperation.LessThanOrEqual, report.SupportedQueryOperations);
    }

    public static IEnumerable<object[]> MandatoryProviderReports()
    {
        yield return ["mongodb", MongoDbGroundworkCapabilities.RuntimeForTransactionCapableDeployment()];
        yield return ["postgresql", PostgreSqlGroundworkCapabilities.Runtime()];
        yield return ["sqlite", SqliteGroundworkCapabilities.Runtime()];
        yield return ["sqlserver", SqlServerGroundworkCapabilities.Runtime()];
    }

    private static string NormalizeProviderName(string name) =>
        name.Replace("groundwork-", string.Empty, StringComparison.Ordinal);

    private static string RepoRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                directory = directory.Parent;

            return directory?.FullName
                   ?? throw new InvalidOperationException("Could not locate the Elsa Foundation repository root.");
        }
    }
}
