using Elsa.Workflows.Runtime.Api.Commands;
using Elsa.Workflows.Runtime.Api.Contracts;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Api.Handlers;

/// <summary>
/// Reads and saves the runtime diagnostics settings. The view's <c>permissions.canManage</c> is the caller's own
/// <c>workflows/runtime:manage</c> grant, asked of <see cref="IWorkflowRuntimeOperatorAccess"/>; without one it is
/// <see langword="false"/> (#3130). It is a display hint: saving still requires the permission.
/// </summary>
public sealed class RuntimeDiagnosticsSettingsService(
    IRuntimeDiagnosticsSettingsStore settingsStore,
    IWorkflowRuntimeOperatorAccess? operatorAccess = null) : IRuntimeDiagnosticsSettingsService
{
    public async Task<RuntimeDiagnosticsSettingsView> GetAsync(GetRuntimeDiagnosticsSettings request, CancellationToken cancellationToken)
    {
        var scope = string.IsNullOrWhiteSpace(request.Scope)
            ? RuntimeDiagnosticsSettings.HostDefaultScope
            : request.Scope;
        var settings = await settingsStore.LoadAsync(scope, cancellationToken) ?? RuntimeDiagnosticsSettings.Default;

        return RuntimeDiagnosticsSettingsResolver.Resolve(settings, permissions: await PermissionsAsync(cancellationToken));
    }

    public async Task<RuntimeDiagnosticsSettingsView> SaveAsync(SaveRuntimeDiagnosticsSettings command, CancellationToken cancellationToken)
    {
        var settings = new RuntimeDiagnosticsSettings
        {
            Scope = string.IsNullOrWhiteSpace(command.Scope)
                ? RuntimeDiagnosticsSettings.HostDefaultScope
                : command.Scope,
            DefaultLevel = command.DefaultLevel,
            SubjectOverrides = command.SubjectOverrides ?? new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>(StringComparer.Ordinal),
            UpdatedAt = DateTimeOffset.UtcNow
        };

        var savedSettings = await settingsStore.SaveAsync(settings, cancellationToken);
        return RuntimeDiagnosticsSettingsResolver.Resolve(savedSettings, permissions: await PermissionsAsync(cancellationToken));
    }

    private async ValueTask<RuntimeDiagnosticsPermissions> PermissionsAsync(CancellationToken cancellationToken) =>
        new() { CanManage = operatorAccess is not null && await operatorAccess.CanManageRuntimeAsync(cancellationToken) };
}

/// <summary>The diagnostics-settings read and save operations the runtime endpoints dispatch to.</summary>
public interface IRuntimeDiagnosticsSettingsService
{
    Task<RuntimeDiagnosticsSettingsView> GetAsync(GetRuntimeDiagnosticsSettings request, CancellationToken cancellationToken);
    Task<RuntimeDiagnosticsSettingsView> SaveAsync(SaveRuntimeDiagnosticsSettings command, CancellationToken cancellationToken);
}
