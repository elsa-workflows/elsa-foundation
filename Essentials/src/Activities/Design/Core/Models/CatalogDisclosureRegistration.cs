namespace Elsa.Activities.Design.Core.Models;

/// <summary>Metadata contributed through the catalog initialization event; never a scoped instance.</summary>
public abstract record CatalogDisclosureRegistration;

/// <summary>The exact ordinal policy key and binding schema selecting a scoped strategy.</summary>
public sealed record CatalogDisclosurePolicyKey(string PolicyKey, string BindingSchema);

/// <summary>Static metadata for one keyed scoped policy implementation.</summary>
public sealed record CatalogDisclosurePolicyRegistration(
    CatalogDisclosurePolicyKey Key, Type PolicyType) : CatalogDisclosureRegistration;

/// <summary>How a trusted producer's actual definition/version ownership is checked.</summary>
public enum CatalogProducerOwnerScope
{
    Unspecified = 0,
    Global = 1,
    CurrentTenant = 2
}

/// <summary>Static types and ownership rules for one keyed scoped producer.</summary>
public sealed record CatalogProducerRegistration : CatalogDisclosureRegistration
{
    public CatalogProducerRegistration(string registrationId, Type producerType, Type admissionOwnerType,
        CatalogProducerOwnerScope ownerScope, CatalogDisclosurePolicyKey? minimumPolicy,
        IReadOnlyList<Type> aliases)
    {
        RegistrationId = registrationId;
        ProducerType = producerType;
        AdmissionOwnerType = admissionOwnerType;
        OwnerScope = ownerScope;
        MinimumPolicy = minimumPolicy;
        Aliases = Array.AsReadOnly(aliases.ToArray());
    }

    public string RegistrationId { get; }
    public Type ProducerType { get; }
    public Type AdmissionOwnerType { get; }
    public CatalogProducerOwnerScope OwnerScope { get; }
    public CatalogDisclosurePolicyKey? MinimumPolicy { get; }
    public IReadOnlyList<Type> Aliases { get; }
}
