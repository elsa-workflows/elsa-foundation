using System.Text;
using System.Security.Cryptography;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.SourceCapture;

/// <summary>Original reusable catalog projections reached by exact placement/dependency version IDs, never latest/draft.</summary>
internal sealed class NativeCatalogSourceMemberContributor(IReleaseRuntimeArtifactCodec codec,
    IWorkflowExecutableSourceReferenceReader references, IExecutableActivityTemplateReader templates,
    INativeReleaseCaptureContextAccessor source, INativeReleaseCatalogCaptureAuthorization authorization,
    IReleaseRuntimeSourceContextAccessor runtimeSource, IReleaseRuntimeSourceAuthorization runtimeAuthorization,
    IPersistenceAccessContextAccessor access, IActivityPublicationSnapshotReader? publications = null) : IReleaseSourceMemberContributor
{
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.native-catalog-source", "2");
    public IReadOnlyCollection<ReleaseCodecKey> SupportedContainerCodecs { get; } = Array.AsReadOnly(new[]
    {
        new ReleaseCodecKey(ReleaseRuntimeCodecs.WorkflowKind, ReleaseRuntimeCodecs.Workflow, 1),
        new ReleaseCodecKey(ReleaseRuntimeCodecs.TemplateKind, ReleaseRuntimeCodecs.Template, 1)
    });

    public async ValueTask ExecuteAsync(IReadOnlyList<ReleaseContentMember> containers, IReleaseContentReader captured, ReleaseBundleLimits limits,
        Func<IReleaseContentReader, CancellationToken, ValueTask> capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(containers); ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(limits); ArgumentNullException.ThrowIfNull(capture);
        if (containers.Count > limits.MaxMembers) throw Refuse("native_catalog_member_limit");
        var scope = Context(); var partition = new PersistenceScope(scope.Partition!); var ids = new HashSet<string>(StringComparer.Ordinal);
        var additions = new NativeSourceMembers(limits); var seen = new HashSet<ReleaseMemberKey>(); var work = 0; long bytes = 0;
        foreach (var container in containers)
        {
            cancellationToken.ThrowIfCancellationRequested(); CheckContext();
            if (!SupportedContainerCodecs.Contains(container.Codec) || !seen.Add(container.Key)) throw Refuse("native_catalog_container_invalid");
            IEnumerable<ExecutableNode> nodes;
            if (container.Key.Kind == ReleaseRuntimeCodecs.WorkflowKind)
            {
                var workflow = codec.DecodeWorkflow(CapturedPayload(container), limits);
                if (workflow.Executable.Identity.ArtifactId != container.Key.Identity || container.OwnerVersion != new ReleaseOwnerVersion("semver", workflow.Executable.Identity.ArtifactVersion, null)) throw Refuse("native_catalog_container_invalid"); CheckScope(workflow.Scope); nodes = workflow.Executable.Nodes;
            }
            else
            {
                var template = codec.DecodeTemplate(CapturedPayload(container), limits);
                if (template.Template.TemplateId != container.Key.Identity || container.OwnerVersion is not null) throw Refuse("native_catalog_container_invalid"); CheckScope(template.Scope); nodes = template.Template.NodesById.Values;
                foreach (var dependency in template.Template.DirectDependencies) Add(dependency.DefinitionVersionId);
            }
            foreach (var node in nodes)
            {
                if (++work > limits.MaxEdges) throw Refuse("native_catalog_work_limit");
                if (node.Metadata.TryGetValue("activity.definitionVersionId", out var id)) Add(id);
            }
        }
        foreach (var id in ids.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested(); CheckContext(); var key = new ReleaseMemberKey(ReleaseRuntimeCodecs.ActivityVersionKind, id);
            if (Contains(captured, key))
            {
                var member = captured.GetMember(key);
                if (member.Codec != new ReleaseCodecKey(ReleaseRuntimeCodecs.ActivityVersionKind, ReleaseRuntimeCodecs.ActivityVersion, 2)) throw Refuse("native_catalog_original_publication_missing");
                var existing = codec.DecodeActivityVersion(CapturedPayload(member), limits); CheckScope(existing.Scope);
                if (existing.VersionId != id || member.OwnerVersion != new ReleaseOwnerVersion("semver", existing.Version, null)) throw Refuse("native_catalog_version_conflict");
                if (existing.Publication is null) throw Refuse("native_catalog_original_publication_missing");
                continue; // A prior retained source callback supplied it; final independent owner verification is mandatory.
            }
            if (publications is null) throw Refuse("native_catalog_source_profile_unsupported");
            if (!await authorization.AuthorizeActivityVersionAsync(scope, id, cancellationToken)) throw Refuse("native_catalog_export_forbidden"); CheckContext();
            var publication = await publications.FindAsync(id, partition.Value, new() { MaxJsonBytes = PayloadLimit(), MaxIdentityBytes = 4096 }, cancellationToken)
                ?? throw Refuse("native_catalog_publication_missing");
            var details = publication.Details ?? throw Refuse("native_catalog_projection_profile_unsupported");
            if (publication.StoredPayloadBytes <= 0) throw Refuse("native_catalog_projection_profile_unsupported"); Charge(publication.StoredPayloadBytes);
            var contractBytes = publication.GetContractBytes(); var providerBytes = publication.GetProviderBytes();
            var requirementsBytes = details.GetRuntimeRequirementsBytes(); var measurementsBytes = details.GetResourceMeasurementsBytes();
            if (publication.VersionId != id) throw Refuse("native_catalog_version_conflict");
            var reference = await references.FindBoundedAsync(publication.SourceReferenceId, partition, ReadLimits(), cancellationToken)
                ?? throw Refuse("native_catalog_source_reference_missing"); ChargeRead(reference.Scope, reference.StoredPayloadBytes);
            if (reference.Reference.Scope != WorkflowExecutableReferenceScope.Published || reference.Reference.DefinitionId != publication.DefinitionId ||
                reference.Reference.DefinitionVersionId != publication.VersionId || reference.Reference.ArtifactId != publication.TemplateId ||
                reference.Reference.ArtifactVersion != publication.Version || reference.Reference.PublishedAt is null || reference.Reference.ActivationId is not null ||
                reference.Reference.TenantId != publication.TenantId) throw Refuse("native_catalog_source_association_conflict");
            if (!await runtimeAuthorization.AuthorizeTemplateAsync(scope, publication.TemplateId, publication.TemplateHash, cancellationToken)) throw Refuse("native_catalog_export_forbidden"); CheckContext();
            var template = await templates.FindBoundedAsync(publication.TemplateId, partition, ReadLimits(), cancellationToken)
                ?? throw Refuse("native_catalog_template_missing"); ChargeRead(template.Scope, template.StoredPayloadBytes);
            if (template.Template.TemplateId != publication.TemplateId || template.Template.TemplateHash != publication.TemplateHash ||
                template.Template.ProviderFingerprint != publication.ProviderFingerprint) throw Refuse("native_catalog_template_conflict");
            var encodedTemplate = codec.EncodeTemplate(new(scope, template.Template), limits);
            var templateKey = new ReleaseMemberKey(ReleaseRuntimeCodecs.TemplateKind, publication.TemplateId);
            if (Contains(captured, templateKey))
            { if (!captured.GetPayload(templateKey).Span.SequenceEqual(encodedTemplate.GetPayloadBytes())) throw Refuse("native_catalog_captured_template_conflict"); }
            else additions.Add(encodedTemplate);
            try
            {
                using var contract = JsonDocument.Parse(contractBytes, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
                using var provider = JsonDocument.Parse(providerBytes, new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
                var providerKey = Text(provider.RootElement, "providerKey"); var providerVersion = Text(provider.RootElement, "schemaVersion");
                var metadata = JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1, publicationId = details.PublicationId, originalResolutionKind = publication.ResolutionKind.ToString(),
                    sourceDraftId = publication.SourceDraftId, sourceVersionId = details.SourceVersionId, lifecycle = publication.Lifecycle.ToString(),
                    publishedAt = details.PublishedAt, directDependencyCount = details.DirectDependencyCount, closedTemplateCount = details.ClosedTemplateCount,
                    resumeTargetCount = details.ResumeTargetCount, runtimeRequirementsJson = Encoding.UTF8.GetString(requirementsBytes),
                    resourceMeasurementsJson = Encoding.UTF8.GetString(measurementsBytes)
                });
                var resolution = publication.ResolutionKind == ActivityDefinitionVersionResolutionKind.Unspecified
                    ? string.IsNullOrWhiteSpace(publication.SourceDraftId) ? "AuthorableActivity" : "ReusableTemplateBoundary" : publication.ResolutionKind.ToString();
                additions.Add(codec.EncodeActivityVersion(new(scope, publication.DefinitionId, publication.VersionId, publication.Version, publication.ActivityTypeKey,
                    resolution, contract.RootElement, providerKey, providerVersion, provider.RootElement.GetProperty("payload"), publication.ProviderFingerprint,
                    publication.TemplateId, publication.TemplateHash, publication.SourceReferenceId, reference.Reference.SourceKind, reference.Reference.SourceId,
                    new(true, publication.TenantId, Encoding.UTF8.GetString(contractBytes), Encoding.UTF8.GetString(providerBytes), metadata)), limits));
            }
            catch (ReleaseRuntimeArtifactException) { throw; }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
            { throw Refuse("native_catalog_original_json_invalid"); }
        }
        CheckContext(); await capture(additions, cancellationToken); CheckContext();

        byte[] CapturedPayload(ReleaseContentMember member)
        {
            var actual = captured.GetMember(member.Key);
            if (actual.Key != member.Key || actual.Codec != member.Codec || actual.OwnerVersion != member.OwnerVersion ||
                actual.Descriptor.ByteLength != member.Descriptor.ByteLength || actual.Descriptor.PayloadDigest != member.Descriptor.PayloadDigest)
                throw Refuse("native_catalog_captured_member_conflict");
            var payload = captured.GetPayload(member.Key);
            if (payload.Length > limits.MaxBlobBytes || payload.Length != member.Descriptor.ByteLength) throw Refuse("native_catalog_captured_member_conflict");
            var copy = payload.ToArray();
            if ("sha256:" + Convert.ToHexStringLower(SHA256.HashData(copy)) != member.Descriptor.PayloadDigest)
                throw Refuse("native_catalog_captured_member_conflict");
            return copy;
        }
        void Add(string id)
        { if (string.IsNullOrWhiteSpace(id) || new UTF8Encoding(false, true).GetByteCount(id) > 4096) throw Refuse("native_catalog_version_invalid"); if (ids.Add(id) && ids.Count > limits.MaxMembers) throw Refuse("native_catalog_member_limit"); }
        int PayloadLimit() { var left = limits.MaxExpandedBytes - bytes; if (left <= 0) throw Refuse("native_catalog_aggregate_byte_limit"); return (int)Math.Min(left, limits.MaxBlobBytes); }
        RuntimeSourceReadLimits ReadLimits()
        { var value = new RuntimeSourceReadLimits { MaxPayloadBytes = PayloadLimit(), MaxAggregatePayloadBytes = limits.MaxExpandedBytes - bytes, MaxJsonDepth = limits.MaxJsonDepth,
            MaxMaterials = limits.MaxMembers, MaxDependencies = limits.MaxEdges, MaxDependencyDepth = limits.MaxDependencyDepth }; value.Validate(); return value; }
        void Charge(int amount) { bytes = checked(bytes + amount); if (amount < 0 || bytes > limits.MaxExpandedBytes) throw Refuse("native_catalog_aggregate_byte_limit"); }
        void ChargeRead(PersistenceScope actual, int amount) { if (actual != partition) throw Refuse("native_catalog_scope_mismatch"); Charge(amount); }
        void CheckScope(ReleaseRuntimeContentScope actual) { if (actual != scope) throw Refuse("native_catalog_scope_mismatch"); }
        void CheckContext() { if (Context() != scope) throw Refuse("native_catalog_scope_changed"); }
    }
    private ReleaseRuntimeContentScope Context()
    {
        var scope = source.Current;
        if (scope is null || string.IsNullOrWhiteSpace(scope.Authority) || string.IsNullOrWhiteSpace(scope.Partition) || scope.Authority.Length > 4096 ||
            scope != runtimeSource.Current || scope.Partition != access.Current.RequireScope(admitPrivileged: true).Value) throw Refuse("native_catalog_scope_mismatch");
        return scope;
    }
    private static bool Contains(IReleaseContentReader reader, ReleaseMemberKey key) { try { _ = reader.GetMember(key); return true; } catch (KeyNotFoundException) { return false; } }
    private static string Text(JsonElement value, string name) => value.GetProperty(name) is { ValueKind: JsonValueKind.String } text && !string.IsNullOrWhiteSpace(text.GetString())
        ? text.GetString()! : throw Refuse("native_catalog_original_json_invalid");
    private static ReleaseCaptureException Refuse(string code) => new(code);
}
