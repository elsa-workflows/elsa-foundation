using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Disclosure.Services;

internal sealed class ActivityCatalogAdmission(
    CatalogDisclosureRegistry registry,
    ICatalogProducerScope producerScope,
    IActivityCatalogOwnerScopeAccessor ownership,
    CatalogDisclosureRequirementCanonicalizer canonicalizer,
    IServiceProvider services) : IActivityCatalogAdmission
{
    public async ValueTask<CatalogDisclosureRequirement> AdmitAsync(
        CatalogAdmissionOrigin origin, CatalogAdmissionCandidate candidate, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var registration = ValidateOrigin(origin);
            ValidateCandidate(registration, candidate);
            var inherited = canonicalizer.Canonicalize(candidate.Inherited);
            var owner = services.GetRequiredKeyedService<IActivityCatalogAdmissionOwner>(registration.RegistrationId);
            if (owner.GetType() != registration.AdmissionOwnerType)
                throw new CatalogAdmissionException();
            var classified = canonicalizer.Canonicalize(await owner.ClassifyAsync(candidate, cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            // Recheck after invoking the owner; classifications never replace current scope facts.
            ValidateOrigin(origin);
            ValidateCandidate(registration, candidate);
            if (registration.MinimumPolicy is { } minimum &&
                (classified.Kind != CatalogDisclosureKind.Protected || !classified.Policies.Any(policy =>
                    policy.PolicyKey == minimum.PolicyKey && policy.BindingSchema == minimum.BindingSchema)))
                throw new CatalogAdmissionException();
            return canonicalizer.Union([inherited, classified]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (CatalogAdmissionException) { throw; }
        catch (Exception error) { throw new CatalogAdmissionException(error); }
    }

    public async ValueTask ValidateAsync(CatalogAdmissionOrigin origin, CatalogAdmissionCandidate candidate,
        CatalogDisclosureRequirement expected, CancellationToken cancellationToken = default)
    {
        var admitted = await AdmitAsync(origin, candidate, cancellationToken);
        if (!canonicalizer.AreEquivalent(admitted, expected))
            throw new CatalogAdmissionException();
    }

    private CatalogProducerRegistration ValidateOrigin(CatalogAdmissionOrigin? origin)
    {
        if (origin is null || !CatalogDisclosureText.IsValid(origin.RegistrationId, 128) || origin.ScopeBinding is null ||
            !ReferenceEquals(origin.ScopeBinding, producerScope.GetBinding(origin.RegistrationId)))
            throw new CatalogAdmissionException();
        return registry.FindProducer(origin.RegistrationId) ?? throw new CatalogAdmissionException();
    }

    private void ValidateCandidate(CatalogProducerRegistration registration, CatalogAdmissionCandidate? candidate)
    {
        if (candidate?.Definition is not { } definition || candidate.Version is not { } version ||
            !CatalogDisclosureText.IsValid(definition.Id, 450) || !CatalogDisclosureText.IsValid(version.Id, 450) ||
            version.DefinitionId != definition.Id ||
            version.Definition?.Id != definition.Id || !ActivityCatalogDisclosureEvaluator.ValidOwner(candidate.DefinitionOwner) ||
            !ActivityCatalogDisclosureEvaluator.ValidOwner(candidate.VersionOwner) || candidate.DefinitionOwner != candidate.VersionOwner)
            throw new CatalogAdmissionException();
        if (registration.OwnerScope == CatalogProducerOwnerScope.Global)
        {
            if (candidate.DefinitionOwner.Kind != CatalogOwnerScopeKind.Global)
                throw new CatalogAdmissionException();
        }
        else
        {
            var current = ownership.Current;
            if (!ActivityCatalogDisclosureEvaluator.ValidOwner(current) || current.Kind != CatalogOwnerScopeKind.Tenant ||
                current != candidate.DefinitionOwner)
                throw new CatalogAdmissionException();
        }
    }
}
