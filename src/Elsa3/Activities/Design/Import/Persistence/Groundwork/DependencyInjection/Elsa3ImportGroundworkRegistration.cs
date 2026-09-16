using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa3.Activities.Design.Import.Composition;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Persistence.Groundwork.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa3.Activities.Design.Import.Persistence.Groundwork.DependencyInjection;

/// <summary>Registers, repeats, or explicitly switches the Elsa 3 import persistence to Groundwork.</summary>
public static class Elsa3ImportGroundworkRegistration
{
    public static IServiceCollection AddElsa3ImportGroundworkPersistence(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var configuration = Elsa3ImportPersistenceBackend.Fingerprint(Elsa3ImportPersistenceBackend.Groundwork);
        var snapshot = services.ToArray();
        // The storage catalog is shared with other lanes and lives outside the service collection, so a
        // failed switch must restore it as well as the descriptors.
        var registrationSnapshots = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<IRuntimePersistenceRegistrationState>()
            .Select(state => state.CaptureSnapshot())
            .ToArray();

        try
        {
            var existingBackend = Elsa3ImportPersistenceBackend.Find(services);
            if (existingBackend is not null)
            {
                existingBackend.EnsureOwnsRegisteredDescriptors(services);
                if (existingBackend.Name == Elsa3ImportPersistenceBackend.Groundwork)
                {
                    if (!existingBackend.HasConfiguration(Elsa3ImportPersistenceBackend.Groundwork, configuration))
                        throw new InvalidOperationException("Elsa 3 import Groundwork persistence is already registered with different options.");
                    return services;
                }

                existingBackend.RemoveOwnedDescriptors(services);
            }

            if (Elsa3ImportPersistenceBackend.HasUnownedReplacementContract(services))
                throw new InvalidOperationException("An Elsa 3 import store or command is already registered; remove it explicitly before selecting Groundwork.");

            var units = Elsa3ImportStorageManifest.CreateUnits();
            foreach (var unit in units)
                services.AddGroundworkStorageUnit(unit);
            // The design storage and the management projection writer are shared with the design lanes,
            // so they are supplied only as defaults and never counted among this backend's registrations.
            services.TryAddScoped<GroundworkDesignStorage>(provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                auditSink: provider.GetService<IGroundworkPrivilegedQueryAuditSink>()));
            services.TryAddScoped<GroundworkActivityManagementProjectionWriter>();

            var registrationStart = services.Count;
            services.AddScoped<IReusableActivityImportOperationStore, GroundworkReusableActivityImportOperationStore>();
            services.AddScoped<IReusableActivityImportCommand, GroundworkReusableActivityImportCommand>();
            Elsa3ImportPersistenceBackend.Register(services, new Elsa3ImportPersistenceBackend(
                Elsa3ImportPersistenceBackend.Groundwork,
                configuration,
                services.Skip(registrationStart).ToArray(),
                collection =>
                {
                    foreach (var unit in units)
                        collection.RemoveGroundworkStorageUnit(unit.Id.Value);
                }));
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            foreach (var registrationSnapshot in registrationSnapshots)
                registrationSnapshot.Rollback();
            throw;
        }
    }
}
