using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>Guards the organizational boundary: folders are persistence metadata, never authored content.</summary>
public sealed class WorkflowFolderBoundaryTests
{
    [Fact]
    public void Folder_placement_is_absent_from_the_source_facing_and_authored_models()
    {
        Assert.Null(typeof(IWorkflowDefinition).GetProperty("FolderId"));
        Assert.Null(typeof(WorkflowDefinitionState).GetProperty("FolderId"));
        Assert.NotNull(typeof(WorkflowDefinition).GetProperty("FolderId"));
    }
}
