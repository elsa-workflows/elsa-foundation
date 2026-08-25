using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Api.Services;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityAuthoringCapabilityFingerprintTests
{
    [Fact]
    public async Task Snapshot_fingerprint_keeps_the_original_anonymous_snapshot_wire_bytes()
    {
        // The display name carries characters the strict and relaxed encoders escape differently,
        // so this also pins the encoder the fingerprint bytes were originally produced with.
        var reader = new ActivityAuthoringCapabilitiesReader(
            new ActivityProviderRegistry([new Provider("allowed.provider", "Allowed & Co <täst>")]),
            new Catalog([
                new(
                    "String",
                    "String",
                    "Primitives",
                    "text",
                    new HashSet<CollectionKind> { CollectionKind.Single, CollectionKind.List },
                    true,
                    true,
                    new HashSet<string>(StringComparer.Ordinal) { "elsa.json" })
            ]),
            new DefaultActivityTypeKeyPolicy(),
            new Context());

        var view = await reader.GetAsync(new GetActivityAuthoringCapabilities(), default);

        var legacyBytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                contractSchemaVersions = view.ContractSchemaVersions,
                activityTypeKeyRules = view.ActivityTypeKeyRules,
                providers = view.Providers,
                types = view.Types,
                storageDriverKeys = view.StorageDriverKeys
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            $"sha256:{Convert.ToHexString(SHA256.HashData(legacyBytes)).ToLowerInvariant()}",
            view.SnapshotFingerprint);
    }

    private sealed class Catalog(IReadOnlyCollection<ActivityContractTypeCapability> types)
        : IActivityContractCapabilityCatalog
    {
        public IReadOnlyCollection<ActivityContractTypeCapability> Types => types;
    }

    private sealed class Context : IActivityAuthoringContextAsync
    {
        public string? TenantId => "tenant-a";
        public string ActorId => "fingerprint-tests";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("tenant-a/allowed-provider");
        public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(StringComparer.Ordinal.Equals(providerKey, "allowed.provider"));
        public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class Provider(string key, string displayName) : IActivityProvider
    {
        public string ProviderKey => key;
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
            displayName,
            [new("1", true, new HashSet<string> { "1" })],
            new([new("done", "Done", true)]));

        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityContractProposal([], []));

        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);

        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityManifestMigration(request.Source, []));
    }
}
