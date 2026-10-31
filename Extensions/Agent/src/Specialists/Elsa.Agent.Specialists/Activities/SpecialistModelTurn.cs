using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Services;

namespace Elsa.Agent.Specialists.Activities;

[BackgroundExecutionSafe]
[Activity(IsDiscoverable = false)]
internal sealed class SpecialistModelTurn(SpecialistNativeBinding binding, SpecialistTurnJournal journal, SpecialistModelRunner runner, TimeProvider time) : IActivity, IActivityResult<string>
{
    public async ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context)
    {
        var (identity, parent, node) = await binding.ParentAsync(context, context.CancellationToken);
        var handle = SpecialistNativeBinding.Handle(parent);
        var (document, request) = await journal.ReadAsync(handle, identity, context.CancellationToken);
        if (request.Contract.SchemaFingerprint != node.ActivityContract!.SchemaFingerprint || request.BindingFingerprint != parent.InputSnapshot!.BindingFingerprint)
            throw SpecialistModelRunner.Failure("specialist.native.binding", "The staged turn no longer matches its native business contract.");
        if (document.Status == SpecialistTurnStatus.Accepted)
        {
            _ = await journal.ResultAsync(handle, identity, context.InvocationId, context.CancellationToken);
            return ActivityTransition.Complete(handle);
        }
        document = await journal.ClaimAsync(handle, identity, context.InvocationId, time.GetUtcNow(), context.CancellationToken);
        var dispatched = false;
        try
        {
            using var deadline = new CancellationTokenSource(document.Deadline - time.GetUtcNow(), time);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, deadline.Token);
            linked.Token.ThrowIfCancellationRequested();
            if (!await journal.DispatchAsync(document, time.GetUtcNow(), linked.Token))
                throw SpecialistModelRunner.Failure("specialist.turn.fence", "The specialist turn cannot dispatch under this worker fence.");
            dispatched = true;
            var result = await runner.RunAsync(request, linked.Token);
            _ = await binding.ParentAsync(context, linked.Token);
            if (!await journal.AcceptAsync(document, result, time.GetUtcNow(), linked.Token))
                throw SpecialistModelRunner.Failure("specialist.turn.fence", "The specialist result cannot be accepted under this worker fence.");
            return ActivityTransition.Complete(handle);
        }
        catch (Exception exception)
        {
            var code = exception is SpecialistTurnException specialist ? specialist.Code : "specialist.turn.interrupted";
            await journal.FailAsync(document, code, dispatched && exception is not SpecialistTurnException, CancellationToken.None);
            return ActivityTransition.Fault<string>(new ActivityFault(code, "The specialist turn failed or was interrupted; inspect its scoped turn status before retrying.",
                category: dispatched && exception is not SpecialistTurnException ? "outcome-unknown" : null));
        }
    }
}
