using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Commands.Core;

public sealed record AddDefinitionCommand(string UniqueName, string Category, string? DisplayName, string? Description, bool IsBrowsable) 
    : ICommand;
