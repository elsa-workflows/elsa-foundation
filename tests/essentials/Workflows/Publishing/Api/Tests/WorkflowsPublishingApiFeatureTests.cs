using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowsPublishingApiFeatureTests
{
    [Fact]
    public void RegistersPublishingRequestHandlers()
    {
        var services = new ServiceCollection();

        // spec 145: the workflow-publish + compile engine moved to the endpoint-free WorkflowsPublishing
        // feature, which the Api feature pulls in via DependsOn at shell-composition time. This unit test
        // invokes ConfigureServices directly (bypassing DependsOn resolution), so it composes both features
        // to assert the same publishing surface it always has (§2.21.1 wiring change; assertions unchanged).
        new WorkflowsPublishingFeature().ConfigureServices(services);
        new WorkflowsPublishingApiFeature().ConfigureServices(services);

        // TS-1 (§2.23.1): registration presence, not implementation-type pinning, so swapping an equivalent
        // implementation no longer breaks this test.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableStore));
        // ADR 0040: the transient store is retired; the test-run flow appends an expiring TestRun source reference
        // into the single content-addressed store, so the source-reference store is part of the publishing surface.
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableSourceReferenceStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableSourceReferenceReader));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IExecutableActivityTemplateReader));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowExecutableCompiler));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityDraftTestRunService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityPublishingAuthorizationContext));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Events.Core.Contracts.IEventHandler<ExecutableCompilationCollecting>) &&
            descriptor.ImplementationType == typeof(CollectExecutableCompilation));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Events.Core.Contracts.IEventHandler<ExecutableNodeMetadataCollecting>) &&
            descriptor.ImplementationType == typeof(CollectExecutableNodeMetadata));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTestRunStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowActivationAuthority));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowActivationCoordinator));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPublicationPolicyStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WorkflowPublicationPreflightReader));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(PublicationSnapshotReviewService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TimeProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRequestHandler));
        // The delete-preflight contribution: permanent definition deletion must veto while a publication is live.
        // It is contributed as the publication check specifically, which is what makes permanent deletion
        // available at all — a host without this feature refuses the operation (#1283).
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IWorkflowDefinitionPermanentDeletionGuard) &&
            typeof(Elsa.Workflows.Design.Persistence.Core.Contracts.IWorkflowDefinitionPublicationDeletionGuard)
                .IsAssignableFrom(descriptor.ImplementationType));
    }

    [Fact]
    public void Advertises_mutation_free_snapshot_preflight()
    {
        var link = Assert.Single(
            PublishingApiCapabilities.StaticDeclaration.Links,
            candidate => candidate.Rel == "publication-snapshot-preflight");

        Assert.Equal("publishing/workflows/preflight", link.Href);
        Assert.False(link.Templated);
    }

    [Fact]
    public void Records_its_in_memory_test_run_store_as_the_owner_only_when_it_added_it()
    {
        var composed = new ServiceCollection();
        new WorkflowsPublishingApiFeature().ConfigureServices(composed);
        var owner = PublishingPersistenceFamilyBackend.Find(composed, PublishingPersistenceFamilyBackend.ActivityDraftTestRuns);
        Assert.Equal(PublishingPersistenceFamilyBackend.InMemory, owner!.Name);
        Assert.True(owner.Owns(Assert.Single(composed, descriptor => descriptor.ServiceType == typeof(IActivityDraftTestRunStore))));

        // A host-provided store stays foreign, so a durable backend selected later refuses to replace it.
        var hostProvided = new ServiceCollection();
        hostProvided.AddSingleton<IActivityDraftTestRunStore, InMemoryActivityDraftTestRunStore>();
        new WorkflowsPublishingApiFeature().ConfigureServices(hostProvided);
        Assert.Null(PublishingPersistenceFamilyBackend.Find(hostProvided, PublishingPersistenceFamilyBackend.ActivityDraftTestRuns));
        Assert.Single(hostProvided, descriptor => descriptor.ServiceType == typeof(IActivityDraftTestRunStore));
    }

    [Fact]
    public void CompileRequest_PreservesPreTenantConstructorAndDeconstruction()
    {
        Assert.NotNull(typeof(WorkflowExecutableCompileRequest).GetConstructor(
        [
            typeof(string),
            typeof(WorkflowExecutableReferenceScope),
            typeof(DateTimeOffset),
            typeof(DateTimeOffset?),
            typeof(DateTimeOffset?),
            typeof(string),
            typeof(IReadOnlyDictionary<string, string>)
        ]));
        var request = new WorkflowExecutableCompileRequest(
            "version",
            WorkflowExecutableReferenceScope.Published,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            null,
            "artifact-",
            null,
            "tenant-a");
        var (versionId, _, _, _, _, artifactIdPrefix, _) = request;

        Assert.Equal("version", versionId);
        Assert.Equal("artifact-", artifactIdPrefix);
    }
}
