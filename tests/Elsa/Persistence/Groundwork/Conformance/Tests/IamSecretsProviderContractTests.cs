using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Persistence.Groundwork;
using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// T088's IAM provider matrix. Lifecycle and injected-failure scenarios stay in T090.
/// </summary>
public sealed class IamSecretsProviderContractTests
{
    private const string ProviderMatrixOptIn = "ELSA_RUN_GROUNDWORK_IAM_SECRETS_PROVIDER_MATRIX";
    private const string TenantA = "iam-secrets-contract-a";
    private const string TenantB = "iam-secrets-contract-b";

    private static readonly GroundworkStoreScenarioEvidence RecoveryEvidence =
        GroundworkStoreScenarioEvidence.Cancellation |
        GroundworkStoreScenarioEvidence.FailureInjection |
        GroundworkStoreScenarioEvidence.ProcessRestart;

    [Fact]
    public void Iam_and_secrets_catalog_partition_is_closed_between_t088_and_t090()
    {
        var definitions = Definitions();
        var t088 = T088Definitions();
        var t090 = T090Definitions();
        var probes = T088Probes();

        Assert.Empty(t088.Select(x => x.ScenarioId).Intersect(t090.Select(x => x.ScenarioId), StringComparer.Ordinal));
        Assert.Equal(
            definitions.Select(x => x.ScenarioId).Order(StringComparer.Ordinal),
            t088.Concat(t090).Select(x => x.ScenarioId).Order(StringComparer.Ordinal));
        Assert.Equal(
            t088.Select(x => x.ScenarioId).Order(StringComparer.Ordinal),
            probes.SelectMany(probe => probe.ScenarioIds).Order(StringComparer.Ordinal));
        Assert.All(t090, definition => Assert.NotEqual(
            GroundworkStoreScenarioEvidence.None,
            definition.RequiredEvidence & RecoveryEvidence));
    }

    [SkippableFact]
    [Trait("Category", "GroundworkProviderMatrix")]
    public async Task Iam_t088_public_contracts_pass_on_every_provider()
    {
        RequireProviderMatrixOptIn();

        foreach (var providerKey in GroundworkStoreScenarioCatalog.MandatoryProviderKeys)
        {
            foreach (var probe in T088Probes())
                await probe.ExecuteAsync(providerKey);
        }
    }

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> Definitions() =>
        GroundworkStoreScenarioCatalog.ForFamily(GroundworkStoreScenarioFamily.Identity)
            .OrderBy(x => x.ScenarioId, StringComparer.Ordinal)
            .ToArray();

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> T088Definitions() =>
        Definitions().Where(definition => (definition.RequiredEvidence & RecoveryEvidence) == GroundworkStoreScenarioEvidence.None).ToArray();

    private static IReadOnlyList<GroundworkStoreScenarioDefinition> T090Definitions() =>
        Definitions().Where(definition => (definition.RequiredEvidence & RecoveryEvidence) != GroundworkStoreScenarioEvidence.None).ToArray();

    private static IReadOnlyList<IamSecretsScenarioProbe> T088Probes() =>
    [
        Probe(["identity-tenant-local-normalized-uniqueness"], AssertTenantLocalNormalizedUniquenessAsync),
        Probe(
            ["iam-ordinary-store-revision-conflicts"],
            async providerKey => _ = await RunOrdinaryIamRevisionConflictsAsync(providerKey)),
        Probe(
            ["iam-provider-configuration-scope-and-privilege"],
            async providerKey => _ = await RunProviderConfigurationScopeAndPrivilegeAsync(providerKey)),
        Probe(["iam-bounded-query-equivalence"], AssertIamBoundedQueriesAsync)
    ];

