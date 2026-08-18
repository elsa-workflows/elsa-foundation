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
using Groundwork.Core.Text;
using Groundwork.Core.Validation;
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
    private const string DarwinUnicodeIdentityAlgorithm =
        "groundwork-unicode-ordinal-ignore-case-v1-3206f759667cb9cc764ec243dfb3d322a39970184efab619e80163c36d86818f";
    private const string LinuxNobleUnicodeIdentityAlgorithm =
        "groundwork-unicode-ordinal-ignore-case-v1-124ca0d0d2b045d7be0e6aea8f07f74fbc0428a13c53a47d8a7d41db71b5ec5f";
    private static readonly DateTimeOffset PlanningTime = DateTimeOffset.Parse("2026-07-31T00:00:00Z");

    /// <summary>
    /// The storage units spec 151 clean-breaks, excluded from the preview.95 upgrade proof **by name**.
    /// Every other unit must still upgrade additively and in place, so this list is the whole exception —
    /// adding to it is a decision, not a fix.
    /// <list type="bullet">
    /// <item><c>workflowTriggerBinding</c> and <c>recurringTriggerSchedule</c>: T033/T034 renamed the
    /// persisted activation identity and the index built over it — index <c>by-publication</c> →
    /// <c>by-activation</c>, <c>by-publication-and-trigger-binding-id</c> →
    /// <c>by-activation-and-trigger-binding-id</c>, <c>by-publication-and-schedule-id</c> →
    /// <c>by-activation-and-schedule-id</c>, projected field <c>publicationId</c> → <c>activationId</c> and
    /// <c>schedule.publicationId</c> → <c>schedule.activationId</c>. The Groundwork serializer uses
    /// <c>JsonSerializerDefaults.Web</c>, so the projected field path *is* the camelCase property name and the
    /// rename necessarily moves both. A rename is a removal plus an addition, which the additive #44 contract
    /// forbids (GW-SCHEMA-004); and the persisted rows themselves still spell <c>publicationId</c>, so they
    /// could not be read after the rename either. These two projection units are dropped and re-created, not
    /// upgraded.</item>
    /// <item><c>publishingPublicationSlot</c>: T027 superseded publishing's slot store with the runtime
    /// activation authority and T036 removed the orphaned manifest unit. One physical activation ledger per
    /// engine; the old unit has no writer and no successor rows to carry over.</item>
    /// <item><c>publishingProjectionIntent</c>: T121 deleted <c>PublicationProjectionReconciler</c>, the
    /// delivery-intent ledger's only consumer, and T122 removed the orphaned contract, models, stores and
    /// manifest unit. The activation coordinator that replaced the reconciler deliberately carries no
    /// delivery-intent ledger, so there is no successor unit and nothing to carry over — the same shape as
    /// <c>publishingPublicationSlot</c> above.</item>
    /// </list>
    /// Legitimate pre-1.0 with no consumers (research risk R2). A deployment carrying preview.95 data resets
    /// these units and republishes — see <c>docs/serialization.md</c>.
    /// </summary>
    private static readonly HashSet<string> CleanBreakStorageUnits = new(StringComparer.Ordinal)
    {
        "workflowTriggerBinding",
        "recurringTriggerSchedule",
        "publishingPublicationSlot",
        "publishingProjectionIntent"
    };

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

    public static TheoryData<string, string> Providers
    {
        get
        {
            var family = CurrentFixtureFamily();
            return new TheoryData<string, string>
            {
                { "sqlite", family.CompressedDigests["sqlite"] },
                { "sql-server", family.CompressedDigests["sql-server"] },
                { "postgresql", family.CompressedDigests["postgresql"] },
                { "mongodb", family.CompressedDigests["mongodb"] }
            };
        }
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public void Preview102_is_an_additive_upgrade_from_the_immutable_preview95_reference_state(
        string provider,
        string expectedCompressedDigest)
    {
        var fixture = FixturePath(provider);
        Assert.Equal(expectedCompressedDigest, Digest(File.ReadAllBytes(fixture)));
        var fixtureState = PhysicalSchemaAppliedStateSerializer.Deserialize(ReadGzip(fixture));
        Assert.Equal(HistoricalProviderContractVersion, fixtureState.Provider.Version);

        var manifest = new GroundworkAllFeaturesWithIdentityAndDiagnosticsDeploymentSchema().CreateManifest();
        var current = CurrentTarget(provider, manifest);
        Assert.Equal(fixtureState.ManifestIdentity, current.ManifestIdentity);
        Assert.Equal(fixtureState.Provider.Name, current.Provider.Name);

        // Nothing the clean break removed may still be declared on a clean-break unit: if a by-publication
        // index survived T034's rename sweep, excluding that unit below would hide it instead of proving it
        // gone. Scoped to the named units on purpose. T122 removed `publishingProjectionIntent`, the last
        // unit that still declared a `by-publication` index — publishing kept it on publication naming inside
        // its own domain (T028), and the unit is gone rather than renamed, so no route declares it now.
        Assert.DoesNotContain(
            current.Routes
                .Where(route => IsCleanBreak(route.StorageUnit.Value))
                .SelectMany(route => route.Indexes.Select(index => $"{route.StorageUnit.Value}/{index.Identity}")),
            identity => identity.Contains("by-publication", StringComparison.Ordinal));

        // Step 1, against the fixture VERBATIM: the only thing this upgrade removes is the named clean break.
        // A removal touching any other storage unit — or any other diagnostic at all — fails here, so the
        // exception cannot quietly widen.
        var verbatimPlan = PhysicalSchemaDiffPlanner.Plan(
            current,
            PhysicalSchemaHistoryState.FromApplied(fixtureState),
            PlanningTime);
        var unexplained = verbatimPlan.Diagnostics
            .Where(diagnostic => !IsCleanBreakRemoval(diagnostic))
            .ToArray();
        Assert.True(
            unexplained.Length == 0,
            $"{HistoricalElsaCommit}: {string.Join("; ", unexplained.Select(x => $"{x.Code}: {x.Message}"))}");
        Assert.NotEmpty(verbatimPlan.Diagnostics);

        // Step 2, against the deployment that reset exactly those units — which is what the clean break asks
        // of an upgrading installation. Everything else must still plan as an additive, in-place upgrade, so
        // the rest of this test is unchanged and still has all of its teeth.
        var historical = WithoutCleanBreakUnits(fixtureState);
        Assert.True(
            historical.AppliedOperations.Count < fixtureState.AppliedOperations.Count,
            "The preview.95 fixture no longer carries the clean-break units; prune CleanBreakStorageUnits instead of keeping a dead exception.");

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
            .Where(operation => !IsCleanBreak(operation.StorageUnit?.Value))
            .Select(operation => $"{operation.StorageUnit!.Value}/{operation.SubjectIdentity}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var currentIndexIdentities = current.Routes
            .SelectMany(route => route.Indexes.Select(index => $"{route.StorageUnit.Value}/{index.Identity}"))
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(
            historicalIndexIdentities,
            identity => Assert.Contains(identity, currentIndexIdentities));

        // This used to filter the plan against a list of operation kinds an additive upgrade may
        // contain. It cannot discriminate: every kind Groundwork can plan today is additive, so the
        // list grew to name all twelve and its Assert.All silently became unable to fail. Nothing about
        // the shape of that check was salvageable -- a kind filter only has teeth while some kind is
        // excluded from it.
        //
        // Pin the vocabulary instead. The upgrade is additive *because* Groundwork has no destructive
        // operation kind, so that premise is the thing worth guarding: if a kind is ever added, this
        // fails and someone has to decide whether an additive upgrade may contain it, rather than the
        // question going unasked the way it did when RebuildPhysicalIndex arrived.
        Assert.Equal(
            new[]
            {
                nameof(PhysicalSchemaOperationKind.AddProjectedColumn),
                nameof(PhysicalSchemaOperationKind.ApplyProviderDefinition),
                nameof(PhysicalSchemaOperationKind.BackfillCanonicalJson),
                nameof(PhysicalSchemaOperationKind.CreateCollectionElementStorage),
                nameof(PhysicalSchemaOperationKind.CreateLinkedStorage),
                nameof(PhysicalSchemaOperationKind.CreatePhysicalEntityStorage),
                nameof(PhysicalSchemaOperationKind.CreatePhysicalIndex),
                nameof(PhysicalSchemaOperationKind.CreatePrimaryStorage),
                nameof(PhysicalSchemaOperationKind.FinalizeProjectedColumn),
                nameof(PhysicalSchemaOperationKind.RebuildPhysicalIndex),
                nameof(PhysicalSchemaOperationKind.RecordAppliedState),
                nameof(PhysicalSchemaOperationKind.ValidatePhysicalSchema)
            },
            Enum.GetNames<PhysicalSchemaOperationKind>().Order(StringComparer.Ordinal));

        // Widening an applied index is executed as a rebuild, and the preview.95 reference state
        // predates every widening, so the upgrade has to plan at least one. Asserting anything about
        // rebuilds without this would be an assertion over a possibly-empty sequence.
        Assert.NotEmpty(plan.Operations.OfType<RebuildPhysicalIndexOperation>());

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

    /// <summary>
    /// Returns the preview.95 applied state as a deployment that reset the <see cref="CleanBreakStorageUnits"/>
    /// units and kept everything else. The fixture on disk is never rewritten: its recorded routes are read
    /// verbatim, the clean-break units are dropped, and the remaining target is re-planned from an empty
    /// history and completed — the same public path <c>Preview95SchemaFixtureReproducer</c> and
    /// <see cref="CreateHistoricalSource"/> already use, and the only one available because
    /// <c>PhysicalSchemaAppliedState</c> has no public constructor and validates its snapshot fingerprint.
    /// </summary>
    private static PhysicalSchemaAppliedState WithoutCleanBreakUnits(PhysicalSchemaAppliedState state)
    {
        var target = new PhysicalSchemaTarget(
            state.ManifestIdentity,
            state.ManifestVersion,
            state.Provider,
            [.. ValidatePhysicalSchemaOperation.ForAppliedState(state).Routes
                .Where(route => !IsCleanBreak(route.StorageUnit.Value))]);
        var plan = PhysicalSchemaDiffPlanner.Plan(target, PhysicalSchemaHistoryState.Empty, PlanningTime);
        Assert.True(
            plan.IsApplicable,
            string.Join("; ", plan.Diagnostics.Select(x => $"{x.Code}: {x.Message}")));

        return plan.Complete(
            [.. plan.Operations
                .Where(operation => operation is not RecordPhysicalSchemaAppliedStateOperation)
                .Select(operation => new PhysicalSchemaOperationAcknowledgement(
                    operation.Identity,
                    operation.Fingerprint,
                    PlanningTime))],
            PlanningTime);
    }

    private static bool IsCleanBreak(string? storageUnit) =>
        storageUnit is not null && CleanBreakStorageUnits.Contains(storageUnit);

    /// <summary>
    /// True when the diagnostic reports the removal of an applied operation that belongs to a storage unit
    /// spec 151 clean-breaks. Applied operation identities are <c>kind:storageUnit:subject:fingerprint</c>,
    /// so the unit is the second colon-separated segment of the identity the diagnostic names.
    /// </summary>
    private static bool IsCleanBreakRemoval(GroundworkDiagnostic diagnostic) =>
        diagnostic.Code == "GW-SCHEMA-004" &&
        RemovedOperationStorageUnit(diagnostic) is { } storageUnit &&
        IsCleanBreak(storageUnit);

    private static string? RemovedOperationStorageUnit(GroundworkDiagnostic diagnostic)
    {
        foreach (var candidate in new[] { diagnostic.Target, QuotedIdentity(diagnostic.Message) })
        {
            if (candidate?.Split(':') is [_, { Length: > 0 } storageUnit, _, ..])
                return storageUnit;
        }

        return null;
    }

    private static string? QuotedIdentity(string? message)
    {
        if (message is null)
            return null;
        var start = message.IndexOf('\'');
        if (start < 0)
            return null;
        var end = message.IndexOf('\'', start + 1);
        return end < 0 ? null : message[(start + 1)..end];
    }

    private static GroundworkPhysicalSchemaManifestSource CreateHistoricalSource(
        GroundworkPhysicalSchemaManifestSource current,
        PhysicalSchemaAppliedState historicalAppliedState)
    {
        // Same named exception as the planning test: the units spec 151 clean-breaks are never materialized in
        // the historical database, so the current target creates them fresh instead of upgrading them in place.
        // Everything else must still upgrade without a reset — that is the proof this test owns.
        var historicalTarget = new PhysicalSchemaTarget(
            historicalAppliedState.ManifestIdentity,
            historicalAppliedState.ManifestVersion,
            historicalAppliedState.Provider,
            [.. ValidatePhysicalSchemaOperation.ForAppliedState(historicalAppliedState).Routes
                .Where(route => !IsCleanBreak(route.StorageUnit.Value))]);
        var currentSnapshot = current.Snapshot;

        Assert.Equal(currentSnapshot.Manifest.Identity, historicalTarget.ManifestIdentity);
        Assert.Equal(currentSnapshot.Manifest.Version, historicalTarget.ManifestVersion);
        Assert.Equal(currentSnapshot.Provider, historicalTarget.Provider);
        // Subset, not equality. A deployment upgraded from the historical schema legitimately gains storage
        // units that did not exist then — creating those in place, without a reset, is the very thing this
        // test proves — so requiring equality would make any new unit unlandable forever. What must still
        // hold is the other direction: the historical target may not carry a unit the current deployment has
        // dropped, because then the upgrade path would be proven against something no longer deployed.
        var currentUnits = currentSnapshot.StorageUnits.Select(unit => unit.Identity).ToHashSet();
        var historicalUnits = historicalTarget.Routes.Select(route => route.StorageUnit).ToHashSet();
        // Guards the subset check against passing for the wrong reason: an empty or truncated fixture is a
        // subset of everything, and would make the upgrade proof vacuous.
        Assert.NotEmpty(historicalUnits);
        Assert.True(
            historicalUnits.IsSubsetOf(currentUnits),
            "The immutable historical target carries storage units the current deployment no longer declares: " +
            string.Join(", ", historicalUnits.Except(currentUnits).Select(unit => unit.Value).Order(StringComparer.Ordinal)));

        // The historical deployment declared only the units its fixture carries routes for. Units added
        // since are created by the upgrade rather than pre-existing, which is the case this test exists to
        // prove — so the historical snapshot must carry the HISTORICAL manifest. Reusing the current one
        // would trip the snapshot's own invariant that every storage unit has exactly one compiled route,
        // and that invariant is the production rule, not a test convenience.
        var historicalManifest = currentSnapshot.Manifest with
        {
            StorageUnits = currentSnapshot.Manifest.StorageUnits
                .Where(unit => historicalUnits.Contains(unit.Identity))
                .ToArray()
        };

        var historicalSnapshot = new GroundworkStorageCompositionSnapshot(
            historicalManifest,
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
            CurrentFixtureFamily().RelativeDirectory,
            $"{provider}-applied-state.json.gz");
    }

    private static FixtureFamily CurrentFixtureFamily() =>
        PortableStringComparison.UnicodeOrdinalIgnoreCaseAlgorithmId switch
        {
            DarwinUnicodeIdentityAlgorithm => new FixtureFamily(
                string.Empty,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sqlite"] = "afab9a4a6cc6842d1658ddbb46fa8a8cbd64bf3e8e8e4a9a166922b2c06790e2",
                    ["sql-server"] = "a7fe88ba0b6d467092fe484cd4be88a79787f6794d51ea9ed5dbae9c27223ee5",
                    ["postgresql"] = "e51b1c3480e2f78c3b70efcff7b6fdfa86acb4904f0453253de6884b5745a0c6",
                    ["mongodb"] = "61c380a25bbb33ceef1ebee062022fba3390b16f73268d72ed0c2b4a4ab4e1e6"
                }),
            LinuxNobleUnicodeIdentityAlgorithm => new FixtureFamily(
                "unicode-identity-124ca0d0d2b0",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sqlite"] = "71d1fffe9d597bec07303ae6ade808e22adcd0f631c05cfdb30f98bc3e82778e",
                    ["sql-server"] = "bf92b166a648f7d5c874772142811e4430dc1624e46953a8ccb33ebf81e214ab",
                    ["postgresql"] = "da57998276ff80d3bd9c00dba8a49394d971d236873b4883e071ab2e36b34570",
                    ["mongodb"] = "8738474157f07102d7c6a73e72816dce787e5fe67a7e0388dc976c8321aa42dc"
                }),
            var algorithm => throw new InvalidOperationException(
                $"No immutable preview.95 fixture family exists for Unicode identity algorithm '{algorithm}'.")
        };

    private static string ReadGzip(string path)
    {
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd();
    }

    private static string Digest(byte[] value) =>
        Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant();

    private sealed record FixtureFamily(
        string RelativeDirectory,
        IReadOnlyDictionary<string, string> CompressedDigests);
}
