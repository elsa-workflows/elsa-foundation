using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime;

internal sealed class ReleaseRuntimeSiteReader(RuntimeCapturedContentCache cache) : IReleaseRuntimeSiteReader
{
    public ReleaseRuntimeNodeContext ReadNode(ReleaseSemanticSite site, IReleaseContentReader content)
    {
        try
        {
            if (site.Payload.ValueKind != System.Text.Json.JsonValueKind.Object || site.Payload.EnumerateObject().Count() != 1 ||
                site.Payload.GetProperty("nodeId").GetString() != site.Node) throw ReleaseRuntimeArtifactCodec.Refuse("invalid_runtime_site");
            var member = cache.Member(content, site.Member);
            if (member.Codec == new ReleaseCodecKey(ReleaseRuntimeCodecs.WorkflowKind, ReleaseRuntimeCodecs.Workflow, 1))
            {
                var decoded = cache.Workflow(content, member.Key);
                if (decoded.Executable.Identity.ArtifactId != member.Key.Identity || !decoded.Executable.NodesById.TryGetValue(site.Node, out var node))
                    throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_member_mismatch");
                return new(member, decoded.Scope, node, decoded.Executable, null);
            }
            if (member.Codec == new ReleaseCodecKey(ReleaseRuntimeCodecs.TemplateKind, ReleaseRuntimeCodecs.Template, 1))
            {
                var decoded = cache.Template(content, member.Key);
                if (decoded.Template.TemplateId != member.Key.Identity || !decoded.Template.NodesById.TryGetValue(site.Node, out var node))
                    throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_member_mismatch");
                return new(member, decoded.Scope, node, null, decoded.Template);
            }
            throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_kind_unsupported");
        }
        catch (ReleaseRuntimeArtifactException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { throw ReleaseRuntimeArtifactCodec.Refuse("invalid_runtime_site"); }
    }

    public ReleaseRuntimeModel ReadModel(ReleaseSemanticSite site, IReleaseContentReader content)
    {
        try
        {
            var member = cache.Member(content, site.Member);
            if (member.Codec != new ReleaseCodecKey(ReleaseRuntimeCodecs.ModelKind, ReleaseRuntimeCodecs.Model, 1))
                throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_kind_unsupported");
            var decoded = cache.Model(content, member.Key);
            if (decoded.Model.Identity.ArtifactId != member.Key.Identity || site.Site != new ReleaseSiteKey(ReleaseRuntimeSiteKinds.ModelProtocol,
                decoded.Model.Protocol.Key, decoded.Model.Protocol.Version) || site.Node != "$model" || site.Origin.Count != 0 || site.Input is not null)
                throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_member_mismatch");
            return decoded;
        }
        catch (ReleaseRuntimeArtifactException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { throw ReleaseRuntimeArtifactCodec.Refuse("invalid_runtime_site"); }
    }
}
