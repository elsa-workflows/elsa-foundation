namespace Elsa.Cli.Worker;

/// <summary>The EF-free, closed worker view of a host's version-2 inspection response.</summary>
internal sealed class HostContextInspectionResponse
{
    public int? Version { get; init; }
    public string? Status { get; init; }
    public int? ExitCode { get; init; }
    public string? Command { get; init; }
    public HostInspectionPayload? InspectContext { get; init; }
    public HostConfigurationContextFacts? ConfigurationContext { get; init; }
    public WorkerError? Error { get; init; }

    public string? Validate(string requestedCommand, int returnedExitCode)
    {
        if (Version != 2 || Command != requestedCommand || ExitCode != returnedExitCode)
            throw InvalidResponse();
        if (Status == "error" && returnedExitCode is >= ToolExitCode.Refusal and <= ToolExitCode.DatabaseFailure &&
            Error is { Code.Length: > 0, Message.Length: > 0 } && InspectContext is null)
            return null;
        if (requestedCommand != "inspect-context")
            throw InvalidResponse();
        var context = ConfigurationContext;
        if (Status != "ok" || returnedExitCode != ToolExitCode.Success || Error is not null ||
            InspectContext?.Outcome is not ("no-resource-applicable" or "legacy-only") ||
            context?.Source is not ("workbench-json-v1" or "workbench-json-environment-v1") ||
            string.IsNullOrWhiteSpace(context.Environment) || context.Shell is not null || context.Resource is not null)
            throw InvalidResponse();

        if (context.Resolution != InspectContext.Outcome || context.TargetVerification != "not-performed" ||
            context.RuntimeParity != "unobserved" || context.Participants is null || context.Unresolved is null ||
            context.Unresolved.Any(string.IsNullOrWhiteSpace) ||
            context.Participants.Any(participant => string.IsNullOrWhiteSpace(participant.Feature) ||
                string.IsNullOrWhiteSpace(participant.Module) || string.IsNullOrWhiteSpace(participant.Selection)))
            throw InvalidResponse();
        return InspectContext.Outcome;
    }

    private static WorkerRefusal InvalidResponse() => WorkerRefusal.Resolution(
        "context-capability-unavailable", "The selected host returned an invalid persistence context inspection response.");
}

internal sealed class HostInspectionPayload
{
    public string? Outcome { get; init; }
}

internal sealed class HostConfigurationContextFacts
{
    public string? Source { get; init; }
    public string? Environment { get; init; }
    public string? Shell { get; init; }
    public string? Resource { get; init; }
    public string? Resolution { get; init; }
    public string? TargetVerification { get; init; }
    public string? RuntimeParity { get; init; }
    public IReadOnlyList<HostContextParticipant>? Participants { get; init; }
    public IReadOnlyList<string>? Unresolved { get; init; }
}

internal sealed class HostContextParticipant
{
    public string? Feature { get; init; }
    public string? Module { get; init; }
    public string? Resource { get; init; }
    public string? Provider { get; init; }
    public string? ConnectionReference { get; init; }
    public string? Selection { get; init; }
}
