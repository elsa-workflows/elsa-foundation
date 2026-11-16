using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record DraftTestRunOptionsView(
    string ArtifactHash,
    WorkflowExecutableInputContract? InputContract,
    IReadOnlyCollection<TestRunTriggerOptionView> Options,
    IReadOnlyCollection<TestRunOptionsDiagnostic> Diagnostics);

public sealed record TestRunTriggerOptionView(
    string OptionId,
    string? DriverId,
    string NodeId,
    string Label,
    bool IsAvailable,
    string? Reason,
    JsonElement? Configuration,
    IReadOnlyCollection<TestRunTriggerMode> Modes);

public sealed record TestRunOptionsDiagnostic(
    string Code,
    string Message,
    string? NodeId = null);
