using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// A lightweight, persistence-agnostic <see cref="IActivityDefinition"/> produced by
/// <see cref="IActivityDefinitionFactory"/>. The persistence layer converts it to its concrete
/// entity via that entity's <c>From</c> method.
/// </summary>
public sealed record ActivityDefinitionModel(
    string Id,
    string ActivityTypeKey,
    string Category,
    string? DisplayName,
    string? Description
) : IActivityDefinition;
