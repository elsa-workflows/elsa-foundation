using System.Reflection;
using Elsa.Tagging.Core.Contracts;
using Elsa.Workflows.Design.Api.Capabilities;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class WorkflowDefinitionTagCapabilityTests
{
    [Fact]
    public async Task Operational_capability_advertises_tag_contract_only_when_both_stores_and_durable_catalog_are_active()
    {
        var active = new WorkflowDesignOperationalCapabilitySource(
            workflowDefinitionTags: DispatchProxy.Create<IWorkflowDefinitionTagStore, NoopProxy>(),
            tagDefinitions: DispatchProxy.Create<ITagDefinitionStore, NoopProxy>(),
            tagCatalogPersistence: new DurableCatalogPersistence());
        var inactive = new WorkflowDesignOperationalCapabilitySource(
            workflowDefinitionTags: DispatchProxy.Create<IWorkflowDefinitionTagStore, NoopProxy>());
        var ephemeral = new WorkflowDesignOperationalCapabilitySource(
            workflowDefinitionTags: DispatchProxy.Create<IWorkflowDefinitionTagStore, NoopProxy>(),
            tagDefinitions: DispatchProxy.Create<ITagDefinitionStore, NoopProxy>());

        var capability = Assert.Single(await active.GetCapabilitiesAsync());
        var link = Assert.Single(capability.Links, candidate => candidate.Rel == "workflow-definition-tags");
        Assert.Equal("design/workflows/definitions/{definitionId}/tags", link.Href);
        Assert.True(link.Templated);
        Assert.Empty(await inactive.GetCapabilitiesAsync());
        Assert.Empty(await ephemeral.GetCapabilitiesAsync());
    }

    private class NoopProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Capability discovery must not invoke persistence stores.");
    }

    private sealed class DurableCatalogPersistence : ITagDefinitionCatalogPersistence;
}
