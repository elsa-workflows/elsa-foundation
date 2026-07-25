using System.Text.Json;
using System.Xml.Linq;
using Elsa.Foundation.Identity.OpenIddict.Groundwork;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Composition;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Executable Spec 106 admission probes. These use the selected public
/// manifest source and a real provider driver. They intentionally do not
/// advertise the unimplemented OpenIddict store surface or four-provider
/// conformance: those remain T006 and later tasks.
/// </summary>
public sealed class OpenIddictGroundworkCapabilityProbeTests
{
    [Fact]
    public void Repository_declares_one_exact_Groundwork_package_family()
    {
        var packageDocument = XDocument.Load(Path.Combine(RepoRoot, "Directory.Packages.props"));
        var packageVersions = packageDocument.Descendants("PackageVersion")
            .Select(element => ((string?)element.Attribute("Include"), (string?)element.Attribute("Version")))
            .Where(item => item.Item1?.StartsWith("Groundwork.", StringComparison.Ordinal) == true)
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
            packageVersions.Select(item => item.Item1).Order(StringComparer.Ordinal));
        Assert.Single(packageVersions.Select(item => item.Item2).Distinct(StringComparer.Ordinal));
    }

    [Fact]
    public void Repository_tool_matches_the_declared_Groundwork_package_family()
    {
        var packageDocument = XDocument.Load(Path.Combine(RepoRoot, "Directory.Packages.props"));
        var packageVersion = packageDocument.Descendants("PackageVersion")
            .Single(element => string.Equals((string?)element.Attribute("Include"), "Groundwork.Core", StringComparison.Ordinal))
            .Attribute("Version")?.Value;
        using var toolDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepoRoot, ".config", "dotnet-tools.json")));
        var toolVersion = toolDocument.RootElement
            .GetProperty("tools")
            .GetProperty("groundwork.tool")
            .GetProperty("version")
            .GetString();

        Assert.Equal(packageVersion, toolVersion);
    }

    [Fact]
    public async Task Public_deployment_source_declares_four_global_physical_entity_tables()
    {
        var declaration = await new OpenIddictGroundworkStorageManifestSource().CreateDeclarationAsync();

        Assert.Equal(
            new[]
            {
                OpenIddictGroundworkJson.ApplicationDocumentKind,
                OpenIddictGroundworkJson.AuthorizationDocumentKind,
                OpenIddictGroundworkJson.ScopeDocumentKind,
                OpenIddictGroundworkJson.TokenDocumentKind
            },
            declaration.Manifest.StorageUnits.Select(unit => unit.Identity.Value).Order(StringComparer.Ordinal));
        Assert.All(declaration.Manifest.StorageUnits, unit =>
        {
            Assert.Equal(TenancyKind.Global, unit.Tenancy.Kind);
            Assert.NotNull(unit.PhysicalStorage);
            var policy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(unit.PhysicalStorage!.Policy);
            Assert.Equal(PhysicalStorageForm.PhysicalEntityTable, policy.Definition.Form);
        });
        Assert.Contains(
            declaration.RequiredRoutes,
            route => route.RouteIdentity == OpenIddictGroundworkStorageManifest.FindTokenByReferenceIdQuery);
    }

    [Fact]
    public void Versioned_openiddict_codec_admits_only_its_declared_four_document_kinds()
    {
        var codec = OpenIddictGroundworkJson.CreateCodec();
        var content = codec.Serialize(
            OpenIddictGroundworkJson.TokenDocumentKind,
            new TokenContent("subject-a", "valid"));
        var envelope = new DocumentEnvelope(
            OpenIddictGroundworkJson.TokenDocumentKind,
            "token-a",
            content.SchemaVersion,
            1,
            content.ContentJson,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);

        Assert.Equal("v1", envelope.SchemaVersion);
        Assert.Equal(new TokenContent("subject-a", "valid"), codec.Deserialize<TokenContent>(envelope));
        Assert.Throws<global::Groundwork.Documents.Serialization.DocumentSchemaVersionException>(() =>
            codec.Deserialize<TokenContent>(envelope with { SchemaVersion = "v2" }));
    }

    [Fact]
    public async Task Sqlite_driver_applies_the_actual_openiddict_manifest_and_preserves_global_documents_across_reopen()
    {
        await using var driver = GroundworkProviderDriverFactory.Create("sqlite");
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new OpenIddictGroundworkStorageManifestSource()]);

        const string id = "token-a";
        const string content = """{"referenceId":"refresh-a","subject":"subject-a","status":"valid","expiration":"2030-01-01T00:00:00+00:00"}""";
        await using (var writer = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global))
        {
            var saved = await writer.DocumentStore.SaveAsync(new SaveDocumentRequest(
                OpenIddictGroundworkJson.TokenDocumentKind,
                id,
                "v1",
                content,
                0));

            Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
            Assert.NotNull(saved.Document);
        }

        await using (var reader = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global))
        {
            var loaded = await reader.DocumentStore.LoadAsync(OpenIddictGroundworkJson.TokenDocumentKind, id);

            Assert.NotNull(loaded);
            Assert.Equal(content, loaded.ContentJson);
            Assert.Equal(1, loaded.Version);
        }
    }

    [Fact]
    public async Task Sqlite_driver_executes_the_declared_token_reference_route_without_a_client_queryable()
    {
        await using var driver = GroundworkProviderDriverFactory.Create("sqlite");
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new OpenIddictGroundworkStorageManifestSource()]);
        await using var client = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global);
        await client.DocumentStore.SaveAsync(new SaveDocumentRequest(
            OpenIddictGroundworkJson.TokenDocumentKind,
            "token-a",
            "v1",
            """{"referenceId":"refresh-a","subject":"subject-a","status":"valid","expiration":"2030-01-01T00:00:00+00:00"}""",
            0));

        var query = new DocumentQuery(
            OpenIddictGroundworkJson.TokenDocumentKind,
            OpenIddictGroundworkStorageManifest.FindTokenByReferenceIdQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.Equal("referenceId", "refresh-a"))],
            [],
            skip: null,
            take: 1,
            null,
            null,
            BoundedQueryResultOperation.Documents);
        var result = await client.BoundedDocumentStore!.QueryAsync(query);
        var explainer = Assert.IsAssignableFrom<IPhysicalDocumentQueryExplainer>(client.BoundedDocumentStore);
        var explanation = await explainer.ExplainAsync(query);

        Assert.Equal("token-a", Assert.Single(result.Documents).Id);
        Assert.Equal(OpenIddictGroundworkStorageManifest.FindTokenByReferenceIdQuery, explanation.Plan.QueryIdentity);
        Assert.Equal(PhysicalQueryAccessKind.PrimaryProjectedColumns, explanation.Plan.AccessKind);
    }

    private sealed record TokenContent(string Subject, string Status);

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
