using Elsa.Workflows.Runtime.Api.Commands;
using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeDiagnosticsSettingsApiTests
{
    private readonly InMemoryRuntimeDiagnosticsSettingsStore _settingsStore = new();
    private readonly GetRuntimeDiagnosticsSettingsRequestHandler _getHandler;
    private readonly SaveRuntimeDiagnosticsSettingsCommandHandler _saveHandler;

    public RuntimeDiagnosticsSettingsApiTests()
    {
        _getHandler = new GetRuntimeDiagnosticsSettingsRequestHandler(_settingsStore);
        _saveHandler = new SaveRuntimeDiagnosticsSettingsCommandHandler(_settingsStore);
    }

    [Fact]
    public async Task GetSettings_ReturnsDiagnosticSnapshotsByDefaultWithHostPayloadCap()
    {
        var view = await _getHandler.Handle(new GetRuntimeDiagnosticsSettings(), CancellationToken.None);

        Assert.Equal(RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot, view.Requested.DefaultLevel);
        Assert.Equal(RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot, view.Effective.DefaultLevel);
        Assert.Equal(RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot, view.HostPolicy.MaximumLevel);
        Assert.False(view.Permissions.CanEnableFullPayloads);
        Assert.Empty(view.Effective.LimitationReasons);
    }

    [Fact]
    public async Task SaveSettings_CapsPayloadRequestToHostMaximumInEffectiveSettings()
    {
        var view = await _saveHandler.Handle(
            new SaveRuntimeDiagnosticsSettings(
                Scope: null,
                DefaultLevel: RuntimeDiagnosticsEvidenceLevel.Payload,
                SubjectOverrides: new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>
                {
                    [RuntimeDiagnosticsSubjects.ActivityInputs] = RuntimeDiagnosticsEvidenceLevel.Payload,
                    [RuntimeDiagnosticsSubjects.DurableValues] = RuntimeDiagnosticsEvidenceLevel.Payload
                }),
            CancellationToken.None);

        Assert.Equal(RuntimeDiagnosticsEvidenceLevel.Payload, view.Requested.DefaultLevel);
        Assert.Equal(RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot, view.Effective.DefaultLevel);
        Assert.Equal(RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot, view.Effective.SubjectOverrides[RuntimeDiagnosticsSubjects.ActivityInputs]);
        Assert.Equal(RuntimeDiagnosticsEvidenceLevel.Metadata, view.Effective.SubjectOverrides[RuntimeDiagnosticsSubjects.DurableValues]);
        Assert.Contains(view.Effective.LimitationReasons, reason => reason.Contains("Default diagnostics level", StringComparison.Ordinal));
        Assert.Contains(view.Effective.LimitationReasons, reason => reason.Contains("Durable values", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RuntimePayloadCapturePolicy_UsesSavedEffectiveRuntimeDiagnosticsSettings()
    {
        await _settingsStore.SaveAsync(new RuntimeDiagnosticsSettings
        {
            DefaultLevel = RuntimeDiagnosticsEvidenceLevel.Metadata,
            SubjectOverrides = new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>
            {
                [RuntimeDiagnosticsSubjects.ActivityOutputs] = RuntimeDiagnosticsEvidenceLevel.Off
            }
        });
        IRuntimePayloadCapturePolicy policy = new DefaultRuntimePayloadCapturePolicy(_settingsStore);

        var inputDecision = policy.Decide(NewCaptureRequest(RuntimePayloadCaptureSubject.ActivityInput));
        var outputDecision = policy.Decide(NewCaptureRequest(RuntimePayloadCaptureSubject.ActivityOutput));

        Assert.Equal(RuntimePayloadCaptureMode.MetadataOnly, inputDecision.Mode);
        Assert.Equal(RuntimePayloadCaptureMode.None, outputDecision.Mode);
    }

    [Fact]
    public async Task RuntimePayloadCapturePolicy_AppliesInheritedSubjectHostCaps()
    {
        await _settingsStore.SaveAsync(new RuntimeDiagnosticsSettings
        {
            DefaultLevel = RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot,
            SubjectOverrides = new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>()
        });
        IRuntimePayloadCapturePolicy policy = new DefaultRuntimePayloadCapturePolicy(_settingsStore);

        var durableDecision = policy.Decide(NewCaptureRequest(RuntimePayloadCaptureSubject.DurableValue));

        Assert.Equal(RuntimePayloadCaptureMode.MetadataOnly, durableDecision.Mode);
    }

    [Fact]
    public async Task SaveSettings_RejectsUnknownSubjects()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _saveHandler.Handle(
                new SaveRuntimeDiagnosticsSettings(
                    Scope: null,
                    DefaultLevel: RuntimeDiagnosticsEvidenceLevel.DiagnosticSnapshot,
                    SubjectOverrides: new Dictionary<string, RuntimeDiagnosticsEvidenceLevel> { ["unknown"] = RuntimeDiagnosticsEvidenceLevel.Metadata }),
                CancellationToken.None));
    }

    [Fact]
    public async Task SaveSettings_RejectsUnknownDefaultLevel()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _saveHandler.Handle(
                new SaveRuntimeDiagnosticsSettings(
                    Scope: null,
                    DefaultLevel: (RuntimeDiagnosticsEvidenceLevel)999,
                    SubjectOverrides: new Dictionary<string, RuntimeDiagnosticsEvidenceLevel>()),
                CancellationToken.None));
    }

    private static RuntimePayloadCaptureRequest NewCaptureRequest(RuntimePayloadCaptureSubject subject) =>
        new(
            subject: subject,
            workflowExecutionId: "wfexec-1",
            requestedAt: new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero),
            activityExecutionId: "actexec-1",
            valueName: "payload");
}
