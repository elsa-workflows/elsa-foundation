using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class InMemoryRuntimeDiagnosticsSettingsStore : IRuntimeDiagnosticsSettingsStore, IRuntimeDiagnosticsSettingsAccessor
{
    private readonly ConcurrentDictionary<string, RuntimeDiagnosticsSettings> _settings = new(StringComparer.Ordinal);

    public RuntimeDiagnosticsSettingsView Current => Resolve(RuntimeDiagnosticsSettings.HostDefaultScope);

    public RuntimeDiagnosticsEvidenceLevel GetEffectiveLevel(RuntimePayloadCaptureSubject subject) =>
        RuntimeDiagnosticsSettingsResolver.GetEffectiveLevel(Current, subject);

    public Task<RuntimeDiagnosticsSettings?> LoadAsync(string scope, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        return Task.FromResult(_settings.TryGetValue(scope, out var settings) ? Clone(settings) : null);
    }

    public Task<RuntimeDiagnosticsSettings> SaveAsync(RuntimeDiagnosticsSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var normalized = RuntimeDiagnosticsSettingsResolver.Normalize(settings);
        _settings[normalized.Scope] = normalized;
        return Task.FromResult(Clone(normalized));
    }

    private RuntimeDiagnosticsSettingsView Resolve(string scope)
    {
        var settings = _settings.TryGetValue(scope, out var savedSettings)
            ? savedSettings
            : RuntimeDiagnosticsSettings.Default;
        return RuntimeDiagnosticsSettingsResolver.Resolve(settings);
    }

    private static RuntimeDiagnosticsSettings Clone(RuntimeDiagnosticsSettings settings) =>
        new()
        {
            Scope = settings.Scope,
            DefaultLevel = settings.DefaultLevel,
            SubjectOverrides = settings.SubjectOverrides.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            UpdatedAt = settings.UpdatedAt,
            UpdatedBy = settings.UpdatedBy
        };
}
