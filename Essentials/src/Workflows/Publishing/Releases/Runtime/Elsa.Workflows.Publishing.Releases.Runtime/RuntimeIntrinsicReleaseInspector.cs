using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

/// <summary>Runtime-owned representation rules; the mandatory activity owner independently checks its actual capabilities.</summary>
internal sealed class RuntimeIntrinsicReleaseInspector(IReleaseRuntimeSiteReader sites) : IReleaseOccurrenceInspector
{
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.runtime-intrinsic-release-profile", "1");
    public IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; } = Array.AsReadOnly(new[]
    {
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Metadata, RuntimeMetadataKeys.StructuralFaultHandling, 1),
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Metadata, RuntimeMetadataKeys.AcceptsRescheduledFaultedChild, 1),
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.InputMetadata, "referenceKey", 1),
        new ReleaseSiteKey(ReleaseRuntimeSiteKinds.Compatibility, "artifact", 1)
    });

    public ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!SupportedSites.Contains(site.Site)) return ValueTask.FromResult(Failed("runtime_intrinsic_site_unsupported", ReleaseSemanticStatus.Unsupported));
            var context = sites.ReadNode(site, content);
            if (site.Site.SiteKind == ReleaseRuntimeSiteKinds.Compatibility)
            {
                var compatibility = context.Workflow?.CompatibilityMetadata ?? context.Template!.CompatibilityMetadata;
                var root = context.Workflow?.RootActivity ?? context.Template!.Root;
                if (site.Input is not null || context.Node.ExecutableNodeId != root.ExecutableNodeId) return ValueTask.FromResult(Failed("invalid_runtime_compatibility_site"));
                return ValueTask.FromResult(compatibility.Count == 1 && compatibility.TryGetValue("slice", out var slice) && slice == "workflow-execution-vertical-slice"
                    ? new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified) : Failed("runtime_compatibility_unsupported", ReleaseSemanticStatus.Unsupported));
            }
            if (site.Site.SiteKind == ReleaseRuntimeSiteKinds.InputMetadata)
            {
                if (site.Input is null || !context.Node.InputBindings.TryGetValue(site.Input, out var binding) ||
                    !binding.Metadata.TryGetValue("referenceKey", out var reference) || reference != binding.InputName)
                    return ValueTask.FromResult(Failed("runtime_input_reference_alias_mismatch"));
                return ValueTask.FromResult(new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified));
            }
            if (site.Input is not null || context.Node.IntrinsicKind is not null || !context.Node.Metadata.TryGetValue(site.Site.Discriminator, out var value))
                return ValueTask.FromResult(Failed("runtime_structural_capability_mismatch"));
            var valid = site.Site.Discriminator switch
            {
                RuntimeMetadataKeys.StructuralFaultHandling => value is RuntimeMetadataKeys.StructuralFaultHandlingChild or RuntimeMetadataKeys.StructuralFaultHandlingDescendant,
                RuntimeMetadataKeys.AcceptsRescheduledFaultedChild => value == "true",
                _ => false
            };
            // These flags do not prove the class implements the capability. Its exact descriptor/CLR owner site is
            // mandatory too and must reject hints that disagree with the actual owner type (including leaf types).
            return ValueTask.FromResult(valid ? new ReleaseOccurrenceFacts(ReleaseSemanticStatus.Verified) : Failed("invalid_runtime_structural_capability"));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is ReleaseRuntimeArtifactException or ArgumentException or InvalidOperationException or KeyNotFoundException)
        { return ValueTask.FromResult(Failed("invalid_runtime_intrinsic_site")); }

        ReleaseOccurrenceFacts Failed(string code, ReleaseSemanticStatus status = ReleaseSemanticStatus.Invalid) =>
            new(status, diagnostics: [new(code, site.Member, site.Origin, site.Input)]);
    }
}
