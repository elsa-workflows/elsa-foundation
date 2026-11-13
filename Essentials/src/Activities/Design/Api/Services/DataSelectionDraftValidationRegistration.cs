using Elsa.Activities.Design.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>Preserves the host's inner validator lifetime while the analysis wrapper remains request-scoped.</summary>
internal sealed class DataSelectionDraftValidationRegistration(IServiceCollection services, object key) : IStartupTask
{
    private readonly IServiceCollection _services = services;
    private readonly object _key = key;
    private ServiceDescriptor _wrapper = null!;

    internal static void Register(IServiceCollection services)
    {
        var entries = services.Where(x => x.ServiceType == typeof(IActivityDraftValidator) && !x.IsKeyedService).ToArray();
        if (entries.Length != 1) throw new InvalidOperationException("Activity draft validation requires exactly one configured validator.");
        var original = entries[0];
        if (original.ImplementationFactory?.Target is DataSelectionDraftValidationRegistration current && ReferenceEquals(current._services, services))
            return;
        object key;
        if (original.ImplementationFactory?.Target is DataSelectionDraftValidationRegistration parent)
            key = parent._key; // Shell copies retain the keyed inner descriptor, not an inner request scope.
        else
        {
            key = new object();
            var type = typeof(IActivityDraftValidator);
            var inner = original.ImplementationInstance is { } instance ? ServiceDescriptor.KeyedSingleton(type, key, instance) :
                original.ImplementationFactory is { } factory ? ServiceDescriptor.DescribeKeyed(type, key, (provider, _) => factory(provider), original.Lifetime) :
                ServiceDescriptor.DescribeKeyed(type, key, original.ImplementationType!, original.Lifetime);
            services.Add(inner);
        }
        var registration = new DataSelectionDraftValidationRegistration(services, key);
        var wrapper = ServiceDescriptor.Describe(typeof(IActivityDraftValidator), registration.Create,
            original.Lifetime == ServiceLifetime.Transient ? ServiceLifetime.Transient : ServiceLifetime.Scoped);
        registration._wrapper = wrapper;
        services.Remove(original);
        services.Add(wrapper);
        // Replace a copied shell's checker with the configuring shell's descriptor inventory.
        foreach (var old in services.Where(x => x.ServiceType == typeof(IStartupTask) && !x.IsKeyedService &&
                     x.ImplementationInstance is DataSelectionDraftValidationRegistration).ToArray()) services.Remove(old);
        services.AddSingleton<IStartupTask>(registration);
        registration.Validate();
    }

    private IActivityDraftValidator Create(IServiceProvider provider)
    {
        Validate();
        var analyzers = provider.GetServices<IActivityDataSelectionAnalyzer>().ToArray();
        if (analyzers.Length != 1)
            throw new InvalidOperationException("Activity draft validation requires exactly one shared data selection analyzer.");
        return new ActivityDataSelectionDraftValidator(provider.GetRequiredKeyedService<IActivityDraftValidator>(_key),
            provider.GetServices<IActivityDataSelectionDocumentProjector>(), analyzers);
    }

    private void Validate()
    {
        var configured = _services.Where(x => x.ServiceType == typeof(IActivityDraftValidator) && !x.IsKeyedService).ToArray();
        if (configured.Length != 1 || !ReferenceEquals(configured[0], _wrapper))
            throw new InvalidOperationException("The mandatory activity data selection validator wrapper was replaced or registered ambiguously.");
    }

    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Validate();
        return Task.CompletedTask;
    }
}
