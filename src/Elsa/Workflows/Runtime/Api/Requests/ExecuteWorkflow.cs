using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

/// <summary>
/// Starts a published workflow by artifact id. <see cref="Inputs"/> carries caller-supplied workflow inputs
/// (name → JSON value) threaded into the start dispatch so <c>input.*</c> expressions resolve to them (#286);
/// null/empty when the caller supplies none. The artifact id is bound from the route, the inputs from the body.
/// </summary>
public sealed record ExecuteWorkflow(string ArtifactId, IReadOnlyDictionary<string, JsonElement>? Inputs = null)
    : IRequest<WorkflowExecutionStartDispatchView>;
