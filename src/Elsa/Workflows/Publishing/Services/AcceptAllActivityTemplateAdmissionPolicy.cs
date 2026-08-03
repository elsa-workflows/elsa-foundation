using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

public sealed class AcceptAllActivityTemplateAdmissionPolicy : IActivityTemplateAdmissionPolicy
{
    public ValueTask<ActivityAdmissionDecision> EvaluateAsync(
        ActivityResourceMeasurements measurements,
        ActivityAdmissionContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ActivityAdmissionDecision(true, []));
    }
}
