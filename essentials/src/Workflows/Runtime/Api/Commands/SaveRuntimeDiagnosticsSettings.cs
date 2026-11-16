using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Commands;

public sealed record SaveRuntimeDiagnosticsSettings(
    string? Scope,
    RuntimeDiagnosticsEvidenceLevel DefaultLevel,
    IReadOnlyDictionary<string, RuntimeDiagnosticsEvidenceLevel>? SubjectOverrides
) : ICommand<RuntimeDiagnosticsSettingsView>;
