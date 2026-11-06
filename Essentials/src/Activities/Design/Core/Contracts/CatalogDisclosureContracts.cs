using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;

namespace Elsa.Activities.Design.Core.Contracts;

/// <summary>Replacement contract: exactly one scoped evaluator in a composed catalog.</summary>
/// <remarks>Uses canonical resource facts and current host authority; no result is a retained grant.</remarks>
public interface IActivityCatalogDisclosureEvaluator
{
    /// <exception cref="CatalogDisclosureUnavailableException">A composed owner could not decide; the cause is retained inside.</exception>
    ValueTask<IReadOnlyList<CatalogDisclosureDecision>> EvaluateAsync(
        IReadOnlyList<CatalogDisclosureResource> resources, CatalogDisclosurePurpose purpose,
        CancellationToken cancellationToken = default);
}

/// <summary>A keyed strategy for one policy key/schema, evaluated against current host authority.</summary>
public interface IActivityCatalogDisclosurePolicy
{
    ValueTask<CatalogDisclosureDecision> EvaluateAsync(
        CatalogDisclosureResource resource, CatalogDisclosurePolicyRequirement requirement,
        CatalogDisclosurePurpose purpose, CancellationToken cancellationToken = default);
}

/// <summary>Replacement contract: exactly one scoped trusted producer admission service.</summary>
/// <remarks>Provisioning admission is independent of interactive disclosure authorization.</remarks>
public interface IActivityCatalogAdmission
{
    /// <exception cref="CatalogAdmissionException">The material or current producer origin could not be admitted.</exception>
    ValueTask<CatalogDisclosureRequirement> AdmitAsync(
        CatalogAdmissionOrigin origin, CatalogAdmissionCandidate candidate,
        CancellationToken cancellationToken = default);

    /// <exception cref="CatalogAdmissionException">The current admission differs from the retained requirement.</exception>
    ValueTask ValidateAsync(CatalogAdmissionOrigin origin, CatalogAdmissionCandidate candidate,
        CatalogDisclosureRequirement expected, CancellationToken cancellationToken = default);
}

/// <summary>A registered producer's keyed classification strategy, not interactive authorization.</summary>
public interface IActivityCatalogAdmissionOwner
{
    ValueTask<CatalogDisclosureRequirement> ClassifyAsync(
        CatalogAdmissionCandidate candidate, CancellationToken cancellationToken = default);
}

/// <summary>Returns static registration metadata for the catalog's named initialization event.</summary>
public interface IActivityCatalogDisclosureRegistrationSource
{
    IEnumerable<CatalogDisclosureRegistration> Read();
}

/// <summary>Replacement contract: exactly one producer-binding context per service scope.</summary>
public interface ICatalogProducerScope
{
    /// <summary>Gets an opaque binding belonging to this scope and exact registration.</summary>
    object GetBinding(string registrationId);

    /// <summary>Creates server-only origin evidence for this scope and exact registration.</summary>
    CatalogAdmissionOrigin GetOrigin(string registrationId);
}

/// <summary>Replacement contract: trusted current provisioning ownership, never an authored tenant switch.</summary>
public interface IActivityCatalogOwnerScopeAccessor
{
    CatalogOwnerScope Current { get; }
}

/// <summary>Replacement contract: whether a trusted current disclosure identity is present.</summary>
/// <remarks>Presence is only availability. It grants no source use, policy permission or workload authority.</remarks>
public interface IActivityCatalogDisclosureAuthority
{
    bool IsPresent { get; }
}

/// <summary>A catalog owner could not decide; the fixed outward message contains no resource details.</summary>
public sealed class CatalogDisclosureUnavailableException(Exception? innerException = null)
    : Exception(FixedMessage, innerException)
{
    public const string Code = "activities.catalog.disclosure-unavailable";
    public const string FixedMessage = "The activity catalog disclosure decision is unavailable.";
}

/// <summary>Trusted admission refused; the fixed outward message contains no candidate or source details.</summary>
public sealed class CatalogAdmissionException(Exception? innerException = null)
    : Exception(FixedMessage, innerException)
{
    public const string Code = "activities.catalog.admission-refused";
    public const string FixedMessage = "The activity catalog material could not be admitted.";
}
