using Elsa.Workflows.Design.Api.Commands;
using Elsa.Workflows.Design.Api.Handlers;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests.Unit;

public sealed class MoveWorkflowDefinitionsCommandHandlerTests
{
    [Fact]
    public async Task Moves_the_exact_deduplicated_selection_to_Unfiled_or_a_folder()
    {
        var command = new RecordingMoveCommand();
        var handler = new MoveWorkflowDefinitionsCommandHandler(command);

        await handler.Handle(new MoveWorkflowDefinitions(["one", "two"], "folder-1"), CancellationToken.None);

        Assert.Equal(["one", "two"], command.DefinitionIds);
        Assert.Equal("folder-1", command.FolderId);
    }

    [Fact]
    public async Task Rejects_invalid_selections_before_the_provider_command()
    {
        var command = new RecordingMoveCommand();
        var handler = new MoveWorkflowDefinitionsCommandHandler(command);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new MoveWorkflowDefinitions(["one", "one"]), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            handler.Handle(new MoveWorkflowDefinitions(["one"], new string('f', 65)), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            handler.Handle(new MoveWorkflowDefinitions(["one"], " "), CancellationToken.None));

        Assert.Null(command.DefinitionIds);
    }

    private sealed class RecordingMoveCommand : IMoveWorkflowDefinitionsCommand
    {
        public IReadOnlyCollection<string>? DefinitionIds { get; private set; }
        public string? FolderId { get; private set; }

        public Task Execute(IReadOnlyCollection<string> definitionIds, string? folderId, CancellationToken cancellationToken = default)
        {
            DefinitionIds = definitionIds;
            FolderId = folderId;
            return Task.CompletedTask;
        }
    }
}
