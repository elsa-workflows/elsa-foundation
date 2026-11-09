using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Disclosure.Services;

internal sealed class ActivityCatalogDisclosureEvaluator(
    CatalogDisclosureRegistry registry,
    CatalogDisclosureRequirementCanonicalizer canonicalizer,
    IActivityCatalogDisclosureAuthority authority,
    IServiceProvider services) : IActivityCatalogDisclosureEvaluator
{
    public async ValueTask<IReadOnlyList<CatalogDisclosureDecision>> EvaluateAsync(
        IReadOnlyList<CatalogDisclosureResource> resources, CatalogDisclosurePurpose purpose,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        registry.EnsureInitialized();
        if (resources is null)
            throw new CatalogDisclosureUnavailableException();
        var snapshot = resources.ToArray();
        var decisions = new CatalogDisclosureDecision[snapshot.Length];
        for (var index = 0; index < snapshot.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            decisions[index] = await EvaluateResourceAsync(snapshot[index], purpose, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        return Array.AsReadOnly(decisions);
    }

    private async ValueTask<CatalogDisclosureDecision> EvaluateResourceAsync(
        CatalogDisclosureResource? resource, CatalogDisclosurePurpose purpose, CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(purpose) || resource is null ||
            resource.Kind is not ("definition" or "version" or "draft" or "publication" or "builtin") ||
            !CatalogDisclosureText.IsValid(resource.Id, 450) || !ValidOwner(resource.OwnerScope) ||
            !canonicalizer.TryCanonicalize(resource.Requirement, out var requirement))
            return new(CatalogDisclosureStatus.Unavailable);
        if (requirement.Kind == CatalogDisclosureKind.Public)
            return new(CatalogDisclosureStatus.Allowed);

        try
        {
            var isPresent = authority.IsPresent;
            cancellationToken.ThrowIfCancellationRequested();
            if (!isPresent)
                return new(CatalogDisclosureStatus.Unavailable);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception error) { throw new CatalogDisclosureUnavailableException(error); }

        var canonicalResource = new CatalogDisclosureResource(resource.Kind, resource.Id, resource.OwnerScope, requirement);
        var denied = false;
        var unavailable = false;
        Exception? failure = null;
        var revisions = new List<string>();
        foreach (var policyRequirement in requirement.Policies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var key = new CatalogDisclosurePolicyKey(policyRequirement.PolicyKey, policyRequirement.BindingSchema);
            var registration = registry.FindPolicy(key);
            if (registration is null)
            {
                unavailable = true;
                continue;
            }
            try
            {
                var policy = services.GetRequiredKeyedService<IActivityCatalogDisclosurePolicy>(key);
                if (policy.GetType() != registration.PolicyType)
                {
                    unavailable = true;
                    continue;
                }
                var decision = await policy.EvaluateAsync(canonicalResource, policyRequirement, purpose, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                switch (decision?.Status)
                {
                    case CatalogDisclosureStatus.Allowed:
                        if (decision.AuthorizationRevision is { } revision)
                            revisions.Add(revision);
                        break;
                    case CatalogDisclosureStatus.Denied:
                        denied = true;
                        break;
                    default:
                        unavailable = true;
                        break;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error)
            {
                unavailable = true;
                failure ??= error;
            }
        }
        if (denied)
            return new(CatalogDisclosureStatus.Denied);
        if (failure is not null)
            throw new CatalogDisclosureUnavailableException(failure);
        return unavailable
            ? new(CatalogDisclosureStatus.Unavailable)
            : new(CatalogDisclosureStatus.Allowed, Revision(revisions));
    }

    internal static bool ValidOwner(CatalogOwnerScope? scope) => scope is not null && (scope.Kind switch
    {
        CatalogOwnerScopeKind.Global => scope.TenantId is null,
        CatalogOwnerScopeKind.Tenant => CatalogDisclosureText.IsValid(scope.TenantId, int.MaxValue),
        _ => false
    });

    private static string? Revision(List<string> revisions)
    {
        if (revisions.Count == 0) return null;
        // Length framing preserves every opaque revision and avoids ambiguous concatenations.
        return string.Concat(revisions.Select(value => $"{value.Length}:{value}"));
    }
}
