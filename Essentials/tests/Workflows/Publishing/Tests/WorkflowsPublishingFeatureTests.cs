using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Tests;

/// <summary>
/// Registration test for the endpoint-free publish engine. Composing the
/// <see cref="WorkflowsPublishingFeature"/> alone must yield a fully wired workflow-publish + compile
/// surface, and NOTHING from the transport boundary: no authorization context, no HTTP endpoints.
/// This is what makes the engine reusable by a headless runtime node (the PR2 enabler).
/// </summary>
public sealed class WorkflowsPublishingFeatureTests
{
    // The authorization context stays in the Api feature (Decision 3). Referenced by full name so this
    // test project needs no reference to …Publishing.Api — the engine's independence is structural.
    private const string AuthorizationContextTypeName =
        "Elsa.Workflows.Publishing.Api.Contracts.IActivityPublishingAuthorizationContext";

    private const string FastEndpointsAssemblyName = "Elsa.Api.FastEndpoints";

    private static ServiceCollection ComposeEngine()
    {
        var services = new ServiceCollection();
        new WorkflowsPublishingFeature().ConfigureServices(services);
        return services;
    }

    [Fact]
    public void Records_its_in_memory_record_and_receipt_stores_as_owners_only_when_it_added_them()
    {
        var services = ComposeEngine();
        Assert.Equal(PublishingPersistenceFamilyBackend.InMemory,
            PublishingPersistenceFamilyBackend.Find(services, PublishingPersistenceFamilyBackend.PublicationRecords)!.Name);
        Assert.Equal(PublishingPersistenceFamilyBackend.InMemory,
            PublishingPersistenceFamilyBackend.Find(services, PublishingPersistenceFamilyBackend.ActivityPublicationReceipts)!.Name);

        // A host-provided record store stays foreign; the receipt default it did not provide is still claimed.
        var hostProvided = new ServiceCollection();
        hostProvided.AddSingleton<IPublicationRecordStore>(new Services.InMemoryPublicationRecordStore());
        new WorkflowsPublishingFeature().ConfigureServices(hostProvided);
        Assert.Null(PublishingPersistenceFamilyBackend.Find(hostProvided, PublishingPersistenceFamilyBackend.PublicationRecords));
        Assert.Single(hostProvided, descriptor => descriptor.ServiceType == typeof(IPublicationRecordStore));
        Assert.NotNull(PublishingPersistenceFamilyBackend.Find(hostProvided, PublishingPersistenceFamilyBackend.ActivityPublicationReceipts));
    }

    // Kept beside the composition test: asserts what the engine must not register, the transport's authorization context.
    [Fact]
    public void Does_not_register_the_transport_authorization_context()
    {
        var services = ComposeEngine();

        // The engine is authorization-free. No neutral default is introduced; the context is
        // supplied only by the transport (Api) feature.
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.FullName == AuthorizationContextTypeName);
    }

    // Kept beside the composition test: asserts what the engine must not register, FastEndpoints transport types.
    [Fact]
    public void Does_not_register_any_FastEndpoints_publish_endpoints()
    {
        var services = ComposeEngine();

        // No transport is mounted by the engine. Nothing it registers originates in the
        // FastEndpoints transport assembly.
        Assert.DoesNotContain(services, descriptor =>
            descriptor.ServiceType.Assembly.GetName().Name == FastEndpointsAssemblyName ||
            descriptor.ImplementationType?.Assembly.GetName().Name == FastEndpointsAssemblyName);
    }

    [Fact]
    public void Does_not_reference_the_transport_assemblies_at_all()
    {
        // The two absence assertions above are satisfied by this project's reference list as much as by the
        // engine's own registrations, so they cannot fail while the engine stays reference-clean. This is the
        // assertion that can: it binds the engine assembly itself, so a future `using` that drags the transport
        // back into the engine fails here instead of silently re-coupling the two.
        var referenced = typeof(WorkflowsPublishingFeature).Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name)
            .ToArray();

        Assert.DoesNotContain("Elsa.Workflows.Publishing.Api", referenced);
        Assert.DoesNotContain(FastEndpointsAssemblyName, referenced);
    }
}
