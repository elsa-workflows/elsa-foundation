using System.Text.Json;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases.Tests;

/// <summary>Controlled generic facts for framework tests; these are not native/BPMN/ETL owner adapters.</summary>
internal sealed class ReleaseSemanticFixture
{
    public ReleaseBundleFixture Bundle { get; }
    public Dictionary<ReleaseMemberKey, ReleaseSemanticFacts> Facts { get; } = [];
    public ControlledArtifactVerifier Adapter { get; }
    public ReleaseSemanticFixture(int members = 2)
    {
        Bundle = new(members);
        var captured = Bundle.Validate();
        var edges = captured.Manifest.GetProperty("edges").EnumerateArray().Select(x => new ReleaseSemanticEdge(Key(x.GetProperty("from")), Key(x.GetProperty("to")),
            x.GetProperty("relation").GetString() == "dependency" ? ReleaseEdgeRelation.Dependency : ReleaseEdgeRelation.Association, x.GetProperty("occurrence").GetString()!)).ToArray();
        foreach (var member in captured.Members)
        {
            var key = new ReleaseMemberKey(member.Kind, member.Identity);
            var item = captured.Manifest.GetProperty("members").EnumerateArray().Single(x => Key(x.GetProperty("key")) == key);
            var version = item.GetProperty("ownerVersion");
            var owner = version.GetProperty("scheme").GetString() == "integer" ? new ReleaseOwnerVersion("integer", null, version.GetProperty("value").GetInt64())
                : new ReleaseOwnerVersion(version.GetProperty("scheme").GetString()!, version.GetProperty("value").GetString(), null);
            Facts.Add(key, new(ReleaseSemanticStatus.Verified, key, owner, true, edges.Where(x => x.From == key).ToArray(),
                rootCapabilities: key == Key(captured.Manifest.GetProperty("roots")[0].GetProperty("member")) ? [new(key, ReleaseRootAction.ActivateServing)] : []));
        }
        Adapter = new(captured.Members.Select(x => new ReleaseCodecKey(x.Kind, x.Codec, x.CodecVersion)).Distinct().ToArray(),
            (member, _, _, _) => ValueTask.FromResult(Facts[member.Key]));
    }
    public ReleaseMemberKey First => new(Bundle.Manifest["roots"]![0]!["member"]!["kind"]!.GetValue<string>(), Bundle.Manifest["roots"]![0]!["member"]!["identity"]!.GetValue<string>());
    public ReleaseMemberKey Last => Facts.Keys.LastOrDefault(x => x != First) ?? First;
    public ReleaseSemanticVerifier Verifier(params IReleaseOccurrenceInspector[] inspectors) => new(new ReleaseBundleCodec(), new([Adapter], inspectors));
    public static ReleaseMemberKey Key(JsonElement value) => new(value.GetProperty("kind").GetString()!, value.GetProperty("identity").GetString()!);
    public void Set(ReleaseMemberKey key, ReleaseSemanticStatus? status = null, ReleaseMemberKey? identity = null, ReleaseOwnerVersion? ownerVersion = null,
        bool? hash = null, IReadOnlyList<ReleaseSemanticEdge>? edges = null, IReadOnlyList<ReleaseResourceRequirement>? resources = null,
        IReadOnlyList<ReleaseSemanticPrerequisite>? prerequisites = null, IReadOnlyList<ReleaseSemanticSite>? sites = null, IReadOnlyList<ReleaseSemanticDiagnostic>? diagnostics = null,
        IReadOnlyList<ReleaseRootCapability>? rootCapabilities = null)
    {
        var old = Facts[key]; Facts[key] = new(status ?? old.Status, identity ?? old.Identity, ownerVersion ?? old.OwnerVersion, hash ?? old.OwnerHashVerified,
            edges ?? old.Edges, resources ?? old.Resources, prerequisites ?? old.Prerequisites, sites ?? old.Sites, diagnostics ?? old.Diagnostics, rootCapabilities ?? old.RootCapabilities);
    }
}

internal sealed class ControlledArtifactVerifier(IReadOnlyCollection<ReleaseCodecKey> claims,
    Func<ReleaseContentMember, ReadOnlyMemory<byte>, IReleaseContentReader, CancellationToken, ValueTask<ReleaseSemanticFacts>> callback) : IReleaseArtifactSemanticVerifier
{
    public ReleaseSemanticAdapterIdentity Identity { get; set; } = new("controlled-generic-artifact", "1");
    public IReadOnlyCollection<ReleaseCodecKey> SupportedCodecs { get; } = claims;
    public ValueTask<ReleaseSemanticFacts> VerifyAsync(ReleaseContentMember member, ReadOnlyMemory<byte> payload, IReleaseContentReader content, CancellationToken cancellationToken = default)
        => callback(member, payload, content, cancellationToken);
}

internal sealed class ControlledOccurrenceInspector(IReadOnlyCollection<ReleaseSiteKey> claims,
    Func<ReleaseSemanticSite, IReleaseContentReader, CancellationToken, ValueTask<ReleaseOccurrenceFacts>> callback) : IReleaseOccurrenceInspector
{
    public ReleaseSemanticAdapterIdentity Identity { get; set; } = new("controlled-generic-occurrence", "1");
    public IReadOnlyCollection<ReleaseSiteKey> SupportedSites { get; } = claims;
    public ValueTask<ReleaseOccurrenceFacts> InspectAsync(ReleaseSemanticSite site, IReleaseContentReader content, CancellationToken cancellationToken = default)
        => callback(site, content, cancellationToken);
}