    private static async Task AssertTenantLocalNormalizedUniquenessAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()]);

        await using var firstClient = await driver.OpenPhysicalClientAsync(Access(TenantA));
        await using var secondClient = await driver.OpenPhysicalClientAsync(Access(TenantA));
        var first = new GroundworkUserStore(firstClient.DocumentStore, Accessor(TenantA), firstClient.BoundedDocumentStore);
        var second = new GroundworkUserStore(secondClient.DocumentStore, Accessor(TenantA), secondClient.BoundedDocumentStore);
        await Task.WhenAll(
            first.SaveAsync(User("user-a", TenantA, "shared-name")).AsTask(),
            second.SaveAsync(User("user-b", TenantA, "shared-name")).AsTask());

        var stored = await Task.WhenAll(
            first.FindAsync(TenantA, "user-a").AsTask(),
            second.FindAsync(TenantA, "user-b").AsTask());
        Assert.Single(stored.Where(user => user is not null));

        await using var otherTenantClient = await driver.OpenPhysicalClientAsync(Access(TenantB));
        var otherTenant = new GroundworkUserStore(
            otherTenantClient.DocumentStore,
            Accessor(TenantB),
            otherTenantClient.BoundedDocumentStore);
        await otherTenant.SaveAsync(User("user-a", TenantB, "shared-name"));
        Assert.NotNull(await otherTenant.FindAsync(TenantB, "user-a"));
    }

    private static async Task AssertIamBoundedQueriesAsync(string providerKey)
    {
        await new FoundationBoundedQueryContractTests()
            .Iam_normalized_lookup_executes_in_scope_and_inside_the_declared_bound(providerKey);
        await new FoundationBoundedQueryContractTests()
            .Iam_claim_mapping_pages_are_complete_deterministic_and_bounded(providerKey);
    }

    internal static async Task<IReadOnlyList<GroundworkScenarioResult>> RunOrdinaryIamRevisionConflictsAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()]);
        await using var firstClient = await driver.OpenPhysicalClientAsync(Access(TenantA));
        await using var secondClient = await driver.OpenPhysicalClientAsync(Access(TenantA));

        var firstApplications = new GroundworkApplicationStore(firstClient.DocumentStore, Accessor(TenantA));
        var secondApplications = new GroundworkApplicationStore(secondClient.DocumentStore, Accessor(TenantA));
        await AssertRevisionConflictAsync(
            firstApplications,
            secondApplications,
            Application(),
            application => application with { DisplayName = "current-application" },
            application => application with { DisplayName = "stale-application" },
            application => application.DisplayName);

        var firstCredentials = new GroundworkCredentialStore(firstClient.DocumentStore, Accessor(TenantA));
        var secondCredentials = new GroundworkCredentialStore(secondClient.DocumentStore, Accessor(TenantA));
        await AssertRevisionConflictAsync(
            firstCredentials,
            secondCredentials,
            Credential(),
            credential => credential with { Status = CredentialStatus.Revoked },
            credential => credential with { Status = CredentialStatus.Active },
            credential => credential.Status);

        var firstClaimMappings = new GroundworkClaimMappingStore(
            firstClient.DocumentStore,
            Accessor(TenantA),
            firstClient.BoundedDocumentStore);
        var secondClaimMappings = new GroundworkClaimMappingStore(
            secondClient.DocumentStore,
            Accessor(TenantA),
            secondClient.BoundedDocumentStore);
        await AssertRevisionConflictAsync(
            firstClaimMappings,
            secondClaimMappings,
            ClaimMapping(),
            rule => rule with { Order = 2 },
            rule => rule with { Order = 3 },
            rule => rule.Order);

        var user = User("membership-user", TenantA, "membership-user");
        await new GroundworkUserStore(firstClient.DocumentStore, Accessor(TenantA)).SaveAsync(user);
        var firstMemberships = new GroundworkTenantMembershipStore(firstClient.DocumentStore, Accessor(TenantA));
        var secondMemberships = new GroundworkTenantMembershipStore(secondClient.DocumentStore, Accessor(TenantA));
        await AssertRevisionConflictAsync(
            firstMemberships,
            secondMemberships,
            Membership(user.Id),
            membership => membership with { Status = TenantMembershipStatus.Suspended },
            membership => membership with { Status = TenantMembershipStatus.Active },
            membership => membership.Status);

        var definition = GroundworkStoreScenarioCatalog.Get("iam-ordinary-store-revision-conflicts");
        var composition = Composition(driver, typeof(IdentityGroundworkStorageManifestSource));
        var observations = new[]
        {
            new GroundworkScenarioObservation("current-revision", "advanced"),
            new GroundworkScenarioObservation("stale-delete-count", "not-exposed"),
            new GroundworkScenarioObservation("stale-update-count", "1")
        };
        return
        [
            definition.CreateResult("iam-application", driver.Descriptor, composition, 2, observations),
            definition.CreateResult("iam-claim-mapping", driver.Descriptor, composition, 2, observations),
            definition.CreateResult("iam-credential", driver.Descriptor, composition, 2, observations),
            definition.CreateResult("iam-tenant-membership", driver.Descriptor, composition, 2, observations)
        ];
    }

    internal static async Task<IReadOnlyList<GroundworkScenarioResult>> RunProviderConfigurationScopeAndPrivilegeAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        await driver.ResetPhysicalAsync([new IdentityGroundworkStorageManifestSource()]);

        await using var tenantClient = await driver.OpenPhysicalClientAsync(Access(TenantA));
        var tenantStore = new GroundworkProviderConfigurationStore(tenantClient.DocumentStore, Accessor(TenantA));
        var tenantConfiguration = TenantConfiguration(TenantA);
        await tenantStore.SaveAsync(tenantConfiguration);
        Assert.NotNull(await tenantStore.FindForTenantAsync(TenantA, tenantConfiguration.Provider));

        await using var otherTenantClient = await driver.OpenPhysicalClientAsync(Access(TenantB));
        var otherTenantStore = new GroundworkProviderConfigurationStore(otherTenantClient.DocumentStore, Accessor(TenantB));
        var scopeException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            otherTenantStore.FindForTenantAsync(TenantA, tenantConfiguration.Provider).AsTask());
        Assert.DoesNotContain(TenantA, scopeException.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(TenantB, scopeException.Message, StringComparison.Ordinal);

        var global = GlobalConfiguration();
        var ordinaryWriteException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            tenantStore.SaveAsync(global).AsTask());
        Assert.DoesNotContain(TenantA, ordinaryWriteException.Message, StringComparison.Ordinal);

        await using var globalClient = await driver.OpenPhysicalClientAsync(DocumentStoreAccess.Global);
        var privilegedGlobal = new GroundworkProviderConfigurationStore(
            globalClient.DocumentStore,
            new FixedAccessContextAccessor(PersistenceAccessContext.PrivilegedGlobal(
                new PersistenceAccessPurpose("iam-secrets-provider-contract"))));
        await privilegedGlobal.SaveAsync(global);
        Assert.NotNull(await privilegedGlobal.FindGlobalAsync(global.Provider));

        var definition = GroundworkStoreScenarioCatalog.Get("iam-provider-configuration-scope-and-privilege");
        var composition = Composition(driver, typeof(IdentityGroundworkStorageManifestSource));
        var observations = new[]
        {
            new GroundworkScenarioObservation("ordinary-write-rejection-count", "1"),
            new GroundworkScenarioObservation("scope-disclosure-count", "0"),
            new GroundworkScenarioObservation("write-state", "tenant-and-global-present")
        };
        return
        [
            definition.CreateResult("iam-provider-configuration-tenant", driver.Descriptor, composition, 2, observations),
            definition.CreateResult("iam-provider-configuration-global", driver.Descriptor, composition, 2, observations)
        ];
    }

    internal static async Task<IamSecretsBoundedRouteEvidence> RunBoundedRoutesAsync(string providerKey)
    {
        await new FoundationBoundedQueryContractTests()
            .Iam_claim_mapping_pages_are_complete_deterministic_and_bounded(providerKey);
        var descriptor = await DescribeAsync(providerKey);
        var plans = await ProviderNativePlanTests.CaptureNativeRoutePlansAsync(
            GroundworkProviderDriverFactory.Create(providerKey));
        var claimMappingPlan = plans.Single(candidate =>
            string.Equals(candidate.Request.DocumentKind, IdentityStorageManifest.IdentityClaimMappingDocumentKind, StringComparison.Ordinal) &&
            string.Equals(candidate.Request.QueryIdentity, IdentityStorageManifest.ListClaimMappingsByProviderQuery, StringComparison.Ordinal));
        return new IamSecretsBoundedRouteEvidence(
            CreateBoundedRouteResult(
                providerKey,
                descriptor,
                "iam-bounded-query-equivalence",
                "iam-claim-mapping",
                claimMappingPlan),
            claimMappingPlan);
    }

    private static GroundworkScenarioResult CreateBoundedRouteResult(
        string providerKey,
        GroundworkProviderDescriptor descriptor,
        string scenarioId,
        string coverageEntryId,
        GroundworkNativeRoutePlanResult plan)
    {
        var composition = Composition(descriptor, scenarioId);
        var path = GroundworkExecutionPath.Create(providerKey, scenarioId, composition);
        return GroundworkStoreScenarioCatalog.Get(scenarioId).CreateResult(
            coverageEntryId,
            descriptor,
            composition,
            clients: 1,
            [
                new GroundworkScenarioObservation("native-execution", "verified"),
                new GroundworkScenarioObservation("ordered-identities", "verified"),
                new GroundworkScenarioObservation("returned-count", "verified")
            ],
            nativeEvidence: GroundworkNativePlanEvidence.Create(path, scenarioId, plan.Evidence));
    }

    private static async Task AssertRevisionConflictAsync(
        GroundworkApplicationStore first,
        GroundworkApplicationStore second,
        ApplicationRecord record,
        Func<ApplicationRecord, ApplicationRecord> current,
        Func<ApplicationRecord, ApplicationRecord> stale,
        Func<ApplicationRecord, object?> observedValue)
    {
        await first.SaveAsync(record);
        var firstRevision = await first.FindWithRevisionAsync(record.TenantId, record.Id);
        var secondRevision = await second.FindWithRevisionAsync(record.TenantId, record.Id);
        Assert.NotNull(firstRevision);
        Assert.NotNull(secondRevision);
        var firstCandidate = current(firstRevision!.Record);
        var secondCandidate = stale(secondRevision!.Record);
        var outcomes = await Task.WhenAll(
            first.SaveWithRevisionAsync(firstCandidate, firstRevision.Revision).AsTask(),
            second.SaveWithRevisionAsync(secondCandidate, secondRevision.Revision).AsTask());
        Assert.Single(outcomes.Where(result => result.Status is IamRevisionSaveStatus.Saved));
        Assert.Single(outcomes.Where(result => result.Status is IamRevisionSaveStatus.Conflict));
        Assert.Equal(
            observedValue(outcomes[0].Status is IamRevisionSaveStatus.Saved ? firstCandidate : secondCandidate),
            observedValue((await first.FindAsync(record.TenantId, record.Id))!));
    }

    private static async Task AssertRevisionConflictAsync(
        GroundworkCredentialStore first,
        GroundworkCredentialStore second,
        CredentialRecord record,
        Func<CredentialRecord, CredentialRecord> current,
        Func<CredentialRecord, CredentialRecord> stale,
        Func<CredentialRecord, object?> observedValue)
    {
        await first.SaveAsync(record);
        var firstRevision = await first.FindWithRevisionAsync(record.TenantId, record.Id);
        var secondRevision = await second.FindWithRevisionAsync(record.TenantId, record.Id);
        Assert.NotNull(firstRevision);
        Assert.NotNull(secondRevision);
        var firstCandidate = current(firstRevision!.Record);
        var secondCandidate = stale(secondRevision!.Record);
        var outcomes = await Task.WhenAll(
            first.SaveWithRevisionAsync(firstCandidate, firstRevision.Revision).AsTask(),
            second.SaveWithRevisionAsync(secondCandidate, secondRevision.Revision).AsTask());
        Assert.Single(outcomes.Where(result => result.Status is IamRevisionSaveStatus.Saved));
        Assert.Single(outcomes.Where(result => result.Status is IamRevisionSaveStatus.Conflict));
        Assert.Equal(
            observedValue(outcomes[0].Status is IamRevisionSaveStatus.Saved ? firstCandidate : secondCandidate),
            observedValue((await first.FindAsync(record.TenantId, record.Id))!));
    }

    private static async Task AssertRevisionConflictAsync(
        GroundworkClaimMappingStore first,
        GroundworkClaimMappingStore second,
        ClaimMappingRule record,
        Func<ClaimMappingRule, ClaimMappingRule> current,
        Func<ClaimMappingRule, ClaimMappingRule> stale,
        Func<ClaimMappingRule, object?> observedValue)
    {
        await first.SaveAsync(record);
        var firstRevision = await first.FindWithRevisionAsync(record.TenantId, record.Provider, record.Id);
        var secondRevision = await second.FindWithRevisionAsync(record.TenantId, record.Provider, record.Id);
        Assert.NotNull(firstRevision);
        Assert.NotNull(secondRevision);
        var firstCandidate = current(firstRevision!.Record);
        var secondCandidate = stale(secondRevision!.Record);
        var outcomes = await Task.WhenAll(
            first.SaveWithRevisionAsync(firstCandidate, firstRevision.Revision).AsTask(),
            second.SaveWithRevisionAsync(secondCandidate, secondRevision.Revision).AsTask());
        Assert.Single(outcomes.Where(result => result.Status is IamRevisionSaveStatus.Saved));
        Assert.Single(outcomes.Where(result => result.Status is IamRevisionSaveStatus.Conflict));
        Assert.Equal(
            observedValue(outcomes[0].Status is IamRevisionSaveStatus.Saved ? firstCandidate : secondCandidate),
            observedValue(Assert.Single(await first.ListForProviderAsync(record.TenantId, record.Provider))));
    }

    private static async Task AssertRevisionConflictAsync(
        GroundworkTenantMembershipStore first,
        GroundworkTenantMembershipStore second,
        TenantMembershipRecord record,
        Func<TenantMembershipRecord, TenantMembershipRecord> current,
        Func<TenantMembershipRecord, TenantMembershipRecord> stale,
        Func<TenantMembershipRecord, object?> observedValue)
    {
        await first.SaveAsync(record);
        var firstRevision = await first.FindWithRevisionAsync(record.TenantId, record.UserId);
        var secondRevision = await second.FindWithRevisionAsync(record.TenantId, record.UserId);
        Assert.NotNull(firstRevision);
        Assert.NotNull(secondRevision);
        var firstCandidate = current(firstRevision!.Record);
        var secondCandidate = stale(secondRevision!.Record);
        var outcomes = await Task.WhenAll(
            first.SaveWithRevisionAsync(firstCandidate, firstRevision.Revision).AsTask(),
            second.SaveWithRevisionAsync(secondCandidate, secondRevision.Revision).AsTask());
        Assert.Single(outcomes.Where(result => result.Status is IamRevisionSaveStatus.Saved));
        Assert.Single(outcomes.Where(result => result.Status is IamRevisionSaveStatus.Conflict));
        Assert.Equal(
            observedValue(outcomes[0].Status is IamRevisionSaveStatus.Saved ? firstCandidate : secondCandidate),
            observedValue((await first.FindAsync(record.TenantId, record.UserId))!));
    }

    private static IamSecretsScenarioProbe Probe(
        IReadOnlyList<string> scenarioIds,
        Func<string, Task> executeAsync) => new(scenarioIds, executeAsync);

    private static DocumentStoreAccess Access(string tenantId) =>
        DocumentStoreAccess.Scoped(new StorageScope(tenantId));

    private static FixedAccessContextAccessor Accessor(string tenantId) =>
        new(PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)));

    private static UserRecord User(string id, string tenantId, string userName) => new(
        id, tenantId, userName, $"{id}@example.test", userName, UserStatus.Active,
        ResourceOwnership.Foundation, new HashSet<string>(), new HashSet<string>());

    private static ApplicationRecord Application() => new(
        "application", TenantA, "application-client", "application", ApplicationType.Confidential,
        ResourceOwnership.Foundation, new HashSet<string> { "client_credentials" }, new HashSet<string>());

    private static CredentialRecord Credential() => new(
        "credential", TenantA, CredentialSubjectType.Application, "application", CredentialKind.ClientSecret,
        "sha256:credential", "SHA-256", CredentialStatus.Active, DateTimeOffset.UnixEpoch.AddDays(1));

    private static ClaimMappingRule ClaimMapping() => new(
        "claim-mapping",
        TenantA,
        "oidc",
        "groups",
        "administrators",
        new HashSet<string> { "administrator" },
        new HashSet<string> { "secrets:read" },
        1,
        true);

    private static TenantMembershipRecord Membership(string userId) => new(
        TenantA, userId, TenantMembershipStatus.Active, new HashSet<string>(), new HashSet<string>());

    private static ProviderConfigurationRecord TenantConfiguration(string tenantId) => new(
        "oidc", tenantId, "external-oidc", true, false, ProviderCapabilities.ExternalOidcDefault,
        new Dictionary<string, string> { ["authority"] = "https://tenant.example" });

    private static ProviderConfigurationRecord GlobalConfiguration() => new(
        "oidc", null, "external-oidc", true, false, ProviderCapabilities.ExternalOidcDefault,
        new Dictionary<string, string> { ["authority"] = "https://global.example" });

    private static GroundworkCompositionFingerprint Composition(
        GroundworkProviderDriver driver,
        Type manifestSource) =>
        GroundworkCompositionFingerprint.Create(string.Join(
            '\n',
            $"provider={driver.Descriptor.ProviderIdentity}",
            $"provider-version={driver.Descriptor.ProviderVersion}",
            $"topology={driver.Descriptor.Topology.Description}",
            $"manifest={manifestSource.AssemblyQualifiedName}"));

    private static GroundworkCompositionFingerprint Composition(
        GroundworkProviderDescriptor descriptor,
        string scenarioId) =>
        GroundworkCompositionFingerprint.Create(string.Join(
            '\n',
            $"provider={descriptor.ProviderIdentity}",
            $"provider-version={descriptor.ProviderVersion}",
            $"topology={descriptor.Topology.Description}",
            $"scenario={scenarioId}"));

    private static async Task<GroundworkProviderDescriptor> DescribeAsync(string providerKey)
    {
        await using var driver = GroundworkProviderDriverFactory.Create(providerKey);
        await driver.InitializeAsync();
        return driver.Descriptor;
    }

    private static void RequireProviderMatrixOptIn() =>
        Skip.If(
            !string.Equals(Environment.GetEnvironmentVariable(ProviderMatrixOptIn), "1", StringComparison.Ordinal),
            $"Set {ProviderMatrixOptIn}=1 to run the SQLite, SQL Server, PostgreSQL, and MongoDB IAM contract matrix.");

    private sealed record IamSecretsScenarioProbe(IReadOnlyList<string> ScenarioIds, Func<string, Task> ExecuteAsync);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}

internal sealed record IamSecretsBoundedRouteEvidence(
    GroundworkScenarioResult ClaimMappingResult,
    GroundworkNativeRoutePlanResult ClaimMappingPlan);
