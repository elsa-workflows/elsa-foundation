using Elsa.Workflows.Publishing.Releases.Core;
using Elsa.Workflows.Publishing.Releases.Capture.Core;
using Elsa.Workflows.Publishing.Releases.Runtime.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Releases.Runtime.Source;

/// <summary>Source-side owner walk. The shared capture service owns bounded contributor fan-in and final verification.</summary>
internal sealed class RuntimeSourceMemberContributor(IWorkflowExecutableStore materials, IExecutableActivityTemplateReader templates,
    IWorkflowExecutableRootWriteLeaseManager leases, IReleaseRuntimeArtifactCodec codec, IReleaseRuntimeSourceContextAccessor source,
    IReleaseRuntimeSourceAuthorization authorization, IPersistenceAccessContextAccessor access) : IReleaseSourceMemberContributor
{
    public ReleaseSemanticAdapterIdentity Identity { get; } = new("elsa.runtime-source-closure", "1");
    public IReadOnlyCollection<ReleaseCodecKey> SupportedContainerCodecs { get; } = Array.AsReadOnly(new[]
    {
        new ReleaseCodecKey(ReleaseRuntimeCodecs.WorkflowKind, ReleaseRuntimeCodecs.Workflow, 1),
        new ReleaseCodecKey(ReleaseRuntimeCodecs.ModelKind, ReleaseRuntimeCodecs.Model, 1),
        new ReleaseCodecKey(ReleaseRuntimeCodecs.TemplateKind, ReleaseRuntimeCodecs.Template, 1)
    });
    public async ValueTask ExecuteAsync(IReadOnlyList<ReleaseContentMember> containers, IReleaseContentReader captured, ReleaseBundleLimits limits,
        Func<IReleaseContentReader, CancellationToken, ValueTask> capture, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(containers); ArgumentNullException.ThrowIfNull(captured); ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(limits);
        var scope = Context(); var partition = new PersistenceScope(scope.Partition!); var budget = new SourceBudget(limits);
        _ = budget.ReadLimits();
        var roots = new Dictionary<string, WorkflowExecutableIdentity>(StringComparer.Ordinal);
        var knownPins = new Dictionary<ReleaseMemberKey, WorkflowExecutableIdentity>();
        var knownTemplates = new Dictionary<ReleaseMemberKey, ExecutableActivityTemplate>();
        var pending = new Queue<Item>(); var seenContainers = new HashSet<ReleaseMemberKey>();
        foreach (var member in containers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seenContainers.Add(member.Key) || !SupportedContainerCodecs.Contains(member.Codec)) throw Refuse("runtime_source_container_invalid");
            budget.WorkMember(); var payload = captured.GetPayload(member.Key);
            if (member.Key.Kind == ReleaseRuntimeCodecs.WorkflowKind)
            {
                var item = codec.DecodeWorkflow(payload, limits); Scope(item.Scope); Root(item.Executable.Identity);
                knownPins.Add(member.Key, item.Executable.Identity);
                pending.Enqueue(new(member.Key, item.Executable, null, null));
            }
            else if (member.Key.Kind == ReleaseRuntimeCodecs.ModelKind)
            {
                var item = codec.DecodeModel(payload, limits); Scope(item.Scope); Root(item.Model.Identity);
                knownPins.Add(member.Key, item.Model.Identity);
                pending.Enqueue(new(member.Key, null, item.Model, null));
            }
            else
            {
                var item = codec.DecodeTemplate(payload, limits); Scope(item.Scope);
                knownTemplates.Add(member.Key, item.Template);
                pending.Enqueue(new(member.Key, null, null, item.Template));
            }
        }
        var additions = new RuntimeSourceCapturedMembers(limits);
        if (roots.Count == 0) await Expand(cancellationToken);
        else await leases.ExecuteFencedAsync(roots.Values.ToArray(), "release-source:" + Guid.NewGuid().ToString("N"), partition, budget.ReadLimits(),
            AuthorizeMaterial, async (_, token) =>
            {
                // The owner manager independently reloaded the closure. Compare each retained source container to
                // the actual captured source member before adding dependencies; no imported body becomes source origin.
                foreach (var root in roots.Values)
                {
                    var current = await ReadMaterial(root.ArtifactId, root.ArtifactHash, root, token);
                    var encoded = Encode(current.Material); var memberKey = new ReleaseMemberKey(encoded.Descriptor.Kind, encoded.Descriptor.Identity);
                    if (!captured.GetPayload(memberKey).Span.SequenceEqual(encoded.GetPayloadBytes())) throw Refuse("runtime_source_container_conflict");
                }
                await Expand(token);
            }, cancellationToken);

        async ValueTask Expand(CancellationToken token)
        {
            foreach (var (key, template) in knownTemplates.ToArray())
            {
                if (!seenContainers.Contains(key)) continue;
                if (!await authorization.AuthorizeTemplateAsync(scope, template.TemplateId, template.TemplateHash, token)) throw Refuse("runtime_source_export_forbidden");
                CheckContext();
                var actual = await templates.FindBoundedAsync(template.TemplateId, partition, budget.ReadLimits(), token) ?? throw Refuse("runtime_source_template_missing");
                budget.Read(actual.Scope, actual.StoredPayloadBytes, partition);
                var encoded = codec.EncodeTemplate(new(scope, actual.Template), limits);
                if (!captured.GetPayload(key).Span.SequenceEqual(encoded.GetPayloadBytes())) throw Refuse("runtime_source_container_conflict");
            }
            var expanded = new HashSet<ReleaseMemberKey>();
            while (pending.TryDequeue(out var item))
            {
                token.ThrowIfCancellationRequested(); CheckContext();
                if (!expanded.Add(item.Key)) continue;
                if (item.Workflow is { } workflow)
                    foreach (var dependency in workflow.Dependencies)
                    {
                        budget.Dependencies(Math.Max(1, dependency.DispatchNodeIds.Count));
                        await Material(dependency.ArtifactId, dependency.ArtifactHash, null, item.Depth + 1, token);
                    }
                else if (item.Model is { } model)
                    foreach (var dependency in model.Dependencies)
                    {
                        budget.Dependencies(1); await Material(dependency.Executable.ArtifactId, dependency.Executable.ArtifactHash, dependency.Executable, item.Depth + 1, token);
                    }
                else foreach (var dependency in item.Template!.DirectDependencies)
                {
                    budget.Dependencies(1); await Template(dependency.TemplateId, dependency.TemplateHash, item.Depth + 1, token);
                }
            }
            ValidateTemplateGraph(token);
            CheckContext(); await capture(additions, token); CheckContext();
        }
        async ValueTask Material(string id, string hash, WorkflowExecutableIdentity? fullPin, int depth, CancellationToken token)
        {
            if (depth > limits.MaxDependencyDepth) throw Refuse("runtime_source_dependency_depth_limit");
            var nativeKey = new ReleaseMemberKey(ReleaseRuntimeCodecs.WorkflowKind, id); var modelKey = new ReleaseMemberKey(ReleaseRuntimeCodecs.ModelKind, id);
            if ((knownPins.ContainsKey(nativeKey) || additions.Contains(nativeKey) || Contains(captured, nativeKey)) &&
                (knownPins.ContainsKey(modelKey) || additions.Contains(modelKey) || Contains(captured, modelKey))) throw Refuse("runtime_source_material_kind_conflict");
            foreach (var kind in new[] { ReleaseRuntimeCodecs.WorkflowKind, ReleaseRuntimeCodecs.ModelKind })
            {
                var key = new ReleaseMemberKey(kind, id);
                if (knownPins.TryGetValue(key, out var known)) { Match(known, id, hash, fullPin); return; }
                if (additions.Contains(key) || Contains(captured, key))
                {
                    var payload = additions.Contains(key) ? additions.GetPayload(key) : captured.GetPayload(key);
                    WorkflowExecutableIdentity actual;
                    if (kind == ReleaseRuntimeCodecs.WorkflowKind) { var decoded = codec.DecodeWorkflow(payload, limits); Scope(decoded.Scope); actual = decoded.Executable.Identity; }
                    else { var decoded = codec.DecodeModel(payload, limits); Scope(decoded.Scope); actual = decoded.Model.Identity; }
                    knownPins.Add(key, actual);
                    Match(actual, id, hash, fullPin); return;
                }
            }
            budget.WorkMember(); var current = await ReadMaterial(id, hash, fullPin, token); var encoded = Encode(current.Material);
            var member = additions.Add(encoded);
            knownPins.Add(member.Key, current.Material.Identity);
            pending.Enqueue(new(member.Key, current.Material.NativeWorkflow, current.Material.Model, null, depth));
        }
        async ValueTask Template(string id, string hash, int depth, CancellationToken token)
        {
            if (depth > limits.MaxDependencyDepth) throw Refuse("runtime_source_dependency_depth_limit");
            var key = new ReleaseMemberKey(ReleaseRuntimeCodecs.TemplateKind, id);
            if (knownTemplates.TryGetValue(key, out var known)) { if (known.TemplateId != id || known.TemplateHash != hash) throw Refuse("runtime_source_template_conflict"); return; }
            if (additions.Contains(key) || Contains(captured, key))
            {
                var existing = codec.DecodeTemplate(additions.Contains(key) ? additions.GetPayload(key) : captured.GetPayload(key), limits);
                Scope(existing.Scope); knownTemplates.Add(key, existing.Template);
                if (existing.Template.TemplateId != id || existing.Template.TemplateHash != hash) throw Refuse("runtime_source_template_conflict");
                pending.Enqueue(new(key, null, null, existing.Template, depth)); return;
            }
            if (!await authorization.AuthorizeTemplateAsync(scope, id, hash, token)) throw Refuse("runtime_source_export_forbidden");
            CheckContext(); budget.WorkMember();
            var snapshot = await templates.FindBoundedAsync(id, partition, budget.ReadLimits(), token) ?? throw Refuse("runtime_source_template_missing");
            budget.Read(snapshot.Scope, snapshot.StoredPayloadBytes, partition);
            if (snapshot.Template.TemplateId != id || snapshot.Template.TemplateHash != hash) throw Refuse("runtime_source_template_conflict");
            var member = additions.Add(codec.EncodeTemplate(new(scope, snapshot.Template), limits)); pending.Enqueue(new(member.Key, null, null, snapshot.Template, depth));
            knownTemplates.Add(key, snapshot.Template);
        }
        void ValidateTemplateGraph(CancellationToken token)
        {
            // A shared descendant may first be reached through a shorter path. Validate the complete DAG,
            // rather than using its first queue depth as the depth of every later occurrence.
            var incoming = knownTemplates.Keys.ToDictionary(key => key, _ => 0);
            var outgoing = new Dictionary<ReleaseMemberKey, List<ReleaseMemberKey>>();
            foreach (var (key, template) in knownTemplates)
            {
                var edges = template.DirectDependencies.Select(dependency => new ReleaseMemberKey(ReleaseRuntimeCodecs.TemplateKind, dependency.TemplateId)).ToList();
                outgoing.Add(key, edges);
                foreach (var child in edges)
                {
                    if (!incoming.ContainsKey(child)) throw Refuse("runtime_source_template_missing");
                    incoming[child]++;
                }
            }
            var ready = new Queue<ReleaseMemberKey>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
            var depths = knownTemplates.Keys.ToDictionary(key => key, _ => 0); var visited = 0;
            while (ready.TryDequeue(out var key))
            {
                token.ThrowIfCancellationRequested(); visited++;
                if (depths[key] > limits.MaxDependencyDepth) throw Refuse("runtime_source_dependency_depth_limit");
                foreach (var child in outgoing[key])
                {
                    depths[child] = Math.Max(depths[child], depths[key] + 1);
                    if (--incoming[child] == 0) ready.Enqueue(child);
                }
            }
            if (visited != knownTemplates.Count) throw Refuse("runtime_source_dependency_cycle");
        }

        async ValueTask<RuntimeMaterialSourceSnapshot> ReadMaterial(string id, string hash, WorkflowExecutableIdentity? fullPin, CancellationToken token)
        {
            await AuthorizeMaterial(id, hash, fullPin, token); CheckContext();
            var snapshot = await materials.FindMaterialBoundedAsync(id, partition, budget.ReadLimits(), token) ?? throw Refuse("runtime_source_material_missing");
            budget.Read(snapshot.Scope, snapshot.StoredPayloadBytes, partition); Match(snapshot.Material.Identity, id, hash, fullPin);
            if (snapshot.Material.Model is not null && !materials.SupportsModelMaterial) throw Refuse("runtime_source_model_profile_unsupported");
            return snapshot;
        }
        async ValueTask AuthorizeMaterial(string id, string hash, WorkflowExecutableIdentity? pin, CancellationToken token)
        {
            CheckContext(); if (!await authorization.AuthorizeMaterialAsync(scope, id, hash, pin, token)) throw Refuse("runtime_source_export_forbidden"); CheckContext();
        }
        ReleaseEncodedRuntimeMember Encode(RuntimeExecutionMaterial item) => item.NativeWorkflow is { } native
            ? codec.EncodeWorkflow(new(scope, native), limits) : codec.EncodeModel(new(scope, item.Model!), limits);
        void Root(WorkflowExecutableIdentity pin)
        {
            if (roots.TryGetValue(pin.ArtifactId, out var prior) && !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(prior, pin)) throw Refuse("runtime_source_pin_conflict");
            roots[pin.ArtifactId] = pin;
        }
        void Scope(ReleaseRuntimeContentScope actual) { if (actual != scope) throw Refuse("runtime_source_scope_mismatch"); }
        void CheckContext() { if (Context() != scope) throw Refuse("runtime_source_scope_changed"); }
    }

    private ReleaseRuntimeContentScope Context()
    {
        var context = source.Current;
        if (context is null || string.IsNullOrWhiteSpace(context.Authority) || string.IsNullOrWhiteSpace(context.Partition) ||
            context.Authority.Length > 4096 || context.Partition != access.Current.RequireScope(admitPrivileged: true).Value)
            throw Refuse("runtime_source_scope_mismatch");
        return context;
    }
    private static bool Contains(IReleaseContentReader reader, ReleaseMemberKey key)
    { try { _ = reader.GetMember(key); return true; } catch (KeyNotFoundException) { return false; } }
    private static void Match(WorkflowExecutableIdentity actual, string id, string hash, WorkflowExecutableIdentity? pin)
    { if (actual.ArtifactId != id || actual.ArtifactHash != hash || pin is not null && !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(actual, pin)) throw Refuse("runtime_source_pin_conflict"); }
    private static ReleaseCaptureException Refuse(string code) => new(code);
    private sealed record Item(ReleaseMemberKey Key, WorkflowExecutable? Workflow, RuntimeModelMaterial? Model, ExecutableActivityTemplate? Template, int Depth = 0);
    private sealed class SourceBudget(ReleaseBundleLimits limits)
    {
        private long _bytes;
        private int _members;
        private int _dependencies;
        public void WorkMember() { if (++_members > limits.MaxMembers) throw Refuse("runtime_source_member_limit"); }
        public void Dependencies(int count) { _dependencies = checked(_dependencies + count); if (_dependencies > limits.MaxEdges) throw Refuse("runtime_source_dependency_limit"); }
        public void Read(PersistenceScope actual, int bytes, PersistenceScope expected)
        { if (actual != expected || bytes < 0) throw Refuse("runtime_source_snapshot_invalid"); _bytes = checked(_bytes + bytes); if (_bytes > limits.MaxExpandedBytes) throw Refuse("runtime_source_aggregate_byte_limit"); }
        public RuntimeSourceReadLimits ReadLimits()
        {
            var remaining = limits.MaxExpandedBytes - _bytes; if (remaining <= 0) throw Refuse("runtime_source_aggregate_byte_limit");
            var result = new RuntimeSourceReadLimits { MaxPayloadBytes = (int)Math.Min(limits.MaxBlobBytes, remaining), MaxAggregatePayloadBytes = remaining,
                MaxJsonDepth = limits.MaxJsonDepth, MaxMaterials = limits.MaxMembers, MaxDependencies = limits.MaxEdges, MaxDependencyDepth = limits.MaxDependencyDepth };
            result.Validate(); return result;
        }
    }
}

