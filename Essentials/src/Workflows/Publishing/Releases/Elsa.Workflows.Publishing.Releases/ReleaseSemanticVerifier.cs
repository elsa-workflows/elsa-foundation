using System.Text.Json;
using Elsa.Workflows.Publishing.Releases.Core;

namespace Elsa.Workflows.Publishing.Releases;

internal sealed class ReleaseSemanticVerifier(IReleaseBundleCodec codec, ReleaseSemanticRegistry registry) : IReleaseSemanticVerifier
{
    public async ValueTask<ReleaseSemanticVerificationResult> VerifyAsync(IReleaseBundleTransport bundle,
        ReleaseSemanticVerificationOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var settings = options ?? new();
        if (settings.MaxSites <= 0 || settings.MaxDiagnostics <= 0 || settings.MaxInspectionBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        cancellationToken.ThrowIfCancellationRequested();
        IReleaseBundleTransport captured;
        try
        {
            var limits = settings.TransportLimits ?? new();
            if (bundle.Members.Count > limits.MaxMembers) return Refused("invalid_transport");
            if (!FitsInspectionBudget(bundle.Members, settings.MaxInspectionBytes, limits.MaxMembers))
                return Refused("semantic_payload_work_limit_exceeded");
            var blobs = new Dictionary<string, ReadOnlyMemory<byte>>(StringComparer.Ordinal);
            foreach (var member in bundle.Members)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!blobs.ContainsKey(member.PayloadDigest)) blobs.Add(member.PayloadDigest, bundle.GetBlobBytes(member.PayloadDigest));
            }
            captured = codec.Validate(bundle.GetManifestBytes(), blobs, settings.TransportLimits);
            if (captured.Digest != bundle.Digest) return Refused("transport_digest_mismatch");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        { return Refused("invalid_transport"); }
        if (!FitsInspectionBudget(captured.Members, settings.MaxInspectionBytes, (settings.TransportLimits ?? new()).MaxMembers))
            return Refused("semantic_payload_work_limit_exceeded", captured.Digest);
        if (settings.ExpectedDigest is not null)
        {
            try { ReleaseManifest.CheckDigest(settings.ExpectedDigest); }
            catch (ReleaseBundleFormatException) { return Refused("invalid_expected_digest", captured.Digest); }
            if (settings.ExpectedDigest != captured.Digest) return Refused("expected_digest_mismatch", captured.Digest);
        }

        return await InspectAsync(new CapturedContentReader(captured), captured.Manifest, captured.Digest, settings, true, cancellationToken);
    }

