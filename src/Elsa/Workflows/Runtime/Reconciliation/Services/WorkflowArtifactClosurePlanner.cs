using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;

namespace Elsa.Workflows.Runtime.Reconciliation.Services;

/// <summary>
/// The outcome of planning one closure unit: either an activation-ordered member list, or a named refusal.
/// </summary>
/// <param name="Members">Dependencies-first order. Empty when <paramref name="RejectionKind"/> is set.</param>
/// <param name="RejectionKind">The named refusal, or <c>None</c> when the plan is usable.</param>
/// <param name="Diagnostic">The human-readable cause of a refusal.</param>
public sealed record WorkflowArtifactClosurePlan(
    IReadOnlyList<WorkflowExecutable> Members,
    WorkflowArtifactRejectionKind RejectionKind = WorkflowArtifactRejectionKind.None,
    string? Diagnostic = null)
{
    public bool IsValid => RejectionKind == WorkflowArtifactRejectionKind.None;

    public static WorkflowArtifactClosurePlan Reject(WorkflowArtifactRejectionKind kind, string diagnostic) =>
        new([], kind, diagnostic);
}

/// <summary>
/// Validates a closure envelope <b>against itself</b> and produces the order its members must be activated in.
/// </summary>
/// <remarks>
/// <para>
/// <b>The store is never consulted here, and that is the whole point.</b> FR-B-010 promises a self-contained
/// transitive closure, so envelope validity is environment-independent: the same file must fail identically on
/// every runtime. An envelope that only validates because the target store happens to already contain a child is
/// a broken export, and letting it pass would mean a file that imports on the machine it was tested on and fails
/// on the one it ships to. The store is consulted afterwards, by the reconciler, and only to skip persisting
/// members it already holds.
/// </para>
/// <para>
/// Ordering is computed explicitly rather than taken from
/// <see cref="WorkflowExecutableDependencyGraph.ResolveClosure"/>, which sorts its result by artifact id and hash
/// — deterministic, but <em>not</em> dependency-first. Persisting is order-free (the executable store is
/// create-only and content-addressed), but activation is not: a parent that activates while a child's source
/// reference does not yet exist is a workflow that dispatches into nothing.
/// </para>
/// </remarks>
public static class WorkflowArtifactClosurePlanner
{
    public static WorkflowArtifactClosurePlan Plan(WorkflowArtifactClosure closure)
    {
        ArgumentNullException.ThrowIfNull(closure);

        if (string.IsNullOrWhiteSpace(closure.RootArtifactId))
            return WorkflowArtifactClosurePlan.Reject(
                WorkflowArtifactRejectionKind.MalformedClosure,
                "the closure declares no root artifact id.");

        if (closure.Artifacts.Count == 0)
            return WorkflowArtifactClosurePlan.Reject(
                WorkflowArtifactRejectionKind.MalformedClosure,
                $"the closure carries no artifacts, so its declared root '{closure.RootArtifactId}' cannot be resolved.");

        if (closure.Artifacts.Any(artifact => artifact is null))
            return WorkflowArtifactClosurePlan.Reject(
                WorkflowArtifactRejectionKind.MalformedClosure,
                "the closure carries a null artifact entry.");

        if (!closure.Artifacts.Any(artifact => StringComparer.Ordinal.Equals(artifact.Identity.ArtifactId, closure.RootArtifactId)))
            return WorkflowArtifactClosurePlan.Reject(
                WorkflowArtifactRejectionKind.MalformedClosure,
                $"the declared root artifact '{closure.RootArtifactId}' is not among the closure's {closure.Artifacts.Count} carried artifact(s).");

        // Every carried member is treated as a root, not just the declared one. Validating only what the root
        // reaches would let a broken extra member ride along unchecked and then be imported anyway (the importer
        // activates every member, since each carries its own definition).
        var roots = closure.Artifacts.Select(artifact => artifact.Identity).ToArray();
        try
        {
            _ = WorkflowExecutableDependencyGraph.ResolveClosure(roots, closure.Artifacts);
        }
        catch (WorkflowExecutableDependencyGraphException exception)
        {
            return WorkflowArtifactClosurePlan.Reject(MapFailureKind(exception.Kind), exception.Message);
        }

        return new WorkflowArtifactClosurePlan(SortDependenciesFirst(closure.Artifacts));
    }

    /// <summary>
    /// Depth-first post-order over the carried dependency edges, with ordinal tie-breaking so two runs over the
    /// same envelope produce byte-identical order. The graph is already known to be acyclic and complete by the
    /// time this runs, so the traversal needs no cycle guard of its own.
    /// </summary>
    private static IReadOnlyList<WorkflowExecutable> SortDependenciesFirst(IReadOnlyList<WorkflowExecutable> artifacts)
    {
        var byArtifactId = artifacts.ToDictionary(artifact => artifact.Identity.ArtifactId, StringComparer.Ordinal);
        var ordered = new List<WorkflowExecutable>(artifacts.Count);
        var visited = new HashSet<string>(StringComparer.Ordinal);

        foreach (var artifact in artifacts.OrderBy(artifact => artifact.Identity.ArtifactId, StringComparer.Ordinal))
            Visit(artifact);

        return ordered;

        void Visit(WorkflowExecutable artifact)
        {
            if (!visited.Add(artifact.Identity.ArtifactId))
                return;

            foreach (var dependency in artifact.Dependencies
                         .Select(dependency => dependency.ArtifactId)
                         .Distinct(StringComparer.Ordinal)
                         .OrderBy(artifactId => artifactId, StringComparer.Ordinal))
            {
                if (byArtifactId.TryGetValue(dependency, out var child))
                    Visit(child);
            }

            ordered.Add(artifact);
        }
    }

    private static WorkflowArtifactRejectionKind MapFailureKind(WorkflowExecutableDependencyGraphFailureKind kind) =>
        kind switch
        {
            WorkflowExecutableDependencyGraphFailureKind.MissingArtifact => WorkflowArtifactRejectionKind.MissingArtifact,
            WorkflowExecutableDependencyGraphFailureKind.HashMismatch => WorkflowArtifactRejectionKind.HashMismatch,
            WorkflowExecutableDependencyGraphFailureKind.ConflictingIdentity => WorkflowArtifactRejectionKind.ConflictingIdentity,
            WorkflowExecutableDependencyGraphFailureKind.Cycle => WorkflowArtifactRejectionKind.Cycle,
            _ => WorkflowArtifactRejectionKind.MalformedClosure
        };
}
