using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Tests;

[Elsa.Activities.Runtime.Core.Attributes.Version("1.0.0")]
public sealed class GeneratedProducerActivity : Activity<string>
{
    protected override ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete("produced"));
}

[Elsa.Activities.Runtime.Core.Attributes.Version("1.0.0")]
[ActivityOutcome("Done")]
public sealed class GeneratedConsumerActivity : Activity<string>
{
    [ActivityInput(Key = "request", Order = 0)]
    [Required]
    public string Request { get; set; } = null!;

    [ActivityInput(Key = "retry", Order = 1)]
    [Required]
    public int Retry { get; set; }

    [ActivityInput(Key = "prior-result", Order = 2)]
    [Required]
    public string PriorResult { get; set; } = null!;

    [ActivityInput(Key = "formatted", Order = 3)]
    [Required]
    public string Formatted { get; set; } = null!;

    [ActivityInput(Key = "literal", Order = 4)]
    [Required]
    public string Literal { get; set; } = null!;

    [ActivityInput(Key = "fallback", Order = 5, DefaultValue = "contract-default", DefaultSyntax = "Literal")]
    public string? Fallback { get; set; }

    protected override ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(Literal));
}
