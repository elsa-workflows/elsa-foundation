using System.Text.Json;

namespace Elsa.Activities.Bpmn.Models;

/// <summary>The versioned, immutable model invocation captured by a real native activity wait.</summary>
public sealed record RunBpmnState(int FormatVersion, JsonElement Invocation);

/// <summary>The model-owned terminal result delivered to the activity's exact typed registration.</summary>
public sealed record RunBpmnCompletion(int FormatVersion, JsonElement Invocation, JsonElement Result);

/// <summary>The completed model result available to the surrounding native workflow.</summary>
public sealed record RunBpmnResult(string ExecutionId, JsonElement Result);
