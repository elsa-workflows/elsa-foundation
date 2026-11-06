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
                var decoded = ReadWorkflow(member.Key, content);
                if (decoded.Executable.Identity.ArtifactId != member.Key.Identity || !decoded.Executable.NodesById.TryGetValue(site.Node, out var node))
                    throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_member_mismatch");
                return new(member, decoded.Scope, node, decoded.Executable, null);
            }
            if (member.Codec == new ReleaseCodecKey(ReleaseRuntimeCodecs.TemplateKind, ReleaseRuntimeCodecs.Template, 1))
            {
                var decoded = ReadTemplate(member.Key, content);
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
            var decoded = ReadModelMember(member.Key, content);
            if (decoded.Model.Identity.ArtifactId != member.Key.Identity || site.Site != new ReleaseSiteKey(ReleaseRuntimeSiteKinds.ModelProtocol,
                decoded.Model.Protocol.Key, decoded.Model.Protocol.Version) || site.Node != "$model" || site.Origin.Count != 0 || site.Input is not null)
                throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_member_mismatch");
            return decoded;
        }
        catch (ReleaseRuntimeArtifactException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { throw ReleaseRuntimeArtifactCodec.Refuse("invalid_runtime_site"); }
    }

    public ReleaseRuntimeWorkflow ReadWorkflow(ReleaseMemberKey member, IReleaseContentReader content) => Boundary(() =>
    {
        var captured = Require(member, content, ReleaseRuntimeCodecs.WorkflowKind, ReleaseRuntimeCodecs.Workflow);
        var value = cache.Workflow(content, member);
        if (value.Executable.Identity.ArtifactId != member.Identity || captured.OwnerVersion != new ReleaseOwnerVersion("semver", value.Executable.Identity.ArtifactVersion, null))
            throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_member_mismatch");
        return value;
    });
    public ReleaseRuntimeModel ReadModelMember(ReleaseMemberKey member, IReleaseContentReader content) => Boundary(() =>
    {
        var captured = Require(member, content, ReleaseRuntimeCodecs.ModelKind, ReleaseRuntimeCodecs.Model);
        var value = cache.Model(content, member);
        if (value.Model.Identity.ArtifactId != member.Identity || captured.OwnerVersion != new ReleaseOwnerVersion("opaque", value.Model.Identity.ArtifactVersion, null))
            throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_member_mismatch");
        return value;
    });
    public ReleaseRuntimeTemplate ReadTemplate(ReleaseMemberKey member, IReleaseContentReader content) => Boundary(() =>
    {
        var captured = Require(member, content, ReleaseRuntimeCodecs.TemplateKind, ReleaseRuntimeCodecs.Template);
        var value = cache.Template(content, member);
        if (value.Template.TemplateId != member.Identity || captured.OwnerVersion is not null) throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_member_mismatch");
        return value;
    });
    public ReleaseActivityVersion ReadActivityVersion(ReleaseMemberKey member, IReleaseContentReader content) => Boundary(() =>
    {
        var captured = Require(member, content, ReleaseRuntimeCodecs.ActivityVersionKind, ReleaseRuntimeCodecs.ActivityVersion);
        var value = cache.ActivityVersion(content, member);
        if (value.VersionId != member.Identity || captured.OwnerVersion != new ReleaseOwnerVersion("semver", value.Version, null))
            throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_member_mismatch");
        return value;
    });
    private ReleaseContentMember Require(ReleaseMemberKey member, IReleaseContentReader content, string kind, string codec)
    {
        var captured = cache.Member(content, member);
        if (member.Kind != kind || captured.Codec != new ReleaseCodecKey(kind, codec, ReleaseRuntimeCodecs.Version))
            throw ReleaseRuntimeArtifactCodec.Refuse("runtime_site_kind_unsupported");
        return captured;
    }
    private static T Boundary<T>(Func<T> read)
    {
        try { return read(); }
        catch (ReleaseRuntimeArtifactException) { throw; }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { throw ReleaseRuntimeArtifactCodec.Refuse("invalid_runtime_captured_member"); }
    }
}
