using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2WorkflowAlterationRegistrationTests
{
    [Fact]
    public void Alteration_registration_declares_two_units_and_routes_the_public_contract_to_one_scoped_store()
    {
        var services = new ServiceCollection();
        services.AddGroundworkV2WorkflowAlterationStore();

        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.Equal(
            ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind,
            registry.Require(ElsaRuntimeV2StorageManifest.WorkflowAlterationPlanDocumentKind).Unit.Id.Value);
        Assert.Equal(
            ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind,
            registry.Require(ElsaRuntimeV2StorageManifest.WorkflowAlterationJobDocumentKind).Unit.Id.Value);

        var implementation = Assert.Single(services, descriptor =>
            descriptor.ServiceType == typeof(GroundworkV2WorkflowAlterationStore));
        Assert.Equal(ServiceLifetime.Scoped, implementation.Lifetime);
        Assert.NotNull(implementation.ImplementationFactory);

        var contract = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IWorkflowAlterationStore));
        Assert.Equal(ServiceLifetime.Scoped, contract.Lifetime);
        Assert.NotNull(contract.ImplementationFactory);
    }
}
