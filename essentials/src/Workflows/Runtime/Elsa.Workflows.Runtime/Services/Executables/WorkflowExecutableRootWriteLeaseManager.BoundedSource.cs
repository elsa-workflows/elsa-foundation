using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executables;

public sealed partial class WorkflowExecutableRootWriteLeaseManager
{
    public async ValueTask ExecuteFencedAsync(IReadOnlyCollection<WorkflowExecutableIdentity> roots, string leaseId,
        PersistenceScope expectedScope, RuntimeSourceReadLimits limits,
        Func<string, string, WorkflowExecutableIdentity?, CancellationToken, ValueTask> authorizeRead,
        Func<IReadOnlyList<WorkflowExecutableRootWriteLease>, CancellationToken, ValueTask> write, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(roots); ArgumentException.ThrowIfNullOrWhiteSpace(leaseId); ArgumentNullException.ThrowIfNull(expectedScope);
        ArgumentNullException.ThrowIfNull(limits); limits.Validate(); ArgumentNullException.ThrowIfNull(authorizeRead); ArgumentNullException.ThrowIfNull(write); cancellationToken.ThrowIfCancellationRequested();
        if (executableStore is not IRuntimeBoundedSourceLeaseStore provider) throw Refuse("runtime_source_retention_profile_unsupported", true);
        var savedRoots = roots.Take(limits.MaxMaterials + 1).ToArray();
        if (savedRoots.Length == 0 || savedRoots.Length > limits.MaxMaterials || savedRoots.Any(root => root is null)) throw Refuse("runtime_source_root_limit");
        var before = await LoadBoundedClosureAsync(savedRoots, expectedScope, limits, authorizeRead, cancellationToken);
        await ExecuteLeasedAsync(before.Keys, leaseId, async (grants, token) =>
        {
            var current = await LoadBoundedClosureAsync(savedRoots, expectedScope, limits, authorizeRead, token);
            if (current.Count != before.Count) throw Refuse("runtime_source_closure_changed");
            foreach (var (id, original) in before)
                if (!current.TryGetValue(id, out var found) || found.Material.IncarnationId != original.Material.IncarnationId ||
                    !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(found.Material.Identity, original.Material.Identity))
                    throw Refuse("runtime_source_closure_changed");
            await write(grants, token);
        }, cancellationToken, verifyBeforeWrite: true, boundedSource: new(provider, expectedScope, limits));
    }

