using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Api.Commands;

/// <summary>
/// The HTTP-surface command behind <c>DELETE design/workflows/definitions/{id}</c>. It permanently removes a
/// workflow definition and everything owned by it (its versions, current Draft, and layout) through the
/// <c>IDeleteWorkflowDefinitionPermanentlyCommand</c> lifecycle command. Published <em>versions</em> are
/// immutable and are never individually deletable — a version only disappears when its owning definition is
/// permanently deleted here (which is why <c>Versions/Delete</c> does not exist as an endpoint).
/// </summary>
public sealed record DeleteDefinition(string Id) : ICommand;
