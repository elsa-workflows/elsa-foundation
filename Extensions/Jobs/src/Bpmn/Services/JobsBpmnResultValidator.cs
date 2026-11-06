using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;

namespace Elsa.Jobs.Bpmn.Services;

/// <summary>Checks the prior durable Jobs grant; it never creates permission or changes either authority.</summary>
internal sealed class JobsBpmnResultValidator(IBpmnNativeWorkConsumer consumer, IJobNativeDeliveryInspector inspector,
    IJobQueries queries) : IBpmnNativeWorkResultValidator
{
    public async ValueTask ValidateAsync(BpmnNativeWorkResultValidation context, CancellationToken cancellationToken = default)
    {
        try
        {
            var evidence = await inspector.InspectWorkerAsync(context.Worker, cancellationToken);
            // Unrelated workers and native/root cancellation without a consumed selected JobResult
            // retain BPMN's existing physical settlement checks and require no Jobs continuation.
            if (evidence is null) return;
            var address = await consumer.ResolveAsync(context.Origin, cancellationToken);
            JobsBpmnBoundary.RequireEvidence(address, evidence, context.Result);
            if (context.InputId != context.Origin.ResultInputId || context.InputId != evidence.Completion.ModelInputId ||
                string.IsNullOrWhiteSpace(context.ContentFingerprint)) throw JobsBpmnBoundary.Invalid();
            var completion = evidence.Completion;
            var grant = await queries.FindContinuationAsync(evidence.Address.Scope, completion.JobId, completion.ContinuationId, cancellationToken);
            if (grant is null || grant.JobId != completion.JobId || grant.ResultId != completion.ResultId ||
                grant.ContinuationId != completion.ContinuationId || grant.ModelInputId != context.InputId ||
                !JsonElement.DeepEquals(JobsBpmnBoundary.Write(grant), completion.Permission)) throw JobsBpmnBoundary.Invalid();
        }
        catch (BpmnModelValidationException error)
        { throw new JobException("jobs.bpmn.invalid", "The Jobs result's retained BPMN owner is invalid.", error); }
        catch (NotSupportedException error)
        { throw new RuntimeSchedulerWorkUnavailableException("jobs.bpmn.result-unavailable", "The selected BPMN owner proof provider is unavailable.", error); }
        catch (JobException error) when (error.Code is "jobs.persistence.unavailable" or "jobs.bpmn.unavailable" or "jobs.native.evidence-unavailable")
        { throw new RuntimeSchedulerWorkUnavailableException("jobs.bpmn.result-unavailable", "The existing Jobs continuation permission is temporarily unavailable.", error); }
    }
}
