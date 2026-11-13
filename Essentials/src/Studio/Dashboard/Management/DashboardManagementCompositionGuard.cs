using CShells.Lifecycle;
using Elsa.Studio.Dashboard.Core.Authoring;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Resources;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Dashboard.Management;

internal sealed class DashboardManagementCompositionGuard(IServiceCollection services) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Require(typeof(IDashboardQueryService), required: true);
        Require(typeof(IDashboardResourceService));
        Require(typeof(IDashboardDefinitionValidator));
        Require(typeof(IDashboardResourceStore));
        Require(typeof(IDashboardActorContext));
        Require(typeof(IDashboardAudienceDirectory));
        Require(typeof(IDashboardAuthoringService));
        return Task.CompletedTask;
    }

    private void Require(Type contract, bool required = false)
    {
        var count = services.Count(descriptor => descriptor.ServiceType == contract);
        if (count > 1 || required && count == 0)
            throw new InvalidOperationException($"{(required ? "Exactly one" : "At most one")} {contract.Name} must be registered for Dashboard Management; found {count}.");
    }
}
