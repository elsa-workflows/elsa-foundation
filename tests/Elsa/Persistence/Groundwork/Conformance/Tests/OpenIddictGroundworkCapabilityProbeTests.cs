using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using Elsa.Foundation.Identity.OpenIddict.Groundwork;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Composition;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Groundwork.MongoDb.Documents;
using Groundwork.PostgreSql.Documents;
using Groundwork.SqlServer.Documents;
using Groundwork.Sqlite;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
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
        var application = declaration.Manifest.StorageUnits.Single(unit =>
            unit.Identity.Value == OpenIddictGroundworkJson.ApplicationDocumentKind);
        var applicationPolicy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(
            application.PhysicalStorage!.Policy);
        Assert.All(
            applicationPolicy.Definition.ProjectedColumns.Where(column =>
                column.Path is "redirectUris" or "postLogoutRedirectUris"),
            column => Assert.Equal(OpenIddictGroundworkStorageManifest.MaxIndexedUriLength, column.Length));
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

    [Fact]
    public async Task Sqlite_driver_executes_exact_multivalue_membership_and_contains_all_from_element_storage()
    {
        await using var driver = GroundworkProviderDriverFactory.Create("sqlite");
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new OpenIddictGroundworkStorageManifestSource()]);
        await using var client = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global);
        await client.DocumentStore.SaveAsync(new SaveDocumentRequest(
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            "application-one",
            "v1",
            """
            {
              "clientId":"client-one",
              "redirectUris":["https://one.example/callback","https://shared.example/callback"],
              "postLogoutRedirectUris":["https://one.example/logout"]
            }
            """,
            0));
        await client.DocumentStore.SaveAsync(new SaveDocumentRequest(
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            "application-two",
            "v1",
            """
            {
              "clientId":"client-two",
              "redirectUris":["https://shared.example/callback"],
              "postLogoutRedirectUris":["https://two.example/logout"]
            }
            """,
            0));

        var contains = new DocumentQuery(
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            OpenIddictGroundworkStorageManifest.FindApplicationByRedirectUriQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.CollectionContains(
                "redirectUris",
                "https://shared.example/callback"))]);
        var containsAll = new DocumentQuery(
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            OpenIddictGroundworkStorageManifest.FindApplicationByRedirectUriQuery,
            [DocumentQueryClause.Of(DocumentQueryComparison.CollectionContainsAll(
                "redirectUris",
                ["https://shared.example/callback", "https://one.example/callback"]))]);
        var bounded = Assert.IsAssignableFrom<IPhysicalDocumentQueryExplainer>(client.BoundedDocumentStore);
        var membership = await client.BoundedDocumentStore!.QueryAsync(contains);
        var intersection = await client.BoundedDocumentStore.QueryAsync(containsAll);
        var explanation = await bounded.ExplainAsync(containsAll);

        Assert.Equal(["application-one", "application-two"], membership.Documents.Select(document => document.Id).Order());
        Assert.Equal("application-one", Assert.Single(intersection.Documents).Id);
        Assert.Equal(PhysicalQueryAccessKind.CollectionElementsThenPrimary, explanation.Plan.AccessKind);
        Assert.NotEmpty(explanation.Commands);
    }

    [Fact]
    public async Task Sqlite_driver_enforces_the_certified_uri_projection_boundary_before_mutation()
    {
        await using var driver = GroundworkProviderDriverFactory.Create("sqlite");
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new OpenIddictGroundworkStorageManifestSource()]);
        await using var client = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global);
        var admittedUri = new string('u', OpenIddictGroundworkStorageManifest.MaxIndexedUriLength);
        var rejectedUri = $"{admittedUri}u";

        var saved = await client.DocumentStore.SaveAsync(new SaveDocumentRequest(
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            "application-uri-boundary",
            "v1",
            JsonSerializer.Serialize(new
            {
                clientId = "client-uri-boundary",
                redirectUris = new[] { admittedUri },
                postLogoutRedirectUris = Array.Empty<string>()
            }),
            0));
        var exception = await Assert.ThrowsAsync<PhysicalProjectionValueValidationException>(() =>
            client.DocumentStore.SaveAsync(new SaveDocumentRequest(
                OpenIddictGroundworkJson.ApplicationDocumentKind,
                "application-uri-overflow",
                "v1",
                JsonSerializer.Serialize(new
                {
                    clientId = "client-uri-overflow",
                    redirectUris = new[] { rejectedUri },
                    postLogoutRedirectUris = Array.Empty<string>()
                }),
                0)));

        Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
        Assert.Equal("GW-PHYSICAL-037", exception.Diagnostic.Code);
        Assert.Contains("redirectUri", exception.Diagnostic.Target, StringComparison.Ordinal);
        Assert.Null(await client.DocumentStore.LoadAsync(
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            "application-uri-overflow"));
    }

    [Fact]
    public async Task Sqlite_driver_enforces_expected_version_CAS_for_the_actual_token_entity()
    {
        await using var driver = GroundworkProviderDriverFactory.Create("sqlite");
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new OpenIddictGroundworkStorageManifestSource()]);

        await using (var first = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global))
        {
            var created = await first.DocumentStore.SaveAsync(Token("token-cas", "refresh-cas", "subject-a", 0));

            Assert.Equal(DocumentStoreWriteStatus.Saved, created.Status);
            Assert.Equal(1, created.Document!.Version);
        }

        await using (var winner = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global))
        {
            var updated = await winner.DocumentStore.SaveAsync(Token("token-cas", "refresh-cas", "subject-b", 1));

            Assert.Equal(DocumentStoreWriteStatus.Saved, updated.Status);
            Assert.Equal(2, updated.Document!.Version);
        }

        await using (var staleWriter = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global))
        {
            var stale = await staleWriter.DocumentStore.SaveAsync(Token("token-cas", "refresh-cas", "subject-c", 1));

            Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, stale.Status);
            Assert.Null(stale.Document);
        }
    }

    [Fact]
    public async Task Sqlite_driver_commits_the_actual_application_and_scope_entities_in_one_unit_of_work()
    {
        await using var driver = GroundworkProviderDriverFactory.Create("sqlite");
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new OpenIddictGroundworkStorageManifestSource()]);

        await using (var writer = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global))
        {
            await using var unitOfWork = await writer.DocumentStore.BeginAsync(DocumentCommitScope.Of(
                OpenIddictGroundworkJson.ApplicationDocumentKind,
                OpenIddictGroundworkJson.ScopeDocumentKind));
            var application = await unitOfWork.SaveAsync(new SaveDocumentRequest(
                OpenIddictGroundworkJson.ApplicationDocumentKind,
                "application-uow",
                "v1",
                """{"clientId":"client-uow","redirectUris":[],"postLogoutRedirectUris":[]}""",
                0));
            var scope = await unitOfWork.SaveAsync(new SaveDocumentRequest(
                OpenIddictGroundworkJson.ScopeDocumentKind,
                "scope-uow",
                "v1",
                """{"name":"scope-uow","resources":[]}""",
                0));

            Assert.Equal(DocumentStoreWriteStatus.Saved, application.Status);
            Assert.Equal(DocumentStoreWriteStatus.Saved, scope.Status);
            await unitOfWork.CommitAsync();
        }

        await using var reader = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global);
        Assert.NotNull(await reader.DocumentStore.LoadAsync(
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            "application-uow"));
        Assert.NotNull(await reader.DocumentStore.LoadAsync(
            OpenIddictGroundworkJson.ScopeDocumentKind,
            "scope-uow"));
    }

    [Fact]
    public async Task Sqlite_executes_the_declared_token_prune_mutation_with_an_exact_count_and_cancellation()
    {
        var database = TemporaryDatabasePath();
        try
        {
            var source = await CreatePhysicalSourceAsync();
            await ApplySqliteSchemaAsync(source, database);
            var store = new SqlitePhysicalDocumentStore(
                $"Data Source={database}",
                source.CreateManifest(),
                source.PhysicalTarget.Routes,
                DocumentStoreAccess.Global);
            var route = source.PhysicalTarget.Routes.Single(candidate =>
                candidate.StorageUnit.Value == OpenIddictGroundworkJson.TokenDocumentKind);
            var mutations = SqlitePhysicalMutationRuntime.Create(
                store,
                source.CreateManifest(),
                route,
                source.PhysicalTarget.Provider);
            const string expiration = "2030-01-01T00:00:00+00:00";

            Assert.Equal(DocumentStoreWriteStatus.Saved, (await store.SaveAsync(
                Token("token-prune-1", "refresh-prune-1", "subject-prune", 0, expiration))).Status);
            Assert.Equal(DocumentStoreWriteStatus.Saved, (await store.SaveAsync(
                Token("token-prune-2", "refresh-prune-2", "subject-prune", 0, expiration))).Status);
            Assert.Equal(DocumentStoreWriteStatus.Saved, (await store.SaveAsync(
                Token("token-preserve", "refresh-preserve", "subject-preserve", 0, expiration))).Status);

            var mutation = PruneTokens("prune-openiddict-tokens-1", expiration);
            var explanation = await mutations.ExplainAsync(mutation);
            var completed = await mutations.ExecuteAsync(mutation);
            var replayed = await mutations.ExecuteAsync(mutation);

            Assert.Equal(OpenIddictGroundworkStorageManifest.PruneTokensMutation, explanation.Plan.MutationIdentity);
            Assert.NotEmpty(explanation.Commands);
            Assert.All(explanation.Commands, command => Assert.NotEmpty(command.Selectors));
            Assert.Equal(BoundedMutationStatus.Completed, completed.Status);
            Assert.Equal(2, completed.AffectedCount);
            Assert.Equal(BoundedMutationStatus.Replayed, replayed.Status);
            Assert.Equal(2, replayed.AffectedCount);
            Assert.Null(await store.LoadAsync(OpenIddictGroundworkJson.TokenDocumentKind, "token-prune-1"));
            Assert.Null(await store.LoadAsync(OpenIddictGroundworkJson.TokenDocumentKind, "token-prune-2"));
            Assert.NotNull(await store.LoadAsync(OpenIddictGroundworkJson.TokenDocumentKind, "token-preserve"));

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutations.ExecuteAsync(
                PruneTokens("prune-openiddict-tokens-cancelled", expiration),
                cancellation.Token));
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Fact]
    public async Task Public_schema_source_has_deterministic_host_naming_and_fingerprint_transformation()
    {
        var manifest = (await new OpenIddictGroundworkStorageManifestSource()
                .CreateDeclarationAsync())
            .Manifest;
        var identity = PhysicalStorageResolver.Resolve(
            manifest,
            PhysicalNamePolicy.Identity,
            SqliteGroundworkCapabilities.PhysicalNames);
        var prefixed = PhysicalStorageResolver.Resolve(
            manifest,
            new DelegatePhysicalNamePolicy(context => $"probe86_{context.FeatureDefaultLogicalName}"),
            SqliteGroundworkCapabilities.PhysicalNames);

        Assert.True(identity.IsValid, string.Join(Environment.NewLine, identity.Diagnostics.Select(item => item.Message)));
        Assert.True(prefixed.IsValid, string.Join(Environment.NewLine, prefixed.Diagnostics.Select(item => item.Message)));
        var originalToken = identity.Definitions.Single(definition =>
            definition.Resolved.StorageUnit.Value == OpenIddictGroundworkJson.TokenDocumentKind);
        var prefixedToken = prefixed.Definitions.Single(definition =>
            definition.Resolved.StorageUnit.Value == OpenIddictGroundworkJson.TokenDocumentKind);

        Assert.Equal($"probe86_{originalToken.PrimaryName.LogicalName}", prefixedToken.PrimaryName.LogicalName);
        Assert.NotEqual(originalToken.Fingerprint, prefixedToken.Fingerprint);
    }

    [Fact]
    public async Task Pinned_schema_cli_and_runtime_readiness_use_the_actual_openiddict_deployment_source()
    {
        var database = TemporaryDatabasePath();
        try
        {
            var runtimeSource = await CreatePhysicalSourceAsync();
            await using var inspection = new SqliteConnection($"Data Source={database}");
            var pending = await runtimeSource.InspectRuntimeAdmissionAsync(
                new SqlitePhysicalSchemaExecutor(inspection));

            Assert.False(pending.IsReady);
            Assert.NotEmpty(pending.PendingOperations);

            var plan = await RunToolAsync("plan", database);
            Assert.Equal(2, plan.ExitCode);
            Assert.Equal("pending", plan.Report.GetProperty("outcome").GetString());
            Assert.Equal(runtimeSource.TargetFingerprint, plan.Report.GetProperty("target").GetProperty("fingerprint").GetString());
            Assert.False(plan.Report.GetProperty("targetMutated").GetBoolean());

            var applied = await RunToolAsync("apply", database, "--safe");
            Assert.Equal(0, applied.ExitCode);
            Assert.Equal("applied", applied.Report.GetProperty("outcome").GetString());
            Assert.True(applied.Report.GetProperty("targetMutated").GetBoolean());

            await using var admittedInspection = new SqliteConnection($"Data Source={database}");
            var admitted = await runtimeSource.InspectRuntimeAdmissionAsync(
                new SqlitePhysicalSchemaExecutor(admittedInspection));
            var status = await RunToolAsync("status", database);

            Assert.True(admitted.IsReady);
            Assert.Empty(admitted.PendingOperations);
            Assert.Equal(0, status.ExitCode);
            Assert.Equal("ready", status.Report.GetProperty("outcome").GetString());
            Assert.Equal(runtimeSource.TargetFingerprint, status.Report.GetProperty("target").GetProperty("fingerprint").GetString());
        }
        finally
        {
            File.Delete(database);
        }
    }

    [Theory]
    [InlineData("sqlite")]
    [InlineData("sqlserver")]
    [InlineData("postgresql")]
    [InlineData("mongodb")]
    public async Task Four_provider_public_capabilities_execute_the_same_openiddict_contract(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        var sources = new IGroundworkStorageManifestSource[] { new OpenIddictGroundworkStorageManifestSource() };
        var physicalSource = await driver.PrepareSchemaParityAsync(sources);
        await driver.ResetPhysicalAsync(sources);
        var status = await driver.RunSchemaToolAsync(
            "status",
            typeof(OpenIddictGroundworkDeploymentSchema));

        Assert.Equal(0, status.ExitCode);
        Assert.Equal("ready", status.Report.GetProperty("outcome").GetString());
        Assert.Equal(providerKey, driver.Descriptor.ProviderKey);
        Assert.Equal("0.0.1-preview.88", driver.Descriptor.ProviderVersion);
        driver.Descriptor.Topology.EnsureSupports(
            GroundworkTopologyCapabilities.PersistentStorage |
            GroundworkTopologyCapabilities.IndependentClients |
            GroundworkTopologyCapabilities.MultiDocumentTransactions |
            GroundworkTopologyCapabilities.ExternalProcessRestart);

        await using (var client = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global))
        {
            await client.DocumentStore.SaveAsync(new SaveDocumentRequest(
                OpenIddictGroundworkJson.ApplicationDocumentKind,
                "application-four-provider",
                "v1",
                """
                {
                  "clientId":"client-four-provider",
                  "redirectUris":["https://one.example/callback","https://shared.example/callback"],
                  "postLogoutRedirectUris":["https://one.example/logout"]
                }
                """,
                0));
            await client.DocumentStore.SaveAsync(new SaveDocumentRequest(
                OpenIddictGroundworkJson.ApplicationDocumentKind,
                "application-four-provider-two",
                "v1",
                """
                {
                  "clientId":"client-four-provider-two",
                  "redirectUris":["https://shared.example/callback"],
                  "postLogoutRedirectUris":["https://two.example/logout"]
                }
                """,
                0));
            var membershipQuery = new DocumentQuery(
                OpenIddictGroundworkJson.ApplicationDocumentKind,
                OpenIddictGroundworkStorageManifest.FindApplicationByRedirectUriQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.CollectionContainsAll(
                    "redirectUris",
                    ["https://shared.example/callback", "https://one.example/callback"]))]);
            var membership = await client.BoundedDocumentStore!.QueryAsync(membershipQuery);
            var membershipPlan = await client.PhysicalDocumentQueryExplainer!.ExplainAsync(membershipQuery);

            Assert.Equal("application-four-provider", Assert.Single(membership.Documents).Id);
            Assert.Equal(PhysicalQueryAccessKind.CollectionElementsThenPrimary, membershipPlan.Plan.AccessKind);
            Assert.NotEmpty(membershipPlan.Commands);

            await using var unitOfWork = await client.DocumentStore.BeginAsync(DocumentCommitScope.Of(
                OpenIddictGroundworkJson.ApplicationDocumentKind,
                OpenIddictGroundworkJson.ScopeDocumentKind));
            Assert.Equal(DocumentStoreWriteStatus.Saved, (await unitOfWork.SaveAsync(new SaveDocumentRequest(
                OpenIddictGroundworkJson.ApplicationDocumentKind,
                "application-four-provider-uow",
                "v1",
                """{"clientId":"client-four-provider-uow","redirectUris":[],"postLogoutRedirectUris":[]}""",
                0))).Status);
            Assert.Equal(DocumentStoreWriteStatus.Saved, (await unitOfWork.SaveAsync(new SaveDocumentRequest(
                OpenIddictGroundworkJson.ScopeDocumentKind,
                "scope-four-provider-uow",
                "v1",
                """{"name":"scope-four-provider-uow","resources":[]}""",
                0))).Status);
            await unitOfWork.CommitAsync();

            Assert.Equal(DocumentStoreWriteStatus.Saved, (await client.DocumentStore.SaveAsync(
                Token("token-four-provider", "refresh-four-provider", "subject-before", 0))).Status);
            Assert.Equal(DocumentStoreWriteStatus.Saved, (await client.DocumentStore.SaveAsync(
                Token("token-four-provider", "refresh-four-provider", "subject-winner", 1))).Status);
        }

        await using (var stale = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global))
        {
            var conflict = await stale.DocumentStore.SaveAsync(
                Token("token-four-provider", "refresh-four-provider", "subject-stale", 1));
            Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, conflict.Status);
            Assert.Null(conflict.Document);
        }

        var tokenRoute = physicalSource.PhysicalTarget.Routes.Single(route =>
            route.StorageUnit.Value == OpenIddictGroundworkJson.TokenDocumentKind);
        const string expiration = "2030-01-01T00:00:00+00:00";
        if (string.Equals(providerKey, "postgresql", StringComparison.Ordinal))
        {
            // PostgreSQL's strict native-mutation inspector requires the actual EXPLAIN
            // to select the declared index. The driver reuses the real native-route
            // fixture: one matching token plus 4,096 non-matching contrast rows, then ANALYZE.
            await driver.PrepareNativeRoutePlanDatasetAsync([
                PruneTokenMutationPlanDataset(tokenRoute, expiration)
            ]);
        }
        else
        {
            await using var writer = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global);
            Assert.Equal(DocumentStoreWriteStatus.Saved, (await writer.DocumentStore.SaveAsync(
                Token("token-four-provider-prune", "refresh-four-provider-prune", "subject-prune", 0, expiration))).Status);
        }

        await using (var client = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global))
        {
            var mutations = CreateMutationExplainer(
                client.DocumentStore,
                physicalSource.CreateManifest(),
                tokenRoute,
                physicalSource.PhysicalTarget.Provider);
            var mutation = PruneTokens($"prune-openiddict-{providerKey}", expiration);
            var mutationPlan = await mutations.ExplainAsync(mutation);
            var mutationResult = await mutations.ExecuteAsync(mutation);

            Assert.NotEmpty(mutationPlan.Commands);
            Assert.All(mutationPlan.Commands, command => Assert.NotEmpty(command.Selectors));
            Assert.Equal(BoundedMutationStatus.Completed, mutationResult.Status);
            Assert.Equal(1, mutationResult.AffectedCount);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => mutations.ExecuteAsync(
                PruneTokens($"prune-openiddict-{providerKey}-cancelled", expiration),
                cancellation.Token));
        }

        await using var restarted = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global);
        Assert.NotNull(await restarted.DocumentStore.LoadAsync(
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            "application-four-provider-uow"));
        Assert.NotNull(await restarted.DocumentStore.LoadAsync(
            OpenIddictGroundworkJson.ScopeDocumentKind,
            "scope-four-provider-uow"));
    }

    private sealed record TokenContent(string Subject, string Status);

    private static SaveDocumentRequest Token(
        string id,
        string referenceId,
        string subject,
        long expectedVersion,
        string expiration = "2030-01-01T00:00:00+00:00") => new(
        OpenIddictGroundworkJson.TokenDocumentKind,
        id,
        "v1",
        $$"""{"referenceId":"{{referenceId}}","subject":"{{subject}}","status":"valid","expiration":"{{expiration}}"}""",
        expectedVersion);

    private static DocumentMutation PruneTokens(string operationId, string expiration) => new(
        OpenIddictGroundworkJson.TokenDocumentKind,
        OpenIddictGroundworkStorageManifest.PruneTokensMutation,
        operationId,
        [
            DocumentQueryClause.Of(DocumentQueryComparison.Equal("subject", "subject-prune")),
            DocumentQueryClause.Of(DocumentQueryComparison.Equal("status", "valid")),
            DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual("expiration", expiration))
        ]);

    private static GroundworkNativeRoutePlanRequest PruneTokenMutationPlanDataset(
        ExecutableStorageRoute tokenRoute,
        string expiration)
    {
        var expirationValue = DateTimeOffset.Parse(expiration, System.Globalization.CultureInfo.InvariantCulture);
        return new GroundworkNativeRoutePlanRequest(
            OpenIddictGroundworkJson.TokenDocumentKind,
            OpenIddictGroundworkStorageManifest.FindTokenBySubjectQuery,
            tokenRoute.PrimaryStorage.Name.Identifier,
            routeField: "subject",
            projectedFields: tokenRoute.ProjectedColumns
                .Select(column => column.Definition.LogicalName)
                .ToArray(),
            storageScope: DocumentScopeSelection.Global.StorageKey!,
            routeValue: "subject-prune",
            limit: 1,
            acceptanceCardinality: 4097,
            candidateDocumentId: "token-four-provider-prune",
            candidateContentJson: "{}",
            candidateSchemaVersion: "v1",
            projectedValues:
            [
                GroundworkNativeRouteProjectedValue.String("referenceId", "refresh-four-provider-prune"),
                GroundworkNativeRouteProjectedValue.String("subject", "subject-prune", "subject-contrast"),
                GroundworkNativeRouteProjectedValue.String("status", "valid", "revoked"),
                GroundworkNativeRouteProjectedValue.DateTime("expiration", expirationValue, expirationValue.AddDays(1))
            ],
            requiredPredicateFields: ["subject", "status", "expiration"],
            expectedCommandKinds: [PhysicalDocumentQueryCommandKind.First],
            indexFields: ["subject", "status", "expiration"],
            evidenceKind: "openiddict-native-mutation-plan",
            contentColumn: tokenRoute.Envelope.CanonicalJson.Identifier,
            expectedIndexName: tokenRoute.Indexes.Single(index =>
                index.Columns.Select(column => column.Column.LogicalName)
                    .SequenceEqual(["subject", "status", "expiration"], StringComparer.Ordinal)).Name.Identifier,
            matchingCardinality: 1,
            varyingProjectedFields: ["referenceId"]);
    }

    private static IPhysicalDocumentMutationExplainer CreateMutationExplainer(
        IDocumentStore store,
        StorageManifest manifest,
        ExecutableStorageRoute route,
        ProviderIdentity provider) =>
        store switch
        {
            SqlitePhysicalDocumentStore sqlite =>
                SqlitePhysicalMutationRuntime.Create(sqlite, manifest, route, provider),
            SqlServerPhysicalDocumentStore sqlServer =>
                SqlServerPhysicalMutationRuntime.Create(sqlServer, manifest, route, provider),
            PostgreSqlPhysicalDocumentStore postgreSql =>
                PostgreSqlPhysicalMutationRuntime.Create(postgreSql, manifest, route, provider),
            MongoDbPhysicalDocumentStore mongoDb =>
                MongoDbPhysicalMutationRuntime.Create(mongoDb, manifest, route, provider),
            _ => throw new NotSupportedException(
                $"The OpenIddict capability probe does not recognize physical store '{store.GetType().FullName}'.")
        };

    private static async ValueTask<GroundworkPhysicalSchemaManifestSource> CreatePhysicalSourceAsync() =>
        await GroundworkStoreInitialization.CreatePhysicalSchemaSourceAsync(
            SqliteGroundworkCapabilities.Runtime(),
            new GroundworkProviderTopologySnapshot(
                SqliteGroundworkCapabilities.Provider.Name,
                "sqlite-file",
                new HashSet<string>(StringComparer.Ordinal)
                {
                    RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                }),
            SqliteGroundworkCapabilities.PhysicalNames,
            [new OpenIddictGroundworkStorageManifestSource()]);

    private static async Task ApplySqliteSchemaAsync(GroundworkPhysicalSchemaManifestSource source, string database)
    {
        await using var connection = new SqliteConnection($"Data Source={database}");
        var result = await PhysicalSchemaApplication.ApplyAsync(
            source.PhysicalTarget,
            new SqlitePhysicalSchemaExecutor(connection));

        Assert.True(result.Outcome is PhysicalSchemaApplicationOutcome.Applied or PhysicalSchemaApplicationOutcome.NoChanges);
    }

    private static async Task<ToolResult> RunToolAsync(string command, string database, params string[] extraArguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = RepoRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in new[]
                 {
                     "tool", "run", "groundwork", "--", command,
                     "--manifest-assembly", typeof(OpenIddictGroundworkDeploymentSchema).Assembly.Location,
                     "--manifest-type", typeof(OpenIddictGroundworkDeploymentSchema).FullName!,
                     "--provider", "sqlite",
                     "--connection-env", "ELSA_OPENIDDICT_GROUNDWORK_PROBE_CONNECTION",
                     "--output", "json"
                 })
            start.ArgumentList.Add(argument);
        start.Environment["ELSA_OPENIDDICT_GROUNDWORK_PROBE_CONNECTION"] = $"Data Source={database}";
        foreach (var argument in extraArguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Groundwork.Tool.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await outputTask;
        var error = await errorTask;
        using var document = JsonDocument.Parse(output);
        return new ToolResult(process.ExitCode, document.RootElement.Clone(), error);
    }

    private static string TemporaryDatabasePath() => Path.Combine(
        Path.GetTempPath(),
        $"elsa-openiddict-groundwork-probe-{Guid.NewGuid():N}.db");

    private sealed record ToolResult(int ExitCode, JsonElement Report, string Error);

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
