using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa3.Activities.Design.Import.Reconciliation;

/// <summary>
/// Registers <see cref="Elsa3ImportRun"/> and its collaborators inside a shell: the selection, the workflow publisher
/// and the clock it reads, and the startup publishing grant with the authorization decoration it switches on.
/// <see cref="Elsa3JsonReconciliationFeature"/> calls it, and so does the migration tooling's shell feature (spec 1237
/// research R-2), which does not enable the folder import.
/// </summary>
internal static class Elsa3ImportRunRegistration
{
    /// <summary>
    /// Adds the run's services once. Call it where <c>IActivityPublishingAuthorizationContext</c> is registered (the
    /// shell, with WorkflowsPublishingApi), because the decoration wraps that registration.
    /// </summary>
    public static void Add(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(Elsa3ImportRun)))
            return;

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<Elsa3ImportSelection>();
        services.AddScoped<ImportedWorkflowPublisher>();
        services.AddScoped<Elsa3StartupPublishingGrant>();
        Elsa3StartupPublishingAuthorization.Decorate(services);
        services.AddScoped<Elsa3ImportRun>();
    }
}
