using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Publishing.Api.Models;

namespace Elsa.Workflows.Publishing.Api.Requests;

/// <summary>Lists safe incident-strategy descriptors and the effective publishing default.</summary>
public sealed record ListIncidentStrategies : IRequest<IncidentStrategiesResponse>;
