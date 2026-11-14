using System.Text;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.SourceCapture;

/// <summary>Read-only exact authoring-version association. Closure fan-in and final semantic verification are shared.</summary>
internal sealed class NativeReleaseCaptureSource(IWorkflowVersionSnapshotReader versions, IWorkflowExecutableSourceReferenceReader references,
    IWorkflowExecutableStore materials, IPublicationRecordStore publications, IWorkflowExecutableRootWriteLeaseManager leases,
    IReleaseRuntimeArtifactCodec codec, INativeReleaseCaptureContextAccessor source, INativeReleaseCaptureAuthorization authorization,
    IReleaseRuntimeSourceContextAccessor runtimeSource, IReleaseRuntimeSourceAuthorization runtimeAuthorization,
    IPersistenceAccessContextAccessor access) : IReleaseCaptureSource
{
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.native-workflow-source", "1");
    public IReadOnlyCollection<string> SupportedSelectionKinds { get; } = Array.AsReadOnly(new[] { NativeReleaseCaptureSelections.WorkflowDefinitionVersion });

    public async ValueTask ExecuteAsync(IReadOnlyList<ReleaseCaptureSelection> selections, ReleaseBundleLimits limits,
        Func<ReleaseSourceCapture, CancellationToken, ValueTask> capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selections); ArgumentNullException.ThrowIfNull(limits); ArgumentNullException.ThrowIfNull(capture);
        var scope = Context(); var partition = new PersistenceScope(scope.Partition!); var budget = new Budget(limits);
        if (selections.Count == 0 || selections.Count > limits.MaxMembers / 2) throw Refuse("native_source_selection_limit");
        var selected = new List<Selected>(); var used = new HashSet<string>(StringComparer.Ordinal);
        var materialCopies = new Dictionary<string, RuntimeMaterialSourceSnapshot>(StringComparer.Ordinal);
        foreach (var selection in selections)
        {
            cancellationToken.ThrowIfCancellationRequested(); CheckContext();
            if (selection.Kind != NativeReleaseCaptureSelections.WorkflowDefinitionVersion || selection.Action != ReleaseRootAction.ActivateServing ||
                string.IsNullOrWhiteSpace(selection.Identity) || selection.Identity.Length > 4096 || !used.Add(selection.Identity)) throw Refuse("native_source_selector_unsupported");
            await AuthorizeVersion(selection.Identity, cancellationToken);
            var version = await versions.FindAsync(selection.Identity, partition.Value,
                new() { MaxSourceBytes = budget.PayloadLimit(), MaxIdentityBytes = 4096 }, cancellationToken) ?? throw Refuse("native_source_version_missing");
            budget.Charge(Encoding.UTF8.GetByteCount(version.StateSource));
            if (version.TenantId != partition.Value) throw Refuse("native_source_version_scope_mismatch");
            if (version.VersionId != selection.Identity || selection.Version is { } requested && requested != new ReleaseOwnerVersion("semver", version.Version, null))
                throw Refuse("native_source_selected_version_mismatch");
            var candidates = await references.ListByDefinitionVersionBoundedAsync(version.VersionId, partition, budget.ReadLimits(), cancellationToken);
            var accepted = new List<(WorkflowExecutableSourceReference Reference, RuntimeMaterialSourceSnapshot Material)>();
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested(); CheckContext(); budget.Read(candidate.Scope, candidate.StoredPayloadBytes, partition);
                var reference = candidate.Reference;
                if (reference.Scope != WorkflowExecutableReferenceScope.Published) continue;
                if (reference.TenantId != scope.TenantId) throw Refuse("native_source_publication_tenant_mismatch");
                if (reference.DefinitionId != version.DefinitionId || reference.DefinitionVersionId != version.VersionId ||
                    reference.SourceKind != "WorkflowDefinitionVersion" || reference.SourceId != version.VersionId || reference.SourceVersion != version.Version || reference.ArtifactVersion != version.Version ||
                    reference.PublishedAt is null || reference.ExpiresAt is not null) throw Refuse("native_source_publication_association_invalid");
                if (!await Published(reference, version, cancellationToken)) continue;
                await AuthorizeSelected(reference, version.VersionId, cancellationToken);
                if (!materialCopies.TryGetValue(reference.ArtifactId, out var material))
                {
                    material = await materials.FindMaterialBoundedAsync(reference.ArtifactId, partition, budget.ReadLimits(), cancellationToken)
                        ?? throw Refuse("native_source_published_material_missing");
                    budget.Read(material.Scope, material.StoredPayloadBytes, partition); materialCopies.Add(reference.ArtifactId, material);
                }
                if (material.Material.NativeWorkflow is null || material.Material.Identity.ArtifactId != reference.ArtifactId)
                    throw Refuse("native_source_published_material_mismatch");
                // The stored first producer may differ from the selected source labels. Preserve both independently.
                accepted.Add((reference, material));
            }
            if (accepted.Count == 0) throw Refuse("native_source_published_version_missing");
            var actual = accepted[0].Material.Material.Identity;
            if (accepted.Any(item => !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(actual, item.Material.Material.Identity)))
                throw Refuse("native_source_publication_ambiguous");
            var winner = accepted.OrderBy(item => item.Reference.SourceReferenceId, StringComparer.Ordinal).First();
            selected.Add(new(version, winner.Reference, winner.Material));
        }
        var roots = selected.Select(item => item.Material.Material.Identity).Distinct().ToArray();
        await leases.ExecuteFencedAsync(roots, "release-native-source:" + Guid.NewGuid().ToString("N"), partition, budget.ReadLimits(),
            async (id, hash, pin, token) =>
            {
                CheckContext();
                if (!await runtimeAuthorization.AuthorizeMaterialAsync(scope, id, hash, pin, token)) throw Refuse("native_source_export_forbidden");
                CheckContext();
            }, async (_, token) =>
            {
                var content = new NativeSourceMembers(limits); var rootRecords = new List<ReleaseBundleRoot>();
                foreach (var item in selected)
                {
                    await AuthorizeVersion(item.Version.VersionId, token); await AuthorizeSelected(item.Reference, item.Version.VersionId, token);
                    var original = item.Material.Material.Identity;
                    var current = await materials.FindMaterialBoundedAsync(original.ArtifactId, partition, budget.ReadLimits(), token)
                        ?? throw Refuse("native_source_retained_material_missing");
                    budget.Read(current.Scope, current.StoredPayloadBytes, partition);
                    if (current.Material.NativeWorkflow is null || current.Material.IncarnationId != item.Material.Material.IncarnationId ||
                        !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(original, current.Material.Identity)) throw Refuse("native_source_retained_material_changed");
                    var body = content.Add(codec.EncodeWorkflow(new(scope, current.Material.NativeWorkflow), limits));
                    var association = content.Add(codec.EncodeWorkflowSelection(new(scope, item.Version.DefinitionId, item.Version.VersionId, item.Version.Version,
                        item.Version.StateSource, item.Reference.SourceKind, item.Reference.SourceId, item.Reference.SourceVersion,
                        item.Reference.SourceReferenceId, item.Reference.Scope, original), limits));
                    rootRecords.Add(new(association.Key, body.Key, ReleaseRootAction.ActivateServing));
                }
                CheckContext(); await capture(new(content, rootRecords), token); CheckContext();
            }, cancellationToken);

        async ValueTask<bool> Published(WorkflowExecutableSourceReference reference, WorkflowVersionSnapshot version, CancellationToken token)
        {
            // A timestamp or Published enum is insufficient provenance. Legacy publications require
            // an explicit owner-qualified adoption path before this source profile can export them.
            if (reference.ActivationId is null) throw Refuse("native_source_legacy_publication_unsupported");
            if (reference.SourceReferenceId != WorkflowActivationReferenceIdentity.Create(reference.ActivationId) || string.IsNullOrWhiteSpace(reference.SlotId))
                throw Refuse("native_source_canonical_publication_invalid");
            var journal = await publications.FindOriginBoundedAsync(reference.ActivationId, partition, budget.ReadLimits(), token)
                ?? throw Refuse("native_source_publication_proof_missing");
            budget.Read(journal.Scope, journal.StoredMetadataBytes, partition);
            if (journal.PublicationId != reference.ActivationId || journal.SourceReferenceId != reference.SourceReferenceId ||
                journal.SlotId != reference.SlotId || journal.WorkflowDefinitionId != version.DefinitionId || journal.WorkflowDefinitionVersionId != version.VersionId ||
                journal.ArtifactId != reference.ArtifactId) throw Refuse("native_source_publication_proof_conflict");
            return journal.Status is (PublicationStatus.Active or PublicationStatus.Retired) && journal.ActivatedAt is not null;
        }
        async ValueTask AuthorizeVersion(string id, CancellationToken token)
        { CheckContext(); if (!await authorization.AuthorizeWorkflowVersionAsync(scope, id, token)) throw Refuse("native_source_export_forbidden"); CheckContext(); }
        async ValueTask AuthorizeSelected(WorkflowExecutableSourceReference reference, string version, CancellationToken token)
        { CheckContext(); if (!await authorization.AuthorizeSelectedMaterialAsync(scope, version, reference.SourceReferenceId, reference.ArtifactId, token)) throw Refuse("native_source_export_forbidden"); CheckContext(); }
        void CheckContext() { if (Context() != scope) throw Refuse("native_source_scope_changed"); }
    }

    private ReleaseRuntimeContentScope Context()
    {
        var scope = source.Current;
        if (scope is null || string.IsNullOrWhiteSpace(scope.Authority) || string.IsNullOrWhiteSpace(scope.Partition) || scope.Authority.Length > 4096 ||
            scope != runtimeSource.Current || scope.Partition != access.Current.RequireScope(admitPrivileged: true).Value) throw Refuse("native_source_scope_mismatch");
        return scope;
    }
    private static ReleaseCaptureException Refuse(string code) => new(code);
    private sealed record Selected(WorkflowVersionSnapshot Version, WorkflowExecutableSourceReference Reference, RuntimeMaterialSourceSnapshot Material);
    private sealed class Budget(ReleaseBundleLimits limits)
    {
        private long _bytes;
        public void Charge(int bytes) { _bytes = checked(_bytes + bytes); if (bytes < 0 || _bytes > limits.MaxExpandedBytes) throw Refuse("native_source_aggregate_byte_limit"); }
        public void Read(PersistenceScope scope, int bytes, PersistenceScope expected) { if (scope != expected) throw Refuse("native_source_scope_mismatch"); Charge(bytes); }
        public int PayloadLimit() { var remaining = limits.MaxExpandedBytes - _bytes; if (remaining < 1) throw Refuse("native_source_aggregate_byte_limit"); return (int)Math.Min(limits.MaxBlobBytes, remaining); }
        public RuntimeSourceReadLimits ReadLimits()
        { var value = new RuntimeSourceReadLimits { MaxPayloadBytes = PayloadLimit(), MaxAggregatePayloadBytes = limits.MaxExpandedBytes - _bytes, MaxJsonDepth = limits.MaxJsonDepth,
            MaxMaterials = limits.MaxMembers, MaxDependencies = limits.MaxEdges, MaxDependencyDepth = limits.MaxDependencyDepth }; value.Validate(); return value; }
    }
}

