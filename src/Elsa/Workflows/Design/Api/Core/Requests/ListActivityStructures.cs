using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Api.Models;

namespace Elsa.Workflows.Design.Api.Requests;

/// <summary>
/// Requests the registry of composite-activity structure kinds registered in the active shell.
/// Backs <c>GET design/workflows/structures</c> for headless authoring clients.
/// </summary>
public sealed record ListActivityStructures : IRequest<ActivityStructuresResponse>;
