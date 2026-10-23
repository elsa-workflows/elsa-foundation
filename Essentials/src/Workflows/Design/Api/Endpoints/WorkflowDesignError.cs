namespace Elsa.Workflows.Design.Api;

/// <summary>
/// The design API error body.
/// </summary>
/// <remarks>
/// The shape is part of the published HTTP contract and is pinned by the module's compatibility
/// baselines. It is written by <see cref="Endpoints.WorkflowDesignProblemWriter"/>.
/// </remarks>
internal sealed record WorkflowDesignError(
    IReadOnlyDictionary<string, string[]> Errors,
    string Message,
    int StatusCode);
