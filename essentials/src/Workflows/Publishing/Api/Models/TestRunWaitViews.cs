using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record TestRunWaitsRequest(string TestRunId);
public sealed record TestRunWaitsView(IReadOnlyCollection<TestRunWaitView> Waits);
public sealed record TestRunWaitView(string WaitId, string NodeId, string Label, string? DriverId,
    bool IsAvailable, string? Reason, IReadOnlyCollection<TestRunTriggerMode> Modes);
public sealed record TestRunEventRequest(string TestRunId, string WaitId, string DeliveryId, string ModeId, JsonElement? Data = null);
public sealed record TestRunEventView(string Status, string? Reason = null);
