using System.IO.Compression;
using System.Security.Cryptography;
using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Diagnostics.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.MongoDb;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.ReferenceComposition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Groundwork.Core.Capabilities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

public sealed class HistoricalSchemaUpgradeTests
{
    private const string HistoricalElsaCommit = "ca818b649d85c5167e2222c0ec534e215153d473";
    private const string HistoricalProviderContractVersion = "1.0.0";
    private static readonly DateTimeOffset PlanningTime = DateTimeOffset.Parse("2026-07-31T00:00:00Z");
    private static readonly string[] ExpectedVersionedIndexes =
    [
        "activityDefinition/activity-definition-by-category-v2",
        "activityDefinition/activity-definition-by-description-v2",
        "activityDefinition/activity-definition-by-display-name-v2",
        "activityDefinition/activity-definition-by-id-v2",
        "activityDefinition/activity-definition-by-type-key-v2",
        "activityDefinitionAuthoringState/by-definition-v2",
        "activityDefinitionAuthoringState/by-head-version-v2",
        "activityDefinitionDraft/by-definition-v2",
        "activityDefinitionDraftLayout/by-draft-v2",
        "activityDefinitionManagementProjection/management-by-sort-v2",
        "activityDefinitionManagementProjection/management-by-valid-to-v2",
        "activityDefinitionVersion/activity-definition-versions-by-definition-v2",
        "activityDefinitionVersionLayout/by-definition-version-v2",
        "activityDefinitionVersionPublication/by-definition-v2",
        "activityDefinitionVersionPublication/by-definition-version-v2",
        "activityDependencyEdge/by-dependency-version-v2",
        "activityDependencyEdge/by-owner-version-v2",
        "activityDraftManagementProjection/management-by-sort-v2",
        "activityDraftManagementProjection/management-by-valid-to-v2",
        "activityDraftValidation/by-draft-v2",
        "activityForkCandidate/fork-candidate-by-retention-v2",
        "activityVersionManagementProjection/management-by-sort-v2",
        "activityVersionManagementProjection/management-by-valid-to-v2",
        "identityClaimMapping/identity-claim-mapping-by-provider-v2",
        "identityExternalLogin/identity-login-by-user-v2",
        "identityMutationReceipt/identity-mutation-receipt-by-expiry-v2",
        "identityRole/identity-role-by-normalized-name-v2",
        "identityRole/identity-role-by-tenant-v2",
        "identityRoleClaim/identity-role-claim-by-role-v2",
        "identityUser/identity-user-by-normalized-email-v2",
        "identityUser/identity-user-by-normalized-name-v2",
        "identityUserClaim/identity-user-claim-by-claim-v2",
        "identityUserClaim/identity-user-claim-by-user-v2",
        "identityUserRole/identity-user-role-by-role-v2",
        "identityUserRole/identity-user-role-by-user-v2",
        "openTelemetryMetricInstrument/by-kind-v2",
        "openTelemetryMetricInstrument/by-resource-v2",
        "openTelemetryMetricInstrument/by-retention-tie-breaker-v2",
        "openTelemetryMetricInstrument/by-retention-v2",
        "openTelemetryResource/by-resource-service-v2",
        "openTelemetryResource/by-retention-tie-breaker-v2",
        "openTelemetryResource/by-retention-v2",
        "secret/secret-filtered-list-v2",
        "workflowDefinition/definition-by-description-v2",
        "workflowDefinition/definition-by-id-list-v2",
        "workflowDefinition/definition-by-name-v2",
        "workflowDefinitionDraft/drafts-by-definition-v2",
        "workflowDefinitionVersion/versions-by-definition-v2",
        "workflowExecutionState/by-collection-and-pinned-artifact-v2"
    ];

    public static TheoryData<string, string> Providers => new()
    {
        { "sqlite", "afab9a4a6cc6842d1658ddbb46fa8a8cbd64bf3e8e8e4a9a166922b2c06790e2" },
        { "sql-server", "a7fe88ba0b6d467092fe484cd4be88a79787f6794d51ea9ed5dbae9c27223ee5" },
        { "postgresql", "e51b1c3480e2f78c3b70efcff7b6fdfa86acb4904f0453253de6884b5745a0c6" },
        { "mongodb", "61c380a25bbb33ceef1ebee062022fba3390b16f73268d72ed0c2b4a4ab4e1e6" }
    };

