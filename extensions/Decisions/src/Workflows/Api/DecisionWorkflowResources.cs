using Elsa.Decisions.Core;
using Elsa.Decisions.Workflows.Runtime;
using Elsa.Decisions.Workflows.Runtime.Activities;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Decisions.Workflows.Api;

/// <summary>Inspects exact immutable dependency material without querying mutable decision Design state.</summary>
internal sealed class DecisionWorkflowResources(IServiceProvider services)
{
    internal sealed record RequiredGrant(string TenantId, string AssetId, bool IsProtected);

    internal async ValueTask<IReadOnlyList<RequiredGrant>> FromStateAsync(WorkflowExecutableIdentity root, CancellationToken ct)
    {
        var store = services.GetService<IWorkflowExecutableStore>() ?? throw Missing();
        var found = new List<RequiredGrant>(); var visited = new Dictionary<string, string>(StringComparer.Ordinal);
        await Visit(root.ArtifactId, root.ArtifactHash);
        return found;
        async ValueTask Visit(string artifactId, string expectedHash)
        {
            ct.ThrowIfCancellationRequested();
            if (visited.TryGetValue(artifactId, out var prior)) { if (prior != expectedHash) throw Missing(); return; }
            if (visited.Count >= 1000) throw Missing();
            visited.Add(artifactId, expectedHash);
            if (store.SupportsModelMaterial)
            {
                var material = await store.FindMaterialAsync(artifactId, ct);
                if (material is null || material.Identity.ArtifactHash != expectedHash) throw Missing();
                if (material.NativeWorkflow is { } native)
                {
                    found.AddRange(FromExecutable(native));
                    foreach (var dependency in native.Dependencies) await Visit(dependency.ArtifactId, dependency.ArtifactHash);
                }
                else if (material.Model is { } model)
                    foreach (var dependency in model.Dependencies) await Visit(dependency.Executable.ArtifactId, dependency.Executable.ArtifactHash);
                else throw Missing();
            }
            else
            {
                var native = await store.FindAsync(artifactId, ct);
                if (native is null || native.Identity.ArtifactHash != expectedHash) throw Missing();
                found.AddRange(FromExecutable(native));
                foreach (var dependency in native.Dependencies) await Visit(dependency.ArtifactId, dependency.ArtifactHash);
            }
        }
    }

    internal static IReadOnlyList<RequiredGrant> FromClosure(WorkflowArtifactClosure closure)
    {
        var byId = new Dictionary<string, WorkflowExecutable>(StringComparer.Ordinal);
        foreach (var artifact in closure.Artifacts)
            if (!byId.TryAdd(artifact.Identity.ArtifactId, artifact)) throw Missing();
        if (!byId.ContainsKey(closure.RootArtifactId) || byId.Count > 1000) throw Missing();
        var visited = new HashSet<string>(StringComparer.Ordinal); var grants = new List<RequiredGrant>();
        Visit(closure.RootArtifactId); if (visited.Count != byId.Count) throw Missing(); return grants;
        void Visit(string id)
        {
            if (!visited.Add(id)) return;
            if (!byId.TryGetValue(id, out var artifact)) throw Missing();
            grants.AddRange(FromExecutable(artifact));
            foreach (var dependency in artifact.Dependencies)
            {
                if (!byId.TryGetValue(dependency.ArtifactId, out var child) || child.Identity.ArtifactHash != dependency.ArtifactHash) throw Missing();
                Visit(dependency.ArtifactId);
            }
        }
    }

    private static IEnumerable<RequiredGrant> FromExecutable(WorkflowExecutable executable)
    {
        foreach (var node in executable.Nodes)
        {
            var hasPin = node.Metadata.TryGetValue(DecisionWorkflowPin.MetadataKey, out var serialized);
            if (node.ActivityType != EvaluateDecision.ActivityType && !hasPin) continue;
            if (!hasPin || node.ActivityType != EvaluateDecision.ActivityType) throw Missing();
            var pin = DecisionWorkflowPin.Parse(serialized!);
            var protectedValues = node.InputBindings.Values.Any(input => input.EffectivePolicy.HidesValue()) ||
                node.ActivityContract?.Result.Policy.HidesValue() == true ||
                node.ActivityContract?.Result.Projections.Values.Any(output => output.Policy.HidesValue()) == true;
            yield return new(pin.TenantId, pin.AssetId, protectedValues);
        }
    }
    internal static DecisionInvocationException Missing() => new("disclosure-unavailable", "The exact decision-dependent material cannot be authorized for disclosure.");
}

internal sealed class DecisionWorkflowResourceHandler(IServiceProvider services, DecisionWorkflowResources resources) : IPermissionResourceHandler
{
    public async ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken ct = default)
    {
        // Domain permission checks recurse through the canonical service with an asset ID resource;
        // this handler never handles those requests, preserving resource-denial precedence.
        if (context.Permission.StartsWith("decisions:", StringComparison.Ordinal)) return null;
        IReadOnlyList<DecisionWorkflowResources.RequiredGrant> required;
        var source = false;
        try
        {
            switch (context.Resource)
            {
                case WorkflowArtifactClosure closure when context.Permission == "workflows/publishing:read":
                    required = DecisionWorkflowResources.FromClosure(closure); source = true; break;
                case WorkflowExecutionState state when context.Permission is "workflows/activity-executions:inspect-values" or "workflows/activity-executions:resolve-value-payloads":
                    required = await resources.FromStateAsync(state.PinnedExecutable, ct); break;
                case RuntimeModelExecutionState model when context.Permission == "bpmn:read":
                    required = await resources.FromStateAsync(model.Executable, ct); break;
                default: return null;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return PermissionEvaluationResult.Denied("Exact decision-dependent material is unavailable."); }
        if (required.Count == 0) return null;
        var tenants = context.Principal.FindAll(IdentityClaimTypes.TenantId).ToArray();
        if (tenants.Length != 1 || string.IsNullOrWhiteSpace(tenants[0].Value) ||
            context.TenantId is not null && context.TenantId != tenants[0].Value)
            return PermissionEvaluationResult.Denied("A canonical tenant is required for decision disclosure.");
        var authorization = services.GetRequiredService<IPermissionAuthorizationService>();
        foreach (var resource in required.Distinct())
        {
            if (resource.TenantId != tenants[0].Value) return PermissionEvaluationResult.Denied("The decision material belongs to another tenant.");
            foreach (var permission in Grants(source, resource.IsProtected))
                if (!(await authorization.AuthorizeAsync(new(context.Principal, permission, resource.TenantId, resource.AssetId), ct)).Succeeded)
                    return PermissionEvaluationResult.Denied("The reader cannot disclose this decision source or value.");
        }
        // Never grant the workflow permission: its ordinary evaluator and other handlers still decide.
        return null;
    }
    private static IEnumerable<string> Grants(bool source, bool sensitive)
    {
        yield return DecisionPermissions.Read;
        if (source) yield return DecisionPermissions.Source;
        if (sensitive) yield return DecisionPermissions.SensitiveValues;
    }
}
