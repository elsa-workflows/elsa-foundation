using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class DefaultRuntimePayloadCapturePolicy : IRuntimePayloadCapturePolicy
{
    private readonly IRuntimeDiagnosticsSettingsAccessor? _settingsAccessor;

    public DefaultRuntimePayloadCapturePolicy()
    {
    }

    public DefaultRuntimePayloadCapturePolicy(IRuntimeDiagnosticsSettingsAccessor settingsAccessor)
    {
        ArgumentNullException.ThrowIfNull(settingsAccessor);

        _settingsAccessor = settingsAccessor;
    }

    public RuntimePayloadCaptureDecision Decide(RuntimePayloadCaptureRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.IsSensitive)
            return new RuntimePayloadCaptureDecision(RuntimePayloadCaptureMode.None, "Sensitive runtime payloads are excluded by default.");

        var level = _settingsAccessor?.GetEffectiveLevel(request.Subject) ?? DefaultLevel(request.Subject);
        var mode = RuntimeDiagnosticsSettingsResolver.ToCaptureMode(level);

        return new RuntimePayloadCaptureDecision(mode, CaptureReason(mode));
    }

    private static RuntimeDiagnosticsEvidenceLevel DefaultLevel(RuntimePayloadCaptureSubject subject) =>
        subject switch
        {
            RuntimePayloadCaptureSubject.WorkflowInput => RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot,
            RuntimePayloadCaptureSubject.WorkflowOutput => RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot,
            RuntimePayloadCaptureSubject.ActivityInput => RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot,
            RuntimePayloadCaptureSubject.ActivityOutput => RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot,
            RuntimePayloadCaptureSubject.Incident => RuntimeDiagnosticsEvidenceLevel.Metadata,
            RuntimePayloadCaptureSubject.Diagnostic => RuntimeDiagnosticsEvidenceLevel.Metadata,
            RuntimePayloadCaptureSubject.DurableValue => RuntimeDiagnosticsEvidenceLevel.Metadata,
            RuntimePayloadCaptureSubject.ContainerVariable => RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot,
            _ => RuntimeDiagnosticsEvidenceLevel.Off
        };

    private static string CaptureReason(RuntimePayloadCaptureMode mode) =>
        mode switch
        {
            RuntimePayloadCaptureMode.None => "Runtime diagnostics policy did not capture value evidence.",
            RuntimePayloadCaptureMode.MetadataOnly => "Runtime diagnostics policy captured metadata only.",
            RuntimePayloadCaptureMode.DiagnosticSnapshot => "Diagnostic snapshot captured by runtime diagnostics policy.",
            RuntimePayloadCaptureMode.Payload => "Full payload captured by runtime diagnostics policy.",
            _ => "Runtime diagnostics policy did not capture value evidence."
        };
}
