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

    [Fact]
    public void Material_registration_restores_the_exact_service_collection_when_backend_registration_fails()
    {
        var services = new ThrowingServiceCollection(
            descriptor => descriptor.ServiceType == typeof(RuntimeArtifactStoreBackend));
        services.Add(ServiceDescriptor.Singleton<object>(new object()));
        var before = services.ToArray();

        Assert.Throws<InvalidOperationException>(() => services.AddGroundworkV2RuntimeMaterials());

        Assert.Equal(before, services);
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

    private sealed class ThrowingServiceCollection(Func<ServiceDescriptor, bool> shouldThrow) : IServiceCollection
    {
        private readonly List<ServiceDescriptor> descriptors = [];

        public ServiceDescriptor this[int index] { get => descriptors[index]; set => descriptors[index] = value; }
        public int Count => descriptors.Count;
        public bool IsReadOnly => false;
        public void Add(ServiceDescriptor item)
        {
            if (shouldThrow(item))
                throw new InvalidOperationException("synthetic service registration failure");
            descriptors.Add(item);
        }
        public void Clear() => descriptors.Clear();
        public bool Contains(ServiceDescriptor item) => descriptors.Contains(item);
        public void CopyTo(ServiceDescriptor[] array, int arrayIndex) => descriptors.CopyTo(array, arrayIndex);
        public IEnumerator<ServiceDescriptor> GetEnumerator() => descriptors.GetEnumerator();
        public int IndexOf(ServiceDescriptor item) => descriptors.IndexOf(item);
        public void Insert(int index, ServiceDescriptor item)
        {
            if (shouldThrow(item))
                throw new InvalidOperationException("synthetic service registration failure");
            descriptors.Insert(index, item);
        }
        public bool Remove(ServiceDescriptor item) => descriptors.Remove(item);
        public void RemoveAt(int index) => descriptors.RemoveAt(index);
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
