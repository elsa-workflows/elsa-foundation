using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

internal sealed class ReleaseRuntimeArtifactVerifier(RuntimeCapturedContentCache cache, IWorkflowExecutableHasher hasher) : IReleaseArtifactSemanticVerifier
{
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.runtime-release-profile", "1");
    public IReadOnlyCollection<ReleaseCodecKey> SupportedCodecs { get; } = Array.AsReadOnly(new[]
    {
        new ReleaseCodecKey(ReleaseRuntimeCodecs.WorkflowKind, ReleaseRuntimeCodecs.Workflow, 1),
        new ReleaseCodecKey(ReleaseRuntimeCodecs.TemplateKind, ReleaseRuntimeCodecs.Template, 1),
        new ReleaseCodecKey(ReleaseRuntimeCodecs.ModelKind, ReleaseRuntimeCodecs.Model, 1),
        new ReleaseCodecKey(ReleaseRuntimeCodecs.SelectionKind, ReleaseRuntimeCodecs.Selection, 1),
        new ReleaseCodecKey(ReleaseRuntimeCodecs.ActivityVersionKind, ReleaseRuntimeCodecs.ActivityVersion, 1)
    });

    public ValueTask<ReleaseSemanticFacts> VerifyAsync(ReleaseContentMember member, ReadOnlyMemory<byte> payload,
        IReleaseContentReader content, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!cache.MatchesPayload(content, member, payload))
                return ValueTask.FromResult(Failed(member.Key, "captured_runtime_identity_conflict"));
            var inspection = new RuntimeInspection(member.Key, cache, content, cancellationToken);
            switch (member.Key.Kind)
            {
                case ReleaseRuntimeCodecs.WorkflowKind:
                    var workflow = cache.Workflow(content, member.Key); inspection.Scope(workflow.Scope);
                    if (!Pin(workflow.Executable.Identity) || !SemVer.TryParse(workflow.Executable.Identity.ArtifactVersion, out _) || workflow.Executable.InputContract?.Version != WorkflowExecutableInputContract.CurrentVersion || !hasher.MatchesHash(workflow.Executable))
                        return ValueTask.FromResult(Failed(member.Key, "invalid_native_identity_or_hash"));
                    inspection.Workflow(workflow.Executable, workflow.Scope);
                    return ValueTask.FromResult(inspection.Facts(new(member.Key.Kind, workflow.Executable.Identity.ArtifactId), Version(workflow.Executable.Identity.ArtifactVersion)));
                case ReleaseRuntimeCodecs.TemplateKind:
                    var template = cache.Template(content, member.Key); inspection.Scope(template.Scope);
                    var hash = ActivityTemplateHasher.Compute(template.Template.Root, template.Template.ResumeTargets, template.Template.DirectDependencies,
                        template.Template.ClosedTemplates, template.Template.RuntimeRequirements, template.Template.StorageDriverRequirements,
                        template.Template.ProviderFingerprint, template.Template.CompatibilityMetadata);
                    if (hash != template.Template.TemplateHash || template.Template.TemplateId != "activity-template-" + hash[7..])
                        return ValueTask.FromResult(Failed(member.Key, "invalid_template_identity_or_hash"));
                    inspection.Template(template.Template, template.Scope);
                    return ValueTask.FromResult(inspection.Facts(new(member.Key.Kind, template.Template.TemplateId), null));
                case ReleaseRuntimeCodecs.ModelKind:
                    var model = cache.Model(content, member.Key); inspection.Scope(model.Scope);
                    if (!Pin(model.Model.Identity) || model.Model.ComputeContentHash() != model.Model.Identity.ArtifactHash)
                        return ValueTask.FromResult(Failed(member.Key, "invalid_model_identity_or_hash"));
                    foreach (var dependency in model.Model.Dependencies)
                    {
                        var target = inspection.RuntimePin(dependency.Executable.ArtifactId, model.Scope);
                        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(target.Pin, dependency.Executable)) inspection.Invalid("model_dependency_pin_mismatch");
                        inspection.Edge(target.Key, ReleaseEdgeRelation.Dependency, dependency.BindingId);
                    }
                    inspection.Requirements(model.Model.RuntimeRequirements, []);
                    inspection.ModelSite(model.Model);
                    return ValueTask.FromResult(inspection.Facts(new(member.Key.Kind, model.Model.Identity.ArtifactId), new("opaque", model.Model.Identity.ArtifactVersion, null)));
                case ReleaseRuntimeCodecs.SelectionKind:
                    var selection = cache.Selection(content, member.Key); inspection.Scope(selection.Scope);
                    if (!SemVer.TryParse(selection.Version, out _) || selection.ReferenceScope != WorkflowExecutableReferenceScope.Published ||
                        selection.SourceKind != "WorkflowDefinitionVersion" || string.IsNullOrWhiteSpace(selection.SourceId) ||
                        string.IsNullOrWhiteSpace(selection.DefinitionId) || string.IsNullOrWhiteSpace(selection.SourceReferenceId) || !Pin(selection.Artifact))
                        return ValueTask.FromResult(Failed(member.Key, "invalid_native_selection_association"));
                    ReleaseRuntimeArtifactCodec.CheckJson(System.Text.Encoding.UTF8.GetBytes(selection.StateSource), new());
                    var selected = inspection.RuntimePin(selection.Artifact.ArtifactId, selection.Scope);
                    if (selected.Key.Kind != ReleaseRuntimeCodecs.WorkflowKind || !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(selected.Pin, selection.Artifact))
                        return ValueTask.FromResult(Failed(member.Key, "native_selection_pin_mismatch"));
                    inspection.Edge(selected.Key, ReleaseEdgeRelation.Association, "");
                    return ValueTask.FromResult(inspection.Facts(new(member.Key.Kind, selection.DefinitionVersionId), Version(selection.Version), [new(selected.Key, ReleaseRootAction.ActivateServing)]));
                case ReleaseRuntimeCodecs.ActivityVersionKind:
                    var activity = cache.ActivityVersion(content, member.Key); inspection.Scope(activity.Scope);
                    inspection.ActivityVersion(activity);
                    return ValueTask.FromResult(inspection.Facts(new(member.Key.Kind, activity.VersionId), Version(activity.Version)));
                default: return ValueTask.FromResult(Failed(member.Key, "unsupported_runtime_kind", ReleaseSemanticStatus.Unsupported));
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (ReleaseRuntimeArtifactException exception)
        { return ValueTask.FromResult(Failed(member.Key, exception.Code, exception.Code.Contains("unsupported", StringComparison.Ordinal) ? ReleaseSemanticStatus.Unsupported : ReleaseSemanticStatus.Invalid)); }
        catch (Exception)
        { return ValueTask.FromResult(Failed(member.Key, "invalid_runtime_artifact")); }
    }

    private static bool Pin(WorkflowExecutableIdentity pin) => !string.IsNullOrWhiteSpace(pin.ArtifactId) && !string.IsNullOrWhiteSpace(pin.DefinitionId) &&
        !string.IsNullOrWhiteSpace(pin.DefinitionVersionId) && !string.IsNullOrWhiteSpace(pin.ArtifactVersion) && pin.ArtifactHash.Length == 71 &&
        pin.ArtifactHash.StartsWith("sha256:", StringComparison.Ordinal) && !pin.ArtifactHash.AsSpan(7).ContainsAnyExcept("0123456789abcdef");
    private static ReleaseOwnerVersion Version(string value) => new("semver", value, null);
    private static ReleaseSemanticFacts Failed(ReleaseMemberKey member, string code, ReleaseSemanticStatus status = ReleaseSemanticStatus.Invalid) =>
        new(status, null, null, false, diagnostics: [new(code, member)]);
}
