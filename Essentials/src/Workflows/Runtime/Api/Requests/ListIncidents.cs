using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

public sealed record ListIncidents(string WorkflowExecutionId, bool BlockingOnly = false) : IRequest<ListIncidentsResponse>;

public sealed record ListIncidentsResponse(bool WorkflowExists, IReadOnlyCollection<IncidentStateView> Incidents, int Count);
