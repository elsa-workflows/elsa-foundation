using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Primitives.Activities;

/// <summary>
/// Returns a deliberate typed fault. The runtime commits its normalized fault record and blocking
/// incident without exposing exception control flow to the activity author or host caller.
/// </summary>
/// <remarks>
/// The four inputs mirror Elsa 3's <c>Fault</c> so a ported graph can classify a fault the same way
/// (#1117). <see cref="Code"/> becomes the durable record's fault code; <see cref="Category"/> and
/// <see cref="FaultType"/> ride as classification metadata on the same record. All four are optional:
/// leaving them unset reproduces the previous behaviour exactly — code <c>workflow.fault</c>, the generic
/// message, and no classification.
/// </remarks>
public sealed class Fault : Activity<ActivityUnit>
{
    /// <summary>The fault code used when <see cref="Code"/> is unset.</summary>
    public const string DefaultCode = "workflow.fault";

    /// <summary>The fault message used when <see cref="Message"/> is unset.</summary>
    public const string DefaultMessage = "The workflow faulted.";

    /// <summary>The fault code recorded on the incident. Defaults to <see cref="DefaultCode"/> when unset.</summary>
    [ActivityInput(Key = "code", Order = 0)]
    public string? Code { get; set; }

    /// <summary>The fault message recorded on the incident. Defaults to <see cref="DefaultMessage"/> when unset.</summary>
    [ActivityInput(Key = "message", Order = 10)]
    public string? Message { get; set; }

    /// <summary>An optional classification for the fault, recorded as fault metadata.</summary>
    [ActivityInput(Key = "category", Order = 20)]
    public string? Category { get; set; }

    /// <summary>An optional fault type naming the family this fault belongs to, recorded as fault metadata.</summary>
    [ActivityInput(Key = "faultType", Order = 30)]
    public string? FaultType { get; set; }

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Fault<ActivityUnit>(new ActivityFault(
            Coalesce(Code, DefaultCode),
            Coalesce(Message, DefaultMessage),
            isRetryable: false,
            category: Category,
            faultType: FaultType)));

    private static string Coalesce(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