internal sealed class RuntimeSourceCapturedMembers(ReleaseBundleLimits limits) : IReleaseContentReader
{
    private readonly Dictionary<ReleaseMemberKey, ReleaseEncodedRuntimeMember> _items = [];
    private long _bytes;
    public IReadOnlyList<ReleaseContentMember> Members => Array.AsReadOnly(_items.Values.Select(value => new ReleaseContentMember(value.Descriptor, value.OwnerVersion)).ToArray());
    public bool Contains(ReleaseMemberKey key) => _items.ContainsKey(key);
    public ReleaseContentMember Add(ReleaseEncodedRuntimeMember item)
    {
        var key = new ReleaseMemberKey(item.Descriptor.Kind, item.Descriptor.Identity);
        if (_items.TryGetValue(key, out var prior))
        { if (prior.OwnerVersion != item.OwnerVersion || !prior.GetPayloadBytes().AsSpan().SequenceEqual(item.GetPayloadBytes())) throw new ReleaseCaptureException("runtime_source_member_conflict"); return GetMember(key); }
        if (_items.Count >= limits.MaxMembers || item.Descriptor.ByteLength > limits.MaxBlobBytes || (_bytes = checked(_bytes + item.Descriptor.ByteLength)) > limits.MaxExpandedBytes)
            throw new ReleaseCaptureException("runtime_source_member_limit");
        _items.Add(key, item); return GetMember(key);
    }
    public ReleaseContentMember GetMember(ReleaseMemberKey key) => _items.TryGetValue(key, out var item) ? new(item.Descriptor, item.OwnerVersion) : throw new KeyNotFoundException();
    public ReadOnlyMemory<byte> GetPayload(ReleaseMemberKey key) => _items[key].GetPayloadBytes();
}
