using Elsa.Activities.Design.Core.Models;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Design.Api.Models;

/// <summary>Shared RFC 7807 extension shape for every reusable-activity Design failure.</summary>
public sealed record ActivityProblemDetailsView(
    string Type,
    string Title,
    int Status,
    string Detail,
    string Instance,
    string ErrorCode,
    string TraceId,
    IReadOnlyList<ActivityDiagnostic> Diagnostics,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ActivityRecoveryView? Recovery = null);

public sealed record ActivityRecoveryView(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? CurrentRevision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Relation = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Href = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Instruction = null);