    internal async ValueTask<ReleaseSemanticVerificationResult> InspectAsync(IReleaseContentReader content, JsonElement manifest,
        string? digest, ReleaseSemanticVerificationOptions settings, bool compareDeclarations, CancellationToken cancellationToken)
    {
        var facts = new ActualFacts(content, settings);
        var capabilities = new List<ReleaseSemanticAdapterCapability>();
        var sites = new List<ReleaseSemanticSite>();
        foreach (var member in content.Members)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!registry.Artifacts.TryGetValue(member.Codec, out var registration))
            { facts.Refuse(ReleaseSemanticStatus.Unsupported, "unsupported_artifact_codec", member.Key); continue; }
            capabilities.Add(registration.Capability);
            ReleaseSemanticFacts answer;
            try { answer = await registration.Verifier.VerifyAsync(member, content.GetPayload(member.Key), content, cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { facts.Refuse(ReleaseSemanticStatus.Incomplete, "artifact_verifier_failed", member.Key); continue; }
            cancellationToken.ThrowIfCancellationRequested();
            if (answer is null) { facts.Refuse(ReleaseSemanticStatus.Invalid, "invalid_owner_facts", member.Key); continue; }
            facts.Roots(member.Key, answer.RootCapabilities);
            if (!facts.Answer(answer.Status, answer.Diagnostics, member.Key)) continue;
            if (answer.Identity != member.Key || answer.OwnerVersion != member.OwnerVersion || !answer.OwnerHashVerified)
            { facts.Refuse(ReleaseSemanticStatus.Invalid, "artifact_identity_version_or_hash_mismatch", member.Key); continue; }
            facts.VerifiedMembers.Add(member.Key);
            facts.Add(member.Key, answer.Edges, answer.Resources, answer.Prerequisites);
            foreach (var site in answer.Sites)
            {
                if (sites.Count >= settings.MaxSites)
                { facts.Refuse(ReleaseSemanticStatus.Invalid, "semantic_site_limit_exceeded", member.Key); break; }
                if (!facts.Site(site, member.Key)) continue;
                sites.Add(site);
            }
        }
        foreach (var site in sites)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!registry.Sites.TryGetValue(site.Site, out var registration))
            { facts.Refuse(ReleaseSemanticStatus.Unsupported, "unsupported_occurrence_site", site.Member, site.Origin, site.Input); continue; }
            capabilities.Add(registration.Capability);
            ReleaseOccurrenceFacts answer;
            try { answer = await registration.Inspector.InspectAsync(site, content, cancellationToken); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { facts.Refuse(ReleaseSemanticStatus.Incomplete, "occurrence_inspector_failed", site.Member, site.Origin, site.Input); continue; }
            cancellationToken.ThrowIfCancellationRequested();
            if (answer is not null && facts.Answer(answer.Status, answer.Diagnostics, site.Member, site))
                facts.Add(site.Member, answer.Edges, answer.Resources, answer.Prerequisites, site.Origin);
            else if (answer is null) facts.Refuse(ReleaseSemanticStatus.Invalid, "invalid_owner_facts", site.Member);
        }
        facts.Finish(manifest, compareDeclarations);
        cancellationToken.ThrowIfCancellationRequested();
        return new(facts.Status, digest, facts.Edges.Values.ToArray(), facts.Resources.Values.ToArray(),
            facts.Prerequisites.Values.ToArray(), capabilities.Distinct().OrderBy(x => x.Identity.Name, StringComparer.Ordinal)
                .ThenBy(x => x.Identity.Version, StringComparer.Ordinal).ToArray(), facts.Diagnostics);
    }

    private static ReleaseSemanticVerificationResult Refused(string code, string? digest = null) =>
        new(ReleaseSemanticStatus.Invalid, digest, [], [], [], [], [new(code)]);

    private static bool FitsInspectionBudget(IReadOnlyList<ReleaseBundleMember> members, long maximum, int maximumMembers)
    {
        if (members.Count > maximumMembers) return false;
        long inspected = 0; var count = 0;
        foreach (var member in members)
        {
            if (++count > maximumMembers || member is null || member.ByteLength <= 0 || member.ByteLength > maximum - inspected) return false;
            inspected += member.ByteLength;
        }
        return count == members.Count;
    }

    private sealed class CapturedContentReader : IReleaseContentReader
    {
        private readonly IReleaseBundleTransport _bundle;
        private readonly IReadOnlyDictionary<ReleaseMemberKey, ReleaseContentMember> _index;
        public IReadOnlyList<ReleaseContentMember> Members { get; }
        public CapturedContentReader(IReleaseBundleTransport bundle)
        {
            _bundle = bundle;
            var versions = bundle.Manifest.GetProperty("members").EnumerateArray().ToDictionary(x => Key(x.GetProperty("key")), x => Version(x.GetProperty("ownerVersion")));
            var members = bundle.Members.Select(x => new ReleaseContentMember(x, versions[new(x.Kind, x.Identity)])).ToArray();
            Members = Array.AsReadOnly(members); _index = members.ToDictionary(x => x.Key);
        }
        public ReleaseContentMember GetMember(ReleaseMemberKey key) => _index.TryGetValue(key, out var member)
            ? member : throw new KeyNotFoundException("The requested member is not in captured release content.");
        public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => _bundle.GetBlobBytes(GetMember(key).Descriptor.PayloadDigest);
    }

    private sealed class ActualFacts(IReleaseContentReader content, ReleaseSemanticVerificationOptions options)
    {
        private readonly HashSet<ReleaseMemberKey> _members = content.Members.Select(x => x.Key).ToHashSet();
        private readonly HashSet<string> _sites = new(StringComparer.Ordinal);
        private readonly HashSet<string> _resourceKeys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _prerequisiteKeys = new(StringComparer.Ordinal);
        private readonly Dictionary<ReleaseMemberKey, HashSet<ReleaseRootCapability>> _roots = [];
        private int _rootCapabilityCount;
        private readonly int _maxFacts = (options.TransportLimits ?? new()).MaxEdges;
        private readonly int _maxDepth = (options.TransportLimits ?? new()).MaxDependencyDepth;
        private readonly int _maxTextLength = (options.TransportLimits ?? new()).MaxManifestBytes;
        private readonly List<ReleaseSemanticDiagnostic> _diagnostics = [];
        public ReleaseSemanticStatus Status { get; private set; } = ReleaseSemanticStatus.Verified;
        public SortedDictionary<string, ReleaseSemanticEdge> Edges { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, ReleaseResourceRequirement> Resources { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, ReleaseSemanticPrerequisite> Prerequisites { get; } = new(StringComparer.Ordinal);
        public IReadOnlyList<ReleaseSemanticDiagnostic> Diagnostics => _diagnostics.AsReadOnly();
        public HashSet<ReleaseMemberKey> VerifiedMembers { get; } = [];

        public void Roots(ReleaseMemberKey member, IReadOnlyList<ReleaseRootCapability> capabilities)
        {
            if (capabilities.Count > _maxFacts - _rootCapabilityCount)
            { Refuse(ReleaseSemanticStatus.Invalid, "root_capability_limit_exceeded", member); return; }
            _rootCapabilityCount += capabilities.Count;
            var allowed = new HashSet<ReleaseRootCapability>(); _roots.Add(member, allowed);
            foreach (var capability in capabilities)
            {
                if (capability is null || capability.Content is null || !_members.Contains(capability.Content))
                { Refuse(ReleaseSemanticStatus.Invalid, "root_capability_content_missing", member); continue; }
                if (capability.Action is not (ReleaseRootAction.ActivateServing or ReleaseRootAction.MakeAvailable))
                { Refuse(ReleaseSemanticStatus.Invalid, "invalid_root_capability", member); continue; }
                if (!allowed.Add(capability)) Refuse(ReleaseSemanticStatus.Invalid, "duplicate_root_capability", member);
            }
        }

        public void Refuse(ReleaseSemanticStatus status, string code, ReleaseMemberKey? member = null, IReadOnlyList<string>? origin = null, string? input = null)
        {
            Status = Combine(Status, status);
            if (_diagnostics.Count < options.MaxDiagnostics) _diagnostics.Add(new(code, member,
                origin is not null && Location(origin) ? origin : [], input is not null && Text(input) ? input : null));
        }

        public bool Answer(ReleaseSemanticStatus status, IReadOnlyList<ReleaseSemanticDiagnostic> diagnostics, ReleaseMemberKey member, ReleaseSemanticSite? site = null)
        {
            if (!Enum.IsDefined(status) || diagnostics.Count > options.MaxDiagnostics)
            { Refuse(ReleaseSemanticStatus.Invalid, "invalid_owner_facts", member); return false; }
            var valid = true;
            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic is null || !Code(diagnostic.Code) || diagnostic.Member is not null && diagnostic.Member != member ||
                    !Location(diagnostic.Origin) || diagnostic.Input is not null && !Text(diagnostic.Input) ||
                    site is not null && diagnostic.Origin.Count != 0 && !Within(diagnostic.Origin, site.Origin) ||
                    site?.Input is not null && diagnostic.Input is not null && site.Input != diagnostic.Input)
                { Refuse(ReleaseSemanticStatus.Invalid, "invalid_owner_facts", member); valid = false; }
                else if (_diagnostics.Count < options.MaxDiagnostics) _diagnostics.Add(new(diagnostic.Code, diagnostic.Member ?? member,
                    diagnostic.Origin.Count == 0 && site is not null ? site.Origin : diagnostic.Origin, diagnostic.Input ?? site?.Input));
            }
            Status = Combine(Status, status);
            if (status == ReleaseSemanticStatus.Verified && diagnostics.Count != 0)
            { Refuse(ReleaseSemanticStatus.Invalid, "invalid_owner_facts", member); return false; }
            if (status != ReleaseSemanticStatus.Verified && diagnostics.Count == 0) Refuse(status, "owner_inspection_not_verified", member);
            return valid && status == ReleaseSemanticStatus.Verified;
        }

        public bool Site(ReleaseSemanticSite? site, ReleaseMemberKey member)
        {
            if (site is null || site.Member != member || !Location(site.Origin) || !Text(site.Node) || site.Input is not null && !Text(site.Input) ||
                site.Site is null || !Text(site.Site.SiteKind) || !Text(site.Site.Discriminator) || site.Site.SchemaVersion <= 0 || site.Payload.ValueKind == JsonValueKind.Undefined)
            { Refuse(ReleaseSemanticStatus.Invalid, "invalid_owner_facts", member); return false; }
            if (!_sites.Add(Token(member.Kind, member.Identity, site.Origin, site.Node, site.Input, site.Site.SiteKind, site.Site.Discriminator, site.Site.SchemaVersion)))
            { Refuse(ReleaseSemanticStatus.Invalid, "duplicate_occurrence_site", member); return false; }
            return true;
        }

        public void Add(ReleaseMemberKey member, IReadOnlyList<ReleaseSemanticEdge> edges, IReadOnlyList<ReleaseResourceRequirement> resources,
            IReadOnlyList<ReleaseSemanticPrerequisite> prerequisites, IReadOnlyList<string>? scope = null)
        {
            if (edges.Count > _maxFacts - Edges.Count || resources.Count > _maxFacts - Resources.Count || prerequisites.Count > _maxFacts - Prerequisites.Count)
            { Refuse(ReleaseSemanticStatus.Invalid, "semantic_fact_limit_exceeded", member); return; }
            foreach (var edge in edges)
            {
                if (edge is null || edge.From != member || edge.To is null || !Text(edge.To.Kind) || !Text(edge.To.Identity) ||
                    !Enum.IsDefined(edge.Relation) || edge.Occurrence is null || edge.Occurrence.Length > _maxTextLength || !WellFormed(edge.Occurrence))
                { Refuse(ReleaseSemanticStatus.Invalid, "invalid_owner_facts", member); continue; }
                if (!_members.Contains(edge.To)) Refuse(ReleaseSemanticStatus.Invalid, "actual_missing_member", member);
                if (!Edges.TryAdd(EdgeToken(edge), edge)) Refuse(ReleaseSemanticStatus.Invalid, "duplicate_actual_edge", member);
            }
            foreach (var resource in resources)
            {
                if (resource is null || resource.Member != member || !Location(resource.Origin) || scope is not null && !Within(resource.Origin, scope) ||
                    !Text(resource.Reference) || !Text(resource.Kind) || !Text(resource.SchemaVersion))
                { Refuse(ReleaseSemanticStatus.Invalid, "invalid_owner_facts", member); continue; }
                if (!_resourceKeys.Add(Token(resource.Member.Kind, resource.Member.Identity, resource.Origin, resource.Reference, resource.Kind)))
                { Refuse(ReleaseSemanticStatus.Invalid, "duplicate_actual_resource", member); continue; }
                if (!Resources.TryAdd(ResourceToken(resource), resource)) Refuse(ReleaseSemanticStatus.Invalid, "duplicate_actual_resource", member);
            }
            foreach (var prerequisite in prerequisites)
            {
                if (prerequisite is null || prerequisite.Member != member || !Text(prerequisite.Key) || !Text(prerequisite.Kind) || !Text(prerequisite.Identity) ||
                    prerequisite.Scheme is not ("exact" or "semver-range" or "integer-range" or "opaque") || !Text(prerequisite.Constraint))
                { Refuse(ReleaseSemanticStatus.Invalid, "invalid_owner_facts", member); continue; }
                if (!_prerequisiteKeys.Add(prerequisite.Key))
                { Refuse(ReleaseSemanticStatus.Invalid, "duplicate_actual_prerequisite", member); continue; }
                if (!Prerequisites.TryAdd(PrerequisiteToken(prerequisite), prerequisite)) Refuse(ReleaseSemanticStatus.Invalid, "duplicate_actual_prerequisite", member);
            }
        }

        public void Finish(JsonElement manifest, bool compareDeclarations)
        {
            var complete = Status == ReleaseSemanticStatus.Verified;
            foreach (var root in manifest.GetProperty("roots").EnumerateArray())
            {
                var selected = Key(root.GetProperty("member"));
                if (!VerifiedMembers.Contains(selected)) continue;
                var capability = new ReleaseRootCapability(Key(root.GetProperty("content")), root.GetProperty("action").GetString() == "activate-serving"
                    ? ReleaseRootAction.ActivateServing : ReleaseRootAction.MakeAvailable);
                if (!_roots.TryGetValue(selected, out var allowed) || !allowed.Contains(capability))
                    Refuse(ReleaseSemanticStatus.Invalid, "root_content_or_action_not_allowed", selected);
            }
            Graph(manifest, complete);
            if (!complete || !compareDeclarations) return;
            var edges = manifest.GetProperty("edges").EnumerateArray().Select(x => new ReleaseSemanticEdge(Key(x.GetProperty("from")), Key(x.GetProperty("to")),
                x.GetProperty("relation").GetString() == "dependency" ? ReleaseEdgeRelation.Dependency : ReleaseEdgeRelation.Association, x.GetProperty("occurrence").GetString()!));
            Compare(edges.ToDictionary(EdgeToken), Edges, "edge");
            var resources = manifest.GetProperty("resourceRequirements").EnumerateArray().Select(x =>
            {
                var key = x.GetProperty("key");
                return new ReleaseResourceRequirement(Key(key.GetProperty("member")), key.GetProperty("origin").EnumerateArray().Select(y => y.GetString()!).ToArray(),
                    key.GetProperty("reference").GetString()!, key.GetProperty("kind").GetString()!, x.GetProperty("schemaVersion").GetString()!);
            });
            Compare(resources.ToDictionary(ResourceToken), Resources, "resource");
            var prerequisites = manifest.GetProperty("prerequisites").EnumerateArray().Select(x => new ReleaseSemanticPrerequisite(x.GetProperty("key").GetString()!,
                Key(x.GetProperty("member")), x.GetProperty("kind").GetString()!, x.GetProperty("identity").GetString()!, x.GetProperty("scheme").GetString()!, x.GetProperty("constraint").GetString()!));
            Compare(prerequisites.ToDictionary(PrerequisiteToken), Prerequisites, "prerequisite");
        }

        private void Compare<T>(IReadOnlyDictionary<string, T> declared, IDictionary<string, T> actual, string kind)
        {
            foreach (var key in declared.Keys) if (!actual.ContainsKey(key)) Refuse(ReleaseSemanticStatus.Invalid, "declared_" + kind + "_not_observed");
            foreach (var key in actual.Keys) if (!declared.ContainsKey(key)) Refuse(ReleaseSemanticStatus.Invalid, "undeclared_" + kind);
        }

        private void Graph(JsonElement manifest, bool complete)
        {
            var edges = Edges.Values.Where(x => _members.Contains(x.To)).ToArray();
            if (complete)
            {
                var roots = manifest.GetProperty("roots").EnumerateArray().Select(x => Key(x.GetProperty("member")));
                var reachable = new HashSet<ReleaseMemberKey>(); var pending = new Stack<ReleaseMemberKey>(roots);
                var adjacency = edges.GroupBy(x => x.From).ToDictionary(x => x.Key, x => x.Select(y => y.To).ToArray());
                while (pending.TryPop(out var key))
                {
                    if (!reachable.Add(key)) continue;
                    if (adjacency.TryGetValue(key, out var next)) foreach (var child in next) pending.Push(child);
                }
                if (reachable.Count != _members.Count) Refuse(ReleaseSemanticStatus.Invalid, "actual_unrelated_member");
            }
            var dependencies = edges.Where(x => x.Relation == ReleaseEdgeRelation.Dependency).Select(x => (x.From, x.To)).Distinct().ToArray();
            var incoming = _members.ToDictionary(x => x, _ => 0); var depth = _members.ToDictionary(x => x, _ => 0);
            var children = dependencies.GroupBy(x => x.From).ToDictionary(x => x.Key, x => x.Select(y => y.To).ToArray());
            foreach (var edge in dependencies) incoming[edge.To]++;
            var queue = new Queue<ReleaseMemberKey>(incoming.Where(x => x.Value == 0).Select(x => x.Key)); var visited = 0;
            while (queue.TryDequeue(out var key))
            {
                visited++;
                if (!children.TryGetValue(key, out var next)) continue;
                foreach (var child in next)
                {
                    depth[child] = Math.Max(depth[child], depth[key] + 1);
                    if (--incoming[child] == 0) queue.Enqueue(child);
                }
            }
            if (visited != _members.Count) Refuse(ReleaseSemanticStatus.Invalid, "actual_dependency_cycle");
            if (depth.Values.Any(x => x > _maxDepth)) Refuse(ReleaseSemanticStatus.Invalid, "actual_dependency_depth_exceeded");
        }

        private bool Location(IReadOnlyList<string> origin) => origin.Count <= _maxDepth && origin.All(Text);
        private static bool Within(IReadOnlyList<string> origin, IReadOnlyList<string> scope) => origin.Count >= scope.Count && origin.Take(scope.Count).SequenceEqual(scope, StringComparer.Ordinal);
        private static bool Code(string? value) => !string.IsNullOrEmpty(value) && value.Length <= 80 && value.All(x => x is >= 'a' and <= 'z' or >= '0' and <= '9' or '_');
        private bool Text(string? value) => !string.IsNullOrEmpty(value) && value.Length <= _maxTextLength && WellFormed(value);
        private static bool WellFormed(string value)
        {
            for (var index = 0; index < value.Length; index++)
                if (char.IsHighSurrogate(value[index]))
                { if (++index >= value.Length || !char.IsLowSurrogate(value[index])) return false; }
                else if (char.IsLowSurrogate(value[index])) return false;
            return true;
        }
    }

    private static ReleaseSemanticStatus Combine(ReleaseSemanticStatus left, ReleaseSemanticStatus right)
    {
        if (left == ReleaseSemanticStatus.Invalid || right == ReleaseSemanticStatus.Invalid) return ReleaseSemanticStatus.Invalid;
        if (left == ReleaseSemanticStatus.Unsupported || right == ReleaseSemanticStatus.Unsupported) return ReleaseSemanticStatus.Unsupported;
        if (left == ReleaseSemanticStatus.Incomplete || right == ReleaseSemanticStatus.Incomplete) return ReleaseSemanticStatus.Incomplete;
        return ReleaseSemanticStatus.Verified;
    }
    private static ReleaseMemberKey Key(JsonElement value) => new(value.GetProperty("kind").GetString()!, value.GetProperty("identity").GetString()!);
    private static ReleaseOwnerVersion? Version(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetProperty("scheme").GetString() == "integer"
        ? new("integer", null, value.GetProperty("value").GetInt64()) : new(value.GetProperty("scheme").GetString()!, value.GetProperty("value").GetString(), null);
    private static string Token(params object?[] values) => JsonSerializer.Serialize(values);
    private static string EdgeToken(ReleaseSemanticEdge edge) => Token(edge.From.Kind, edge.From.Identity, edge.Relation.ToString(), edge.Occurrence, edge.To.Kind, edge.To.Identity);
    private static string ResourceToken(ReleaseResourceRequirement requirement) => Token(requirement.Member.Kind, requirement.Member.Identity, requirement.Origin, requirement.Reference, requirement.Kind, requirement.SchemaVersion);
    private static string PrerequisiteToken(ReleaseSemanticPrerequisite requirement) => Token(requirement.Key, requirement.Member.Kind, requirement.Member.Identity, requirement.Kind, requirement.Identity, requirement.Scheme, requirement.Constraint);
}
