using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

/// <summary>Locks down the temporary EF Core behavioral oracle for the literal design commands.</summary>
public sealed class WorkflowDesignMutationOracleCommandTests
{
    [Fact]
    public async Task AddVersion_allocates_successive_major_versions_under_the_definition_lock()
    {
        using var host = WorkflowsDesignTestHost.Create();
        await host.EnsureDefinition("definition-1");
        var state = new WorkflowDefinitionState([], null, [], [], null);

        using var scope = host.Services.CreateScope();
        var command = scope.ServiceProvider.GetRequiredService<IAddWorkflowDefinitionVersionCommand>();

        var first = await command.Execute(WorkflowsDesignTestHost.TestOperationKey, "definition-1", state);
        var second = await command.Execute(WorkflowsDesignTestHost.TestOperationKey, "definition-1", state);

        Assert.Equal(("definition-1", "1.0.0"), (first.DefinitionId, first.Version));
        Assert.Equal(("definition-1", "2.0.0"), (second.DefinitionId, second.Version));
        Assert.NotEqual(first.VersionId, second.VersionId);
    }

    [Fact]
    public async Task Materialize_commands_preserve_source_supplied_identities()
    {
        using var host = WorkflowsDesignTestHost.Create();
        var definition = new WorkflowDefinition
        {
            Id = "source-definition",
            Name = "From source",
            IsSourceOwned = true,
        };
        var version = new WorkflowDefinitionVersion(definition.Id, "4.2.0")
        {
            Id = "source-version",
            State = new WorkflowDefinitionState([], null, [], [], null),
        };

        using (var scope = host.Services.CreateScope())
        {
            var materializeDefinition = scope.ServiceProvider.GetRequiredService<IMaterializeWorkflowDefinitionCommand>();
            var materializeVersion = scope.ServiceProvider.GetRequiredService<IMaterializeWorkflowDefinitionVersionCommand>();

            Assert.Equal(definition.Id, await materializeDefinition.Execute(WorkflowsDesignTestHost.TestOperationKey, definition));
            var result = await materializeVersion.Execute(WorkflowsDesignTestHost.TestOperationKey, version);
            Assert.Equal((definition.Id, version.Id, version.Version), (result.DefinitionId, result.VersionId, result.Version));
        }

        await using var dbContext = host.CreateContext();
        Assert.True(await dbContext.WorkflowDefinitions.AnyAsync(x => x.Id == definition.Id && x.IsSourceOwned));
        Assert.True(await dbContext.WorkflowDefinitionVersions.AnyAsync(x => x.Id == version.Id && x.Version == version.Version));
    }
}
