using Elsa.Activities.Bpmn.Contracts;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Activities;

/// <summary>Runs one pinned model through the direct BPMN lifecycle and waits for its durable result.</summary>
[Activity(Icon = "Workflow")]
[ActivityOutcome("Done")]
[RefusesSecretBinding(nameof(Model), SecretBindingRefusalReason.FixedAtPublish)]
public sealed class RunBpmn(IBpmnModelActivityRuntime runtime) : StatefulActivity<RunBpmnResult, RunBpmnState, RunBpmnCompletion>
{
    public const string ActivityType = BpmnActivityIdentities.RunType;

    [ActivityInput(Key = nameof(Model), Order = 0)]
    [Required]
    public string Model { get; set; } = string.Empty;

    protected override ValueTask<ActivityTransition<RunBpmnResult, RunBpmnState>> ExecuteAsync(ActivityExecutionContext context) => runtime.ExecuteAsync(context, Model);

    [ResumeTarget(BpmnEmbeddingBoundary.ResumeKey)]
    protected override ValueTask<ActivityTransition<RunBpmnResult, RunBpmnState>> ResumeAsync(ActivityResumeContext<RunBpmnState, RunBpmnCompletion> context) => runtime.ResumeAsync(context);
}
