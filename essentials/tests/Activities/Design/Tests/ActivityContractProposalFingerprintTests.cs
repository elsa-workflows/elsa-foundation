using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Handlers;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Primitives.Models;
using Xunit;

namespace Elsa.Activities.Design.Tests;

public sealed class ActivityContractProposalFingerprintTests
{
    [Fact]
    public async Task Proposal_fingerprint_keeps_the_original_anonymous_payload_wire_bytes()
    {
        // The change payloads carry characters the strict and relaxed encoders escape differently,
        // so this also pins the encoder the fingerprint bytes were originally produced with.
        // Changes are handed to the provider unsorted and diagnostics unordered to exercise the
        // same normalization the expected bytes are computed over. Diagnostics pass through the
        // registry's sanitizer before they reach the fingerprint, so the expected payload mirrors
        // that with the same public sanitizer.
        var manifest = new ActivityProviderManifest(
            "proposal.provider",
            "1",
            JsonDocument.Parse("{\"mode\":\"strict & <täst>\"}").RootElement);
        var manifestFingerprint = ActivityProviderManifestFingerprint.Compute(manifest);
        var draft = new ActivityDefinitionDraft
        {
            Id = "draft-1",
            TenantId = "tenant-a",
            DefinitionId = "definition-1",
            Revision = 3,
            Status = ActivityDefinitionDraftStatus.Active,
            State = new(new("1", [], [], []), manifest, new Dictionary<string, string>())
        };
        var changes = new List<ActivityContractProposalChange>
        {
            new(
                "outcome:add:approved",
                ActivityContractProposalOperation.Add,
                ActivityContractMemberKind.Outcome,
                "approved",
                Outcome: new("approved", "Approved & Co <täst>", true, "Emitted when äpproved")),
            new(
                "input:add:note",
                ActivityContractProposalOperation.Add,
                ActivityContractMemberKind.Input,
                "note",
                Input: new(
                    "note",
                    "Note",
                    new TypeReference("String"),
                    true,
                    true,
                    new("literal", JsonDocument.Parse("\"hi & <there>\"").RootElement),
                    "elsa.json",
                    DisplayName: "Note & <täst>",
                    UiSpecifications: JsonDocument.Parse("{\"rows\":3}").RootElement)),
            new(
                "output:add:result",
                ActivityContractProposalOperation.Add,
                ActivityContractMemberKind.Output,
                "result",
                Output: new(
                    "result",
                    "Result",
                    new TypeReference("String"),
                    true,
                    false,
                    "elsa.json",
                    SourceRepresentation: ValueRepresentation.TextValue))
        };
        var diagnostics = new List<ActivityDiagnostic>
        {
            new(
                "activity.contract.sample-info",
                ActivityDiagnosticSeverity.Info,
                "Informational",
                new("ActivityDraft", "draft-1")),
            new(
                "activity.contract.sample-warning",
                ActivityDiagnosticSeverity.Warning,
                "Warning",
                new("ActivityDraft", "draft-1", "definition-1", Revision: 3))
        };
        // The three trailing dependencies are only used on the apply path.
        var service = new ActivityContractProposalService(
            new DraftStore(draft),
            new AuthoringStore(new()
            {
                Id = "authoring-1",
                TenantId = "tenant-a",
                DefinitionId = "definition-1",
                ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
            }),
            new ActivityProviderRegistry([new Provider(new(changes, diagnostics))]),
            null!,
            null!,
            null!,
            new Context());

        var proposal = await service.ProposeAsync(
            new(draft.Id, draft.Revision, manifest.ProviderKey, manifest.SchemaVersion, manifestFingerprint),
            default);

        IReadOnlyList<ActivityContractProposalChange> orderedChanges =
            changes.OrderBy(x => x.ChangeId, StringComparer.Ordinal).ToArray();
        var sanitizedDiagnostics = ActivityProviderDiagnosticSanitizer.Sanitize(diagnostics, manifest.ProviderKey);
        var legacyBytes = JsonSerializer.SerializeToUtf8Bytes(
            new
            {
                draftId = draft.Id,
                revision = draft.Revision,
                providerKey = manifest.ProviderKey,
                providerSchemaVersion = manifest.SchemaVersion,
                manifestFingerprint,
                changes = orderedChanges,
                diagnostics = sanitizedDiagnostics
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(
            $"sha256:{Convert.ToHexString(SHA256.HashData(legacyBytes)).ToLowerInvariant()}",
            proposal.ProposalFingerprint);
    }

    private sealed class DraftStore(ActivityDefinitionDraft draft) : IActivityDefinitionDraftStore
    {
        public Task<ActivityDefinitionDraft?> FindAsync(string draftId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StringComparer.Ordinal.Equals(draftId, draft.Id) ? draft : null);

        public Task<IReadOnlyList<ActivityDefinitionDraft>> ListByDefinitionAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionDraft>>([]);
    }

    private sealed class AuthoringStore(ActivityDefinitionAuthoringState state) : IActivityDefinitionAuthoringStore
    {
        public Task<ActivityDefinitionAuthoringState?> FindAsync(string definitionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(StringComparer.Ordinal.Equals(definitionId, state.DefinitionId) ? state : null);

        public Task<IReadOnlyList<ActivityDefinitionAuthoringState>> ListAsync(IEnumerable<string> definitionIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionAuthoringState>>([]);
    }

    private sealed class Context : IActivityAuthoringContextAsync
    {
        public string? TenantId => "tenant-a";
        public string ActorId => "fingerprint-tests";
        public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult("tenant-a/proposal-provider");
        public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(StringComparer.Ordinal.Equals(providerKey, "proposal.provider"));
        public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
        public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(false);
    }

    private sealed class Provider(ActivityContractProposal proposal) : IActivityProvider
    {
        public string ProviderKey => "proposal.provider";
        public IReadOnlySet<string> SupportedManifestSchemas { get; } = new HashSet<string> { "1" };
        public ActivityProviderAuthoringCapabilities AuthoringCapabilities { get; } = new(
            "Proposal & Co <täst>",
            [new("1", true, new HashSet<string>())],
            new([]));

        public ValueTask<ActivityContractProposal> ProposeContractAsync(ActivityProviderContractProposalRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(proposal);

        public ValueTask<IReadOnlyList<ActivityDiagnostic>> ValidateAsync(ActivityProviderManifest manifest, ActivityContract contract, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ActivityDiagnostic>>([]);

        public ValueTask<ActivityManifestMigration> MigrateAsync(ActivityManifestMigrationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new ActivityManifestMigration(request.Source, []));
    }
}
