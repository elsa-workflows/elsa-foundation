using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeDiagnosticsSettingsStore
{
    Task<RuntimeDiagnosticsSettings?> LoadAsync(string scope, CancellationToken cancellationToken = default);

    Task<RuntimeDiagnosticsSettings> SaveAsync(RuntimeDiagnosticsSettings settings, CancellationToken cancellationToken = default);
}

public interface IRuntimeDiagnosticsSettingsAccessor
{
    RuntimeDiagnosticsSettingsView Current { get; }

    RuntimeDiagnosticsEvidenceLevel GetEffectiveLevel(RuntimePayloadCaptureSubject subject);
}