    [Theory]
    [MemberData(nameof(Providers))]
    public void Preview102_is_an_additive_upgrade_from_the_immutable_preview95_reference_state(
        string provider,
        string expectedCompressedDigest)
    {
        var fixture = FixturePath(provider);
        Assert.Equal(expectedCompressedDigest, Digest(File.ReadAllBytes(fixture)));
        var historical = PhysicalSchemaAppliedStateSerializer.Deserialize(ReadGzip(fixture));
        Assert.Equal(HistoricalProviderContractVersion, historical.Provider.Version);

        var manifest = new GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema().CreateManifest();
        var current = CurrentTarget(provider, manifest);
        Assert.Equal(historical.ManifestIdentity, current.ManifestIdentity);
        Assert.Equal(historical.Provider.Name, current.Provider.Name);

        var plan = PhysicalSchemaDiffPlanner.Plan(
            current,
            PhysicalSchemaHistoryState.FromApplied(historical),
            PlanningTime);

        Assert.True(
            plan.IsApplicable,
            $"{HistoricalElsaCommit}: {string.Join("; ", plan.Diagnostics.Select(x => $"{x.Code}: {x.Message}"))}");
        Assert.Equal(historical.TargetFingerprint, plan.ExpectedAppliedTargetFingerprint);
        Assert.DoesNotContain(plan.Diagnostics, diagnostic =>
            diagnostic.Code is "GW-SCHEMA-003" or "GW-SCHEMA-004" or "GW-SCHEMA-006");

        var historicalIndexIdentities = historical.AppliedOperations
            .Where(operation => operation.Kind == PhysicalSchemaOperationKind.CreatePhysicalIndex)
            .Select(operation => $"{operation.StorageUnit!.Value}/{operation.SubjectIdentity}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currentIndexIdentities = current.Routes
            .SelectMany(route => route.Indexes.Select(index => $"{route.StorageUnit.Value}/{index.Identity}"))
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(
            historicalIndexIdentities,
            identity => Assert.Contains(identity, currentIndexIdentities));

        PhysicalSchemaOperationKind[] additiveOperationKinds =
        [
            PhysicalSchemaOperationKind.CreatePrimaryStorage,
            PhysicalSchemaOperationKind.CreateLinkedStorage,
            PhysicalSchemaOperationKind.CreateCollectionElementStorage,
            PhysicalSchemaOperationKind.CreatePhysicalEntityStorage,
            PhysicalSchemaOperationKind.AddProjectedColumn,
            PhysicalSchemaOperationKind.FinalizeProjectedColumn,
            PhysicalSchemaOperationKind.CreatePhysicalIndex,
            PhysicalSchemaOperationKind.BackfillCanonicalJson,
            PhysicalSchemaOperationKind.ApplyProviderDefinition,
            PhysicalSchemaOperationKind.ValidatePhysicalSchema,
            PhysicalSchemaOperationKind.RecordAppliedState
        ];
        Assert.All(
            plan.Operations,
            operation => Assert.Contains(operation.Kind, additiveOperationKinds));

        var versionedIndexes = plan.Operations
            .OfType<CreatePhysicalIndexOperation>()
            .Where(operation => operation.SubjectIdentity.EndsWith("-v2", StringComparison.Ordinal))
            .Select(operation => $"{operation.StorageUnit!.Value}/{operation.SubjectIdentity}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(ExpectedVersionedIndexes, versionedIndexes);
    }

    /// <summary>
    /// Materializes the immutable preview.95 physical target, then admits the current target on the
    /// same provider database without a second reset. This proves physical schema evolution and runtime
    /// admission only; it deliberately does not claim old-runtime data survival or a data migration.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Preview102_physically_upgrades_the_preview95_schema_without_reset(
        string provider,
        string expectedCompressedDigest)
    {
        var fixture = FixturePath(provider);
        Assert.Equal(expectedCompressedDigest, Digest(File.ReadAllBytes(fixture)));
        var historicalAppliedState = PhysicalSchemaAppliedStateSerializer.Deserialize(ReadGzip(fixture));

        await using var driver = GroundworkProviderDriverFactory.Create(
            provider == "sql-server" ? "sqlserver" : provider);
        await driver.InitializeAsync();

        var current = await driver.PrepareSchemaParityAsync(CurrentDeploymentSources());
        var historical = CreateHistoricalSource(current, historicalAppliedState);

        Assert.NotEqual(historical.TargetFingerprint, current.TargetFingerprint);
        await driver.ResetPhysicalAsync(historical);

        var historicalAdmission = await driver.InspectSchemaParityAdmissionAsync(historical);
        Assert.True(
            historicalAdmission.IsReady,
            string.Join(Environment.NewLine, historicalAdmission.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(historical.TargetFingerprint, historicalAdmission.AppliedTargetFingerprint);
        Assert.Empty(historicalAdmission.PendingOperations);

        // Apply the current target to the existing historical database. Do not reset here: that is the
        // materialization proof this test owns.
        await driver.ApplyPhysicalSchemaAsync(current);

        var currentAdmission = await driver.InspectSchemaParityAdmissionAsync(current);
        Assert.True(
            currentAdmission.IsReady,
            string.Join(Environment.NewLine, currentAdmission.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(current.TargetFingerprint, currentAdmission.AppliedTargetFingerprint);
        Assert.Empty(currentAdmission.PendingOperations);
    }

    private static GroundworkPhysicalSchemaManifestSource CreateHistoricalSource(
        GroundworkPhysicalSchemaManifestSource current,
        PhysicalSchemaAppliedState historicalAppliedState)
    {
        var historicalTarget = new PhysicalSchemaTarget(
            historicalAppliedState.ManifestIdentity,
            historicalAppliedState.ManifestVersion,
            historicalAppliedState.Provider,
            ValidatePhysicalSchemaOperation.ForAppliedState(historicalAppliedState).Routes);
        var currentSnapshot = current.Snapshot;

        Assert.Equal(currentSnapshot.Manifest.Identity, historicalTarget.ManifestIdentity);
        Assert.Equal(currentSnapshot.Manifest.Version, historicalTarget.ManifestVersion);
        Assert.Equal(currentSnapshot.Provider, historicalTarget.Provider);
        Assert.True(
            currentSnapshot.StorageUnits.Select(unit => unit.Identity).ToHashSet()
                .SetEquals(historicalTarget.Routes.Select(route => route.StorageUnit)),
            "The immutable historical target must cover exactly the current deployment storage-unit set.");

        var historicalSnapshot = new GroundworkStorageCompositionSnapshot(
            currentSnapshot.Manifest,
            currentSnapshot.ManifestSources,
            currentSnapshot.ResolvedNames,
            currentSnapshot.RequiredCapabilities,
            currentSnapshot.Provider,
            currentSnapshot.TopologyRequirements,
            currentSnapshot.NamingPolicyIdentity,
            currentSnapshot.PhysicalNamePolicy,
            currentSnapshot.CompositionFingerprint,
            historicalTarget);

        Assert.Equal(currentSnapshot.CompositionFingerprint, historicalSnapshot.CompositionFingerprint);
        return new GroundworkPhysicalSchemaManifestSource(historicalSnapshot);
    }

    private static IReadOnlyList<IGroundworkStorageManifestSource> CurrentDeploymentSources() =>
    [
        new RuntimeGroundworkStorageManifestSource(),
        new SecretsGroundworkStorageManifestSource(),
        new StudioPreferencesGroundworkStorageManifestSource(),
        new DistributedGroundworkStorageManifestSource(),
        new WorkflowsDesignGroundworkStorageManifestSource(),
        new ActivitiesDesignGroundworkStorageManifestSource(),
        new GroundworkDesignAtomicWriteStorageManifestSource(),
        new PublishingGroundworkStorageManifestSource(),
        new DiagnosticsGroundworkStorageManifestSource(),
        new IdentityGroundworkStorageManifestSource()
    ];

    private static PhysicalSchemaTarget CurrentTarget(
        string provider,
        global::Groundwork.Core.Manifests.StorageManifest manifest) =>
        provider switch
        {
            "sqlite" => PhysicalSchemaTargetCompiler.Compile(
                manifest,
                SqliteGroundworkCapabilities.Provider,
                SqliteGroundworkCapabilities.PhysicalNames),
            "sql-server" => PhysicalSchemaTargetCompiler.Compile(
                manifest,
                SqlServerGroundworkCapabilities.Provider,
                SqlServerGroundworkCapabilities.PhysicalNames),
            "postgresql" => PhysicalSchemaTargetCompiler.Compile(
                manifest,
                PostgreSqlGroundworkCapabilities.Provider,
                PostgreSqlGroundworkCapabilities.PhysicalNames),
            "mongodb" => MongoDbPhysicalStorageModel.Compile(
                manifest,
                MongoDbGroundworkCapabilities.Provider,
                PhysicalNamePolicy.Identity).Target,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
        };

    private static string FixturePath(string provider)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(
            directory.FullName,
            "tests",
            "Elsa",
            "Persistence",
            "Groundwork",
            "UnifiedHost",
            "Tests",
            "Fixtures",
            "schema-evolution",
            "preview.95",
            $"{provider}-applied-state.json.gz");
    }

    private static string ReadGzip(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    private static string Digest(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();
}
