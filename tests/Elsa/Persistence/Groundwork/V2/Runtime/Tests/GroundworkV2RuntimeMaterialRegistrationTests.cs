using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2RuntimeMaterialRegistrationTests
{
    [Fact]
    public void Registration_declares_five_units_and_routes_each_public_contract_to_one_scoped_store()
    {
        var services = new ServiceCollection();
        services.AddGroundworkV2RuntimeMaterials();
        var registry = Assert.IsType<GroundworkStorageUnitRegistry>(services.Single(descriptor =>
            descriptor.ServiceType == typeof(GroundworkStorageUnitRegistry)).ImplementationInstance);
        Assert.All(
            new[]
            {
                ElsaRuntimeV2StorageManifest.WorkflowExecutableDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowExecutableCoordinationDocumentKind,
                ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateDocumentKind,
                ElsaRuntimeV2StorageManifest.ExecutableActivityTemplateHashClaimDocumentKind,
                ElsaRuntimeV2StorageManifest.WorkflowExecutableSourceReferenceDocumentKind
            },
            unitId => Assert.Equal(unitId, registry.Require(unitId).Unit.Id.Value));

        AssertScoped<GroundworkV2WorkflowExecutableStore>(services, typeof(IWorkflowExecutableStore));
        AssertScoped<GroundworkV2ExecutableActivityTemplateStore>(services, typeof(IExecutableActivityTemplateStore));
        AssertScoped<GroundworkV2WorkflowExecutableSourceReferenceStore>(services, typeof(IWorkflowExecutableSourceReferenceStore));
        AssertAlias(services, typeof(IExecutableActivityTemplateReader));
        AssertAlias(services, typeof(IExecutableActivityTemplateWriter));
        AssertAlias(services, typeof(IWorkflowExecutableSourceReferenceReader));
        AssertAlias(services, typeof(IWorkflowExecutableSourceReferenceWriter));
    }

    private static void AssertScoped<TImplementation>(IServiceCollection services, Type contract)
    {
        var implementation = Assert.Single(services, candidate => candidate.ServiceType == typeof(TImplementation));
        Assert.Equal(ServiceLifetime.Scoped, implementation.Lifetime);
        Assert.NotNull(implementation.ImplementationFactory);

        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == contract);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }

    private static void AssertAlias(IServiceCollection services, Type contract)
    {
        var descriptor = Assert.Single(services, candidate => candidate.ServiceType == contract);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.NotNull(descriptor.ImplementationFactory);
    }
}
