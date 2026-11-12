using System.Text.Json.Serialization;

namespace Elsa.Workflows.Design.Api;

/// <summary>
/// The design API error body.
/// </summary>
/// <remarks>
/// The shape is part of the published HTTP contract and is pinned by the module's compatibility
/// baselines. It is written by <see cref="Endpoints.WorkflowDesignProblemWriter"/>.
/// <see cref="ErrorCode"/> is additive: only <see cref="Endpoints.WorkflowDesignFaultRenderer"/> sets it,
/// for a coded failure, and it is omitted from the wire otherwise, so every existing body is unchanged.
/// </remarks>
internal sealed record WorkflowDesignError(
    IReadOnlyDictionary<string, string[]> Errors,
    string Message,
    int StatusCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ErrorCode = null);
