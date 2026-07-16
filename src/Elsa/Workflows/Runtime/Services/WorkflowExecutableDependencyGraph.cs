using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Loads and validates the immutable transitive dependency closure of one or more executable artifacts.
/// </summary>
public sealed class WorkflowExecutableDependencyGraph(IWorkflowExecutableStore executableStore)
{
    /// <summary>Loads a fresh store snapshot and resolves the closure rooted at <paramref name="root"/>.</summary>
    public async ValueTask<IReadOnlyCollection<WorkflowExecutable>> LoadClosureAsync(
        WorkflowExecutableIdentity root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        var executables = await executableStore.ListAsync(cancellationToken);
        return ResolveClosure([root], executables);
    }

    /// <summary>
    /// Resolves a deterministic, de-duplicated closure from a caller-owned store snapshot. Every edge is checked
    /// against the child's full artifact ID/hash identity. Missing artifacts, hash mismatches, conflicting identities,
    /// and exact-identity cycles fail closed.
    /// </summary>
    public static IReadOnlyCollection<WorkflowExecutable> ResolveClosure(
        IReadOnlyCollection<WorkflowExecutableIdentity> roots,
        IReadOnlyCollection<WorkflowExecutable> executables)
    {
        ArgumentNullException.ThrowIfNull(roots);
        ArgumentNullException.ThrowIfNull(executables);

        var byArtifactId = new Dictionary<string, WorkflowExecutable>(StringComparer.Ordinal);
        foreach (var executable in executables)
        {
            ArgumentNullException.ThrowIfNull(executable);
            var artifactId = executable.Identity.ArtifactId;
            if (!byArtifactId.TryAdd(artifactId, executable))
            {
                throw NewFailure(
                    WorkflowExecutableDependencyGraphFailureKind.ConflictingIdentity,
                    $"The executable store snapshot contains more than one artifact with ID '{artifactId}'.");
            }
        }

        var resolved = new Dictionary<ArtifactIdentity, WorkflowExecutable>();
        var active = new HashSet<ArtifactIdentity>();
        var path = new List<ArtifactIdentity>();
        foreach (var root in roots
                     .OrderBy(root => root.ArtifactId, StringComparer.Ordinal)
                     .ThenBy(root => root.ArtifactHash, StringComparer.Ordinal))
        {
            Visit(new ArtifactIdentity(root.ArtifactId, root.ArtifactHash));
        }

        return Array.AsReadOnly(resolved
            .OrderBy(item => item.Key.ArtifactId, StringComparer.Ordinal)
            .ThenBy(item => item.Key.ArtifactHash, StringComparer.Ordinal)
            .Select(item => item.Value)
            .ToArray());

        void Visit(ArtifactIdentity expected)
        {
            if (active.Contains(expected))
            {
                var cycleStart = path.FindIndex(item => item == expected);
                var cycle = path.Skip(Math.Max(0, cycleStart)).Append(expected);
                throw NewFailure(
                    WorkflowExecutableDependencyGraphFailureKind.Cycle,
                    $"The executable dependency graph contains an exact artifact cycle: {RenderPath(cycle)}.");
            }

            if (resolved.ContainsKey(expected))
                return;

            if (!byArtifactId.TryGetValue(expected.ArtifactId, out var executable))
            {
                throw NewFailure(
                    WorkflowExecutableDependencyGraphFailureKind.MissingArtifact,
                    $"Executable dependency '{expected.ArtifactId}@{expected.ArtifactHash}' is missing. Path: {RenderPath(path.Append(expected))}.");
            }

            if (!StringComparer.Ordinal.Equals(executable.Identity.ArtifactId, expected.ArtifactId) ||
                !StringComparer.Ordinal.Equals(executable.Identity.ArtifactHash, expected.ArtifactHash))
            {
                throw NewFailure(
                    WorkflowExecutableDependencyGraphFailureKind.HashMismatch,
                    $"Executable dependency '{expected.ArtifactId}' expected hash '{expected.ArtifactHash}' but stored hash was '{executable.Identity.ArtifactHash}'. Path: {RenderPath(path.Append(expected))}.");
            }

            active.Add(expected);
            path.Add(expected);
            try
            {
                foreach (var dependency in executable.Dependencies
                             .OrderBy(dependency => dependency.ArtifactId, StringComparer.Ordinal)
                             .ThenBy(dependency => dependency.ArtifactHash, StringComparer.Ordinal))
                {
                    Visit(new ArtifactIdentity(dependency.ArtifactId, dependency.ArtifactHash));
                }

                resolved.Add(expected, executable);
            }
            finally
            {
                path.RemoveAt(path.Count - 1);
                active.Remove(expected);
            }
        }
    }

    private static WorkflowExecutableDependencyGraphException NewFailure(
        WorkflowExecutableDependencyGraphFailureKind kind,
        string message) =>
        new(kind, message);

    private static string RenderPath(IEnumerable<ArtifactIdentity> path) =>
        string.Join(" -> ", path.Select(item => $"{item.ArtifactId}@{item.ArtifactHash}"));

    private readonly record struct ArtifactIdentity(string ArtifactId, string ArtifactHash);
}

/// <summary>Classifies a fail-closed executable dependency graph integrity failure.</summary>
public enum WorkflowExecutableDependencyGraphFailureKind
{
    MissingArtifact,
    HashMismatch,
    ConflictingIdentity,
    Cycle
}

/// <summary>Raised when an executable dependency closure cannot be trusted.</summary>
public sealed class WorkflowExecutableDependencyGraphException(
    WorkflowExecutableDependencyGraphFailureKind kind,
    string message) : InvalidOperationException(message)
{
    public WorkflowExecutableDependencyGraphFailureKind Kind { get; } = kind;
}
