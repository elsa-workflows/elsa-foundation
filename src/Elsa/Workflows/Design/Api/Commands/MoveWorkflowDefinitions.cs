using System.Text.Json.Serialization;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Commands;

/// <summary>Moves one or more logical workflow definitions to a folder, or to Unfiled when <see cref="FolderId"/> is null.</summary>
public sealed record MoveWorkflowDefinitions(
    IReadOnlyCollection<string> DefinitionIds,
    [property: JsonRequired] string? FolderId) : ICommand;
