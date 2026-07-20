using System.Text.Json.Nodes;
using Elsa.Persistence.Groundwork.Testing;
using Xunit;

namespace Elsa.Persistence.Groundwork.Conformance.Tests;

/// <summary>
/// Opt-in publication entry point for the B6 local IAM/secrets facts executed by the provider matrix.
/// It intentionally excludes the #644 authority rows: their checked-in source artifacts target preview.60,
/// whereas the Spec 094 ledger currently requires preview.77 evidence.
/// </summary>
public sealed class IamSecretsProviderEvidencePublicationTests
{
    private const string PublishOptIn = "ELSA_PUBLISH_GROUNDWORK_IAM_SECRETS_EVIDENCE";
    private const string EvidenceOutput = "ELSA_GROUNDWORK_EVIDENCE_OUTPUT";

    [Fact]
    public void Preview60_identity_authority_artifacts_cannot_satisfy_the_preview77_B6_ledger()
    {
        var ledgerVersion = JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "specs/094-harden-groundwork-stores/coverage-ledger.json")))!["groundworkVersion"]!.GetValue<string>();
        var pointer = JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "specs/095-groundwork-aspnetcore-identity/evidence/providers/current.json")))!.AsObject();
        var generation = pointer["generation"]!.GetValue<string>();
        var artifact = JsonNode.Parse(File.ReadAllText(Path.Combine(
            RepoRoot,
            "specs/095-groundwork-aspnetcore-identity/evidence/providers/generations",
            generation,
            "sqlite.json")))!.AsObject();

        Assert.Equal("0.0.1-preview.77", ledgerVersion);
        var artifactVersion = artifact["testedCandidate"]!["groundworkPackageFamilyVersion"]!.GetValue<string>();
        Assert.Equal("0.0.1-preview.60", artifactVersion);
        Assert.NotEqual(ledgerVersion, artifactVersion);
    }

    [SkippableFact]
    [Trait("Category", "GroundworkEvidencePublication")]
    public async Task Publish_the_real_B6_provider_configuration_and_secret_matrices()
    {
        RequirePublicationOptIn();
        var output = Environment.GetEnvironmentVariable(EvidenceOutput)!;
        var tenantConfigurations = new List<GroundworkScenarioResult>();
        var globalConfigurations = new List<GroundworkScenarioResult>();
        var applications = new List<GroundworkScenarioResult>();
        var credentials = new List<GroundworkScenarioResult>();
        var claimMappings = new List<GroundworkScenarioResult>();
        var memberships = new List<GroundworkScenarioResult>();
        var claimMappingRoutes = new List<(GroundworkScenarioResult Result, GroundworkNativeRoutePlanResult Plan)>();
        var secretRoutes = new List<(GroundworkScenarioResult Result, GroundworkNativeRoutePlanResult Plan)>();
        var secretOrdinary = new List<GroundworkScenarioResult>();
        var secretCreateOnly = new List<GroundworkScenarioResult>();
        var secretRevisions = new List<GroundworkScenarioResult>();

        foreach (var provider in GroundworkStoreScenarioCatalog.MandatoryProviderKeys)
        {
            var iam = await IamSecretsProviderContractTests.RunOrdinaryIamRevisionConflictsAsync(provider);
            applications.Add(iam.Single(result => result.CoverageEntryId == "iam-application"));
            credentials.Add(iam.Single(result => result.CoverageEntryId == "iam-credential"));
            claimMappings.Add(iam.Single(result => result.CoverageEntryId == "iam-claim-mapping"));
            memberships.Add(iam.Single(result => result.CoverageEntryId == "iam-tenant-membership"));
            var routes = await IamSecretsProviderContractTests.RunBoundedRoutesAsync(provider);
            claimMappingRoutes.Add((routes.ClaimMappingResult, routes.ClaimMappingPlan));
            secretRoutes.Add((routes.SecretResult, routes.SecretPlan));

            var configurations = await IamSecretsProviderContractTests.RunProviderConfigurationScopeAndPrivilegeAsync(provider);
            tenantConfigurations.Add(configurations.Single(result => result.CoverageEntryId == "iam-provider-configuration-tenant"));
            globalConfigurations.Add(configurations.Single(result => result.CoverageEntryId == "iam-provider-configuration-global"));

            var secrets = await IamSecretsProviderContractTests.RunSecretCreateOnlyAndRevisionAsync(provider);
            secretOrdinary.Add(secrets.Ordinary);
            secretCreateOnly.Add(secrets.CreateOnly);
            secretRevisions.Add(secrets.Revision);
        }

        GroundworkStoreScenarioCatalog.Get("iam-provider-configuration-scope-and-privilege")
            .RequireEquivalentProviderResults("iam-provider-configuration-tenant", tenantConfigurations);
        GroundworkStoreScenarioCatalog.Get("iam-provider-configuration-scope-and-privilege")
            .RequireEquivalentProviderResults("iam-provider-configuration-global", globalConfigurations);
        foreach (var (entry, results) in new[]
                 {
                     ("iam-application", applications),
                     ("iam-credential", credentials),
                     ("iam-claim-mapping", claimMappings),
                     ("iam-tenant-membership", memberships)
                 })
        {
            GroundworkStoreScenarioCatalog.Get("iam-ordinary-store-revision-conflicts")
                .RequireEquivalentProviderResults(entry, results);
        }
        GroundworkStoreScenarioCatalog.Get("secret-concurrent-create-only")
            .RequireEquivalentProviderResults("secrets-repository", secretCreateOnly);
        GroundworkStoreScenarioCatalog.Get("secret-revision-aware-mutation")
            .RequireEquivalentProviderResults("secrets-repository", secretOrdinary);
        GroundworkStoreScenarioCatalog.Get("secret-revision-aware-mutation")
            .RequireEquivalentProviderResults("secrets-repository", secretRevisions);
        GroundworkStoreScenarioCatalog.Get("iam-bounded-query-equivalence")
            .RequireEquivalentProviderResults("iam-claim-mapping", claimMappingRoutes.Select(route => route.Result));
        GroundworkStoreScenarioCatalog.Get("secret-bounded-list-equivalence")
            .RequireEquivalentProviderResults("secrets-repository", secretRoutes.Select(route => route.Result));

        var publications = await Task.WhenAll(
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.OrdinaryRoundTrip(
                    "iam-provider-configuration-tenant",
                    "iam-provider-configuration-scope-and-privilege"),
                tenantConfigurations),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.OrdinaryRoundTrip(
                    "iam-provider-configuration-global",
                    "iam-provider-configuration-scope-and-privilege"),
                globalConfigurations),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.OrdinaryRoundTrip(
                    "iam-application",
                    "iam-ordinary-store-revision-conflicts"),
                applications),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.OrdinaryRoundTrip(
                    "iam-credential",
                    "iam-ordinary-store-revision-conflicts"),
                credentials),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.OrdinaryRoundTrip(
                    "iam-claim-mapping",
                    "iam-ordinary-store-revision-conflicts"),
                claimMappings),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.OrdinaryRoundTrip(
                    "iam-tenant-membership",
                    "iam-ordinary-store-revision-conflicts"),
                memberships),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "iam-application",
                    "expected-version-save",
                    "iam-ordinary-store-revision-conflicts"),
                applications),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "iam-claim-mapping",
                    "expected-version-save",
                    "iam-ordinary-store-revision-conflicts"),
                claimMappings),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "iam-tenant-membership",
                    "expected-version-save",
                    "iam-ordinary-store-revision-conflicts"),
                memberships),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.QueryShape(
                    "iam-claim-mapping",
                    "list-by-tenant-and-provider-bounded",
                    "list-claim-mappings-by-provider",
                    "iam-bounded-query-equivalence"),
                claimMappingRoutes.Select(route => route.Result),
                claimMappingRoutes.Select(route => route.Plan)),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.QueryShape(
                    "secrets-repository",
                    "list-by-scope-bounded",
                    "list-filtered",
                    "secret-bounded-list-equivalence"),
                secretRoutes.Select(route => route.Result),
                secretRoutes.Select(route => route.Plan)),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.OrdinaryRoundTrip(
                    "secrets-repository",
                    "secret-revision-aware-mutation"),
                secretOrdinary),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "secrets-repository",
                    "create-only-try-add",
                    "secret-concurrent-create-only"),
                secretCreateOnly),
            GroundworkProviderEvidencePublisher.PublishAsync(
                output,
                GroundworkLedgerObligation.ConcurrencySemantic(
                    "secrets-repository",
                    "expected-version-save",
                    "secret-revision-aware-mutation"),
                secretRevisions));

        Assert.All(publications, publication => Assert.Equal(4, publication.LedgerRecords.Count));
        var attachment = await GroundworkProviderEvidencePublisher.WriteLedgerAttachmentAsync(
            output,
            "iam-secrets",
            publications);
        Assert.NotEmpty(attachment.RelativePath);
        Assert.NotEmpty(attachment.Sha256);
    }

    private static void RequirePublicationOptIn()
    {
        Skip.If(
            !string.Equals(Environment.GetEnvironmentVariable(PublishOptIn), "1", StringComparison.Ordinal),
            $"Set {PublishOptIn}=1 to publish the real IAM/secrets B6 provider matrices.");
        Skip.If(
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(EvidenceOutput)),
            $"Set {EvidenceOutput} to an explicit artifact output directory before publication.");
    }

    private static string RepoRoot => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../../.."));
}
