using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

/// <summary>
/// Lists the catalog rows the publishing surface could construct, with their descriptor kind.
/// Optionally filter by <paramref name="DescriptorType"/> (e.g. to show only the Workflow rows).
/// </summary>
public sealed record ListConstructableActivities(string? DescriptorType = null)
    : IRequest<IEnumerable<ConstructableActivityView>>;
