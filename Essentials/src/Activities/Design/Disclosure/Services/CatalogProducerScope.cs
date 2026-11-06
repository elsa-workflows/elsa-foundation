using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Reconciliation;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Disclosure.Services;

/// <summary>Memoizes identity, not authorization. Every DI scope owns its own opaque binding wrapper.</summary>
internal sealed class CatalogProducerScope(IServiceProvider services, CatalogDisclosureRegistry registry) : ICatalogProducerScope
{
    private readonly object gate = new();
    private readonly Dictionary<string, ProducerBinding> bindings = new(StringComparer.Ordinal);

    public object GetBinding(string registrationId)
    {
        try
        {
            if (!CatalogDisclosureText.IsValid(registrationId, 128))
                throw new CatalogAdmissionException();
            lock (gate)
            {
                var registration = registry.FindProducer(registrationId) ?? throw new CatalogAdmissionException();
                if (!bindings.TryGetValue(registrationId, out var binding))
                {
                    var producer = services.GetRequiredKeyedService(registration.ProducerType, registrationId);
                    binding = new ProducerBinding(producer);
                    ValidateAliases(registration, binding);
                    bindings.Add(registrationId, binding);
                }
                else
                    ValidateAliases(registration, binding);
                return binding;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (CatalogAdmissionException) { throw; }
        catch (Exception error) { throw new CatalogAdmissionException(error); }
    }

    public CatalogAdmissionOrigin GetOrigin(string registrationId) => new(registrationId, GetBinding(registrationId));

    private void ValidateAliases(CatalogProducerRegistration registration, ProducerBinding binding)
    {
        if (binding.Producer.GetType() != registration.ProducerType || registration.Aliases.Any(alias =>
                !ReferenceEquals(binding.Producer, services.GetRequiredKeyedService(alias, registration.RegistrationId))))
            throw new CatalogAdmissionException();
    }

    private sealed record ProducerBinding(object Producer);
}
