using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.LayoutEntityTests;

/// <summary>
/// SC-004 + Unit C FR-007: both layout entities implement <see cref="IWorkflowDefinitionLayout"/>
/// — design-time consumers read via the contract without depending on
/// <c>Elsa.Workflows.Design.Persistence.Core</c>.
/// </summary>
public sealed class ReadContractTests
{
    [Fact]
    public void VersionLayout_implements_IWorkflowDefinitionLayout()
    {
        Assert.True(typeof(IWorkflowDefinitionLayout).IsAssignableFrom(typeof(WorkflowDefinitionVersionLayout)));
    }

    [Fact]
    public void DraftLayout_implements_IWorkflowDefinitionLayout()
    {
        Assert.True(typeof(IWorkflowDefinitionLayout).IsAssignableFrom(typeof(WorkflowDefinitionDraftLayout)));
    }

    [Fact]
    public void DesignMetadataRecord_implements_IDesignMetadataRecord()
    {
        Assert.True(typeof(IDesignMetadataRecord).IsAssignableFrom(typeof(DesignMetadataRecord)));
    }

    [Fact]
    public void Records_accessor_exposes_readonly_records_via_contract()
    {
        var record = new DesignMetadataRecord("node-1", 10, 20);
        var entity = new WorkflowDefinitionVersionLayout
        {
            Id = "layout-1",
            WorkflowDefinitionVersionId = "ver-1",
            Records = [record]
        };

        IWorkflowDefinitionLayout contractView = entity;
        var records = contractView.Records.ToArray();
        Assert.Single(contractView.Records);
        Assert.Equal("node-1", records[0].NodeId);
        Assert.Equal(10, records[0].X);
        Assert.Equal(20, records[0].Y);
    }

    [Fact]
    public void Layout_contract_lives_in_Workflows_Design_Core()
    {
        // §E2.2 + framework §2.1 — read contracts live in .Core; consumers must not need Persistence.Core.
        var contractAssembly = typeof(IWorkflowDefinitionLayout).Assembly.GetName().Name;
        Assert.Equal("Elsa.Workflows.Design.Core", contractAssembly);

        var entityAssembly = typeof(WorkflowDefinitionVersionLayout).Assembly.GetName().Name;
        Assert.Equal("Elsa.Workflows.Design.Persistence.Core", entityAssembly);
    }
}
