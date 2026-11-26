using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Services;
using Elsa.Workflows.Publishing.Api;
using Elsa.Workflows.Publishing.Api.Capabilities;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed class WorkflowsPublishingApiFeatureTests
{
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
