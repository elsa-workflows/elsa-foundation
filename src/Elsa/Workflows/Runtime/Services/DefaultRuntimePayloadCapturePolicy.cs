using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class DefaultRuntimePayloadCapturePolicy : IRuntimePayloadCapturePolicy
{
    public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsSensitive)
            return new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Sensitive runtime payloads are excluded by default.");

        return request.Subject switch
        {
            RuntimePayloadCaptureSubject.Incident => new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.MetadataOnly, "Incident history captures metadata by default, not payload."),
            RuntimePayloadCaptureSubject.Diagnostic => new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.MetadataOnly, "Diagnostics capture metadata by default, not payload."),
            RuntimePayloadCaptureSubject.WorkflowInput => OmitInputOutput(),
            RuntimePayloadCaptureSubject.WorkflowOutput => OmitInputOutput(),
            RuntimePayloadCaptureSubject.ActivityInput => OmitInputOutput(),
            RuntimePayloadCaptureSubject.ActivityOutput => OmitInputOutput(),
            RuntimePayloadCaptureSubject.DurableValue => new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Durable value history snapshots require explicit capture policy."),
            _ => new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Unknown runtime payload subject is not captured by default.")
        };
    }

    private static RuntimePayloadCaptureDecision OmitInputOutput() =>
        new(RuntimePayloadCaptureMode.None, "Input and output snapshots are omitted by default.");
}
