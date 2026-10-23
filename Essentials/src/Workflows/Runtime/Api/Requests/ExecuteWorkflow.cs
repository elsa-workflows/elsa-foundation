using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

/// <summary>
/// Starts a published workflow by artifact id. <see cref="Inputs"/> carries caller-supplied workflow inputs
/// (name → JSON value) threaded into the start dispatch so <c>input.*</c> expressions resolve to them (#286);
/// null/empty when the caller supplies none. <see cref="SourceReferenceId"/> disambiguates multiple live publications;
/// the server validates it against the route artifact and derives all version facts from the matched reference.
/// The artifact id is bound from the route, the remaining values from the body.
/// </summary>
public sealed record ExecuteWorkflow(
    string ArtifactId,
    IReadOnlyDictionary<string, JsonElement>? Inputs = null,
    string? SourceReferenceId = null)
    : IRequest<WorkflowExecutionStartDispatchView>;
