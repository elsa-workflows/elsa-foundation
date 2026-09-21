using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

/// <summary>
/// Lists catalog rows the publishing surface can construct, optionally filtered by stable Runtime consumer key.
/// </summary>
public sealed record ListConstructableActivities(string? ConsumerKey = null)
    : IRequest<IEnumerable<ConstructableActivityView>>;
