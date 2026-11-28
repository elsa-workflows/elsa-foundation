using System.ComponentModel;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Decisions.Core;

namespace Elsa.Decisions.Workflows.Runtime.Activities;

/// <summary>The published Evaluate Decision contract. Actor Foundation executes it from the exact compiled executable.</summary>
// Preserve the previous declaring assembly's category in existing catalog hashes.
[Category("Runtime")]
[Activity(Icon = "TableProperties")]
[ActivityOutcome("Done")]
[ActivityOutcome("NoMatch")]
[ActivityOutcome("Refused")]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
[RefusesSecretBinding(nameof(Decision), SecretBindingRefusalReason.FixedAtPublish)]
[RefusesSecretBinding(nameof(Inputs), SecretBindingRefusalReason.EchoedToOutput)]
public sealed class EvaluateDecision : Activity<DecisionEvaluation>
{
    public const string ActivityType = Kernel.Native.EvaluateDecision.ActivityType;

    [ActivityInput(UIHint = "decision-version"), Required]
    public string Decision { get; set; } = string.Empty;

    [ActivityInput(DefaultValue = "{}", DefaultSyntax = "Object")]
    public Dictionary<string, DecisionValue> Inputs { get; set; } = new(StringComparer.Ordinal);

    [ActivityInput(DefaultValue = "1000000", DefaultSyntax = "Literal")]
    public int OperationBudget { get; set; } = 1_000_000;

    protected override ValueTask<ActivityTransition<DecisionEvaluation>> ExecuteAsync(ActivityExecutionContext context) =>
        throw new NotSupportedException("Evaluate Decision requires execution through the Actor Foundation workflow model.");
}
