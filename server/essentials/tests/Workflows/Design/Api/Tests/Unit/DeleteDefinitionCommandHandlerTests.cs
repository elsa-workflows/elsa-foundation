using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Xunit;
using Elsa.Workflows.Design.Api.Endpoints.Definitions.Delete;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

/// <summary>
/// Behavioral tests for <see cref="Handler"/>: it forwards the target id to the
/// permanent-delete lifecycle command, which removes the definition together with its versions, current Draft,
/// and layout. Individual published versions are immutable and have no delete endpoint, so there is no
/// version-level delete path to test here.
/// </summary>
public sealed class DeleteDefinitionCommandHandlerTests
{
    [Fact]
    public async Task Forwards_id_to_permanent_delete_command()
    {
        var deleteCommand = new RecordingDeleteCommand();
        var handler = new Handler(deleteCommand);

        await handler.Handle(new DeleteDefinition("delete-request-1", "def-1"), CancellationToken.None);

        Assert.Equal("def-1", deleteCommand.LastId);
        Assert.Equal(new DesignOperationKey("delete-request-1"), deleteCommand.OperationKey);
    }

    private sealed class RecordingDeleteCommand : IDeleteWorkflowDefinitionPermanentlyCommand
    {
        public string? LastId { get; private set; }
        public DesignOperationKey? OperationKey { get; private set; }

        public Task Execute(
            DesignOperationKey operationKey,
            string definitionId,
            CancellationToken cancellationToken = default)
        {
            OperationKey = operationKey;
            LastId = definitionId;
            return Task.CompletedTask;
        }
    }
}
