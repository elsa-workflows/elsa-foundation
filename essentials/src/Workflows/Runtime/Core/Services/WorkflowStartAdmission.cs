using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

internal static class WorkflowStartAdmission
{
    internal static async ValueTask EnforceStartPolicyAsync(
        WorkflowExecutionStartDispatchRequest request,
        WorkflowExecutableIdentity executable,
        WorkflowExecutionPartition partition,
        IWorkflowExecutableStartPolicy startPolicy,
        CancellationToken cancellationToken)
    {
        var context = new WorkflowExecutableStartPolicyContext(
            executable,
            request.StartAuthority?.Kind ?? WorkflowExecutableStartAuthorityKind.LiveReference,
            request.RequestedBy,
            request.Authority,
            request.RunKind,
            request.TenantId,
            partition,
            request.DispatchNestingDepth);
        var decision = await startPolicy.EvaluateAsync(context, cancellationToken)
            ?? throw new InvalidOperationException($"{nameof(IWorkflowExecutableStartPolicy)} returned no decision.");
        if (!decision.IsAllowed)
        {
            throw new WorkflowExecutableStartRejectedException(
                WorkflowExecutableStartRejectionCategory.Policy,
                decision.ReasonCode!,
                decision.Message!);
        }
    }

    // Every start applies its executable's input contract before the command is enqueued (#3728): declared defaults fill
    // omitted inputs, and a blank, unknown, duplicate or mistyped input or a missing required one refuses the start. A
    // legacy artifact without a contract, or a dispatcher composed without a validator, keeps the supplied inputs.
    internal static IReadOnlyDictionary<string, JsonElement> ApplyInputContract(WorkflowExecutable executable,
        IReadOnlyDictionary<string, object?> inputs, IWorkflowExecutableInputValidator? inputValidator)
    {
        var supplied = WorkflowExecutionStartCommandPayload.ToJsonValues(inputs);
        if (executable.InputContract is not { } contract || inputValidator is null)
            return supplied;

        var validation = inputValidator.Validate(contract, supplied);
        if (!validation.IsValid)
        {
            throw new WorkflowExecutableStartRejectedException(
                WorkflowExecutableStartRejectionCategory.Input,
                validation.Findings.First().ReasonCode,
                string.Join(" ", validation.Findings.Select(finding => finding.Message)));
        }

        return validation.NormalizedInputs;
    }
}
