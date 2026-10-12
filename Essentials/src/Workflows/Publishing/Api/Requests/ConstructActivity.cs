using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

/// <summary>
/// Construct the live activity for a catalog row. <paramref name="ActivityId"/> is the
/// <c>ActivityDefinitionVersion</c> id (the catalog row id).
/// </summary>
public sealed record ConstructActivity(string ActivityId) : IRequest<ConstructedActivityView>;
