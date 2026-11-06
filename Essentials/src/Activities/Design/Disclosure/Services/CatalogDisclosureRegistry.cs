using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Disclosure.Services;

/// <summary>Application-wide immutable metadata only; no service provider or scoped object is retained.</summary>
internal sealed class CatalogDisclosureRegistry
{
    private readonly object gate = new();
    private IReadOnlyDictionary<CatalogDisclosurePolicyKey, CatalogDisclosurePolicyRegistration>? policies;
    private IReadOnlyDictionary<string, CatalogProducerRegistration>? producers;

    public void Initialize(IEnumerable<CatalogDisclosureRegistration> registrations)
    {
        var nextPolicies = new Dictionary<CatalogDisclosurePolicyKey, CatalogDisclosurePolicyRegistration>();
        var nextProducers = new Dictionary<string, CatalogProducerRegistration>(StringComparer.Ordinal);
        try
        {
            foreach (var registration in registrations)
            {
                switch (registration)
                {
                    case CatalogDisclosurePolicyRegistration policy:
                        if (!ValidPolicyKey(policy.Key) || !Concrete(policy.PolicyType, typeof(IActivityCatalogDisclosurePolicy)) ||
                            !nextPolicies.TryAdd(policy.Key, policy))
                            throw new InvalidOperationException("A catalog policy registration is invalid or duplicated.");
                        break;
                    case CatalogProducerRegistration producer:
                        if (!ValidKey(producer.RegistrationId, 128) || !Concrete(producer.ProducerType, typeof(object)) ||
                            !Concrete(producer.AdmissionOwnerType, typeof(IActivityCatalogAdmissionOwner)) ||
                            producer.OwnerScope is not (CatalogProducerOwnerScope.Global or CatalogProducerOwnerScope.CurrentTenant) ||
                            producer.MinimumPolicy is not null && !ValidPolicyKey(producer.MinimumPolicy) ||
                            producer.Aliases.Count == 0 || producer.Aliases.Distinct().Count() != producer.Aliases.Count ||
                            producer.Aliases.Any(alias => alias is null || !alias.IsAssignableFrom(producer.ProducerType)) ||
                            !producer.Aliases.Contains(producer.ProducerType) ||
                            !nextProducers.TryAdd(producer.RegistrationId, producer))
                            throw new InvalidOperationException("A catalog producer registration is invalid or duplicated.");
                        // Snapshot a contributor-owned descriptor before retaining it.
                        nextProducers[producer.RegistrationId] = new(producer.RegistrationId, producer.ProducerType,
                            producer.AdmissionOwnerType, producer.OwnerScope, producer.MinimumPolicy, producer.Aliases);
                        break;
                    default:
                        throw new InvalidOperationException("An unsupported catalog registration was contributed.");
                }
            }
            lock (gate)
            {
                if (policies is not null || producers is not null)
                    throw new InvalidOperationException("The catalog disclosure registry was already initialized.");
                policies = nextPolicies;
                producers = nextProducers;
            }
        }
        catch (CatalogDisclosureUnavailableException) { throw; }
        catch (Exception error)
        {
            throw new CatalogDisclosureUnavailableException(error);
        }
    }

    public void EnsureInitialized()
    {
        lock (gate)
            if (policies is null || producers is null)
                throw new CatalogDisclosureUnavailableException(
                    new InvalidOperationException("The catalog disclosure registry has not been initialized."));
    }

    public CatalogDisclosurePolicyRegistration? FindPolicy(CatalogDisclosurePolicyKey key)
    {
        EnsureInitialized();
        return policies!.GetValueOrDefault(key);
    }

    public CatalogProducerRegistration? FindProducer(string registrationId)
    {
        EnsureInitialized();
        return registrationId is null ? null : producers!.GetValueOrDefault(registrationId);
    }

    public IReadOnlyCollection<CatalogDisclosurePolicyRegistration> Policies
    {
        get { EnsureInitialized(); return policies!.Values.ToArray(); }
    }

    public IReadOnlyCollection<CatalogProducerRegistration> Producers
    {
        get { EnsureInitialized(); return producers!.Values.ToArray(); }
    }

    internal static bool ValidPolicyKey(CatalogDisclosurePolicyKey? key) =>
        key is not null && ValidKey(key.PolicyKey, 128) && ValidKey(key.BindingSchema, 64);

    private static bool ValidKey(string? value, int maximum) =>
        CatalogDisclosureText.IsValid(value, maximum);

    private static bool Concrete(Type? type, Type contract) =>
        type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false } && contract.IsAssignableFrom(type);
}