    private async ValueTask<Dictionary<string, RuntimeMaterialSourceSnapshot>> LoadBoundedClosureAsync(
        IReadOnlyCollection<WorkflowExecutableIdentity> roots, PersistenceScope scope, RuntimeSourceReadLimits limits,
        Func<string, string, WorkflowExecutableIdentity?, CancellationToken, ValueTask> authorizeRead, CancellationToken token)
    {
        var found = new Dictionary<string, RuntimeMaterialSourceSnapshot>(StringComparer.Ordinal);
        var edges = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pending = new Queue<(string Id, string Hash, WorkflowExecutableIdentity? Full)>();
        foreach (var root in roots) { Pin(root); pending.Enqueue((root.ArtifactId, root.ArtifactHash, root)); }
        long bytes = 0; var dependencyCount = 0;
        while (pending.TryDequeue(out var expected))
        {
            token.ThrowIfCancellationRequested();
            if (found.TryGetValue(expected.Id, out var prior)) { Match(prior.Material.Identity, expected); continue; }
            if (found.Count >= limits.MaxMaterials) throw Refuse("runtime_source_material_limit");
            var remaining = limits.MaxAggregatePayloadBytes - bytes;
            if (remaining <= 0) throw Refuse("runtime_source_aggregate_byte_limit");
            var readLimits = new RuntimeSourceReadLimits { MaxPayloadBytes = (int)Math.Min(limits.MaxPayloadBytes, remaining), MaxMetadataBytes = limits.MaxMetadataBytes,
                MaxAggregatePayloadBytes = remaining, MaxJsonDepth = limits.MaxJsonDepth, MaxMaterials = limits.MaxMaterials,
                MaxDependencies = limits.MaxDependencies, MaxDependencyDepth = limits.MaxDependencyDepth };
            await authorizeRead(expected.Id, expected.Hash, expected.Full, token);
            var snapshot = await executableStore.FindMaterialBoundedAsync(expected.Id, scope, readLimits, token)
                ?? throw Refuse("runtime_source_dependency_missing");
            if (snapshot.Scope != scope || snapshot.StoredPayloadBytes < 0 || snapshot.StoredPayloadBytes > remaining ||
                string.IsNullOrWhiteSpace(snapshot.Material.IncarnationId)) throw Refuse("runtime_source_snapshot_invalid");
            bytes = checked(bytes + snapshot.StoredPayloadBytes);
            if (bytes > limits.MaxAggregatePayloadBytes) throw Refuse("runtime_source_aggregate_byte_limit");
            Match(snapshot.Material.Identity, expected); found.Add(expected.Id, snapshot);
            var outgoing = new List<string>(); edges.Add(expected.Id, outgoing);
            if (snapshot.Material.NativeWorkflow is { } native)
            {
                foreach (var dependency in native.Dependencies)
                {
                    dependencyCount = checked(dependencyCount + Math.Max(1, dependency.DispatchNodeIds.Count));
                    if (dependencyCount > limits.MaxDependencies) throw Refuse("runtime_source_dependency_limit");
                    outgoing.Add(dependency.ArtifactId); pending.Enqueue((dependency.ArtifactId, dependency.ArtifactHash, null));
                }
            }
            else foreach (var dependency in snapshot.Material.Model!.Dependencies)
            {
                if (++dependencyCount > limits.MaxDependencies) throw Refuse("runtime_source_dependency_limit");
                var pin = dependency.Executable; Pin(pin);
                outgoing.Add(pin.ArtifactId); pending.Enqueue((pin.ArtifactId, pin.ArtifactHash, pin));
            }
        }
        // Iterative topological validation rejects actual cycles and bounds the longest dependency path,
        // including a long second path to material first discovered through a shorter route.
        var incoming = found.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var children in edges.Values) foreach (var child in children) incoming[child]++;
        var ready = new Queue<string>(incoming.Where(pair => pair.Value == 0).Select(pair => pair.Key));
        var depths = found.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal); var visited = 0;
        while (ready.TryDequeue(out var id))
        {
            token.ThrowIfCancellationRequested(); visited++;
            if (depths[id] > limits.MaxDependencyDepth) throw Refuse("runtime_source_dependency_depth_limit");
            foreach (var child in edges[id])
            {
                depths[child] = Math.Max(depths[child], depths[id] + 1);
                if (--incoming[child] == 0) ready.Enqueue(child);
            }
        }
        if (visited != found.Count) throw Refuse("runtime_source_dependency_cycle");
        return found;

        static void Match(WorkflowExecutableIdentity actual, (string Id, string Hash, WorkflowExecutableIdentity? Full) expected)
        {
            if (actual.ArtifactId != expected.Id || actual.ArtifactHash != expected.Hash ||
                expected.Full is not null && !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(actual, expected.Full))
                throw Refuse("runtime_source_pin_conflict");
        }
        void Pin(WorkflowExecutableIdentity pin)
        {
            if (pin is null || new[] { pin.ArtifactId, pin.ArtifactHash, pin.DefinitionId, pin.DefinitionVersionId, pin.ArtifactVersion }
                    .Any(value => string.IsNullOrWhiteSpace(value) || value.Length > limits.MaxMetadataBytes)) throw Refuse("runtime_source_pin_invalid");
        }
    }
    private static RuntimeSourceReadException Refuse(string code, bool unsupported = false) => new(code, unsupported);
    private sealed record BoundedSourceLease(IRuntimeBoundedSourceLeaseStore Store, PersistenceScope Scope, RuntimeSourceReadLimits Limits);
}