internal sealed class NativeSourceMembers(ReleaseBundleLimits limits) : IReleaseContentReader
{
    private readonly Dictionary<ReleaseMemberKey, ReleaseEncodedRuntimeMember> _members = [];
    private long _bytes;
    public IReadOnlyList<ReleaseContentMember> Members => Array.AsReadOnly(_members.Values.Select(item => new ReleaseContentMember(item.Descriptor, item.OwnerVersion)).ToArray());
    public ReleaseContentMember Add(ReleaseEncodedRuntimeMember item)
    {
        var key = new ReleaseMemberKey(item.Descriptor.Kind, item.Descriptor.Identity);
        if (_members.TryGetValue(key, out var existing))
        { if (existing.OwnerVersion != item.OwnerVersion || existing.Descriptor.Codec != item.Descriptor.Codec || existing.Descriptor.CodecVersion != item.Descriptor.CodecVersion ||
                existing.Descriptor.PayloadDigest != item.Descriptor.PayloadDigest || existing.Descriptor.ByteLength != item.Descriptor.ByteLength ||
                !existing.GetPayloadBytes().AsSpan().SequenceEqual(item.GetPayloadBytes())) throw new ReleaseCaptureException("native_source_member_conflict"); return GetMember(key); }
        if (_members.Count >= limits.MaxMembers || item.Descriptor.ByteLength > limits.MaxBlobBytes || (_bytes = checked(_bytes + item.Descriptor.ByteLength)) > limits.MaxExpandedBytes)
            throw new ReleaseCaptureException("native_source_member_limit");
        _members.Add(key, item); return GetMember(key);
    }
    public ReleaseContentMember GetMember(ReleaseMemberKey key) => _members.TryGetValue(key, out var item) ? new(item.Descriptor, item.OwnerVersion) : throw new KeyNotFoundException();
    public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => _members[key].GetPayloadBytes();
}
