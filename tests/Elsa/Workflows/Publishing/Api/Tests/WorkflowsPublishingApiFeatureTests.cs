using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api.Contracts;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Events;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowsPublishingApiFeatureTests
{
    [Fact]
    public void RegistersPublishingRequestHandlers()
    {
        var services = new ServiceCollection();

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
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityDraftTestRunPublisher));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IActivityPublishingAuthorizationContext));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Events.Core.Contracts.IEventHandler<OnExecutableCompilationCollecting>) &&
            descriptor.ImplementationType == typeof(CollectExecutableCompilation));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(Elsa.Events.Core.Contracts.IEventHandler<OnExecutableNodeMetadataCollecting>) &&
            descriptor.ImplementationType == typeof(CollectExecutableNodeMetadata));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowTestRunStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPublicationSlotStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IPublicationPolicyStore));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(WorkflowPublicationPreflightReader));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(PublicationSnapshotReviewService));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(TimeProvider));
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IRequestHandler));
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
