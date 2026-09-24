namespace Elsa.Cli.Worker;

/// <summary>The EF-free, closed worker view of a host's version-2 inspection response.</summary>
internal sealed class HostContextInspectionResponse
{
    public int? Version { get; init; }
    public string? Status { get; init; }
    public int? ExitCode { get; init; }
    public string? Command { get; init; }
    public HostInspectionPayload? InspectContext { get; init; }
    public HostListPayload? List { get; init; }
    public HostPlanPayload? Plan { get; init; }
    public HostConfigurationContextFacts? ConfigurationContext { get; init; }
    public WorkerError? Error { get; init; }

    public string? Validate(string requestedCommand, int returnedExitCode)
    {
        if (Version != 2 || Command != requestedCommand || ExitCode != returnedExitCode)
            throw InvalidResponse();
        if (Status == "error" && returnedExitCode is >= ToolExitCode.Refusal and <= ToolExitCode.DatabaseFailure &&
            Error is { Code.Length: > 0, Message.Length: > 0 } && InspectContext is null && List is null && Plan is null)
            return null;
        if (requestedCommand == "list")
        {
            var listed = List?.Modules;
            var facts = ConfigurationContext;
            if (Status != "ok" || returnedExitCode != ToolExitCode.Success || Error is not null ||
                InspectContext is not null || Plan is not null || listed is null ||
                listed.Any(module => module is null || string.IsNullOrWhiteSpace(module.Module) ||
                    string.IsNullOrWhiteSpace(module.Assembly) || string.IsNullOrWhiteSpace(module.Context) ||
                    string.IsNullOrWhiteSpace(module.HistoryTable) || module.Providers is null ||
                    module.DependsOn is null || module.Providers.Any(string.IsNullOrWhiteSpace) ||
                    module.DependsOn.Any(string.IsNullOrWhiteSpace)) ||
                listed.Select(module => module.Module).Distinct(StringComparer.OrdinalIgnoreCase).Count() != listed.Count ||
                !ValidExplicitContext(facts))
                throw InvalidResponse();
            return null;
        }
        if (requestedCommand == "plan")
        {
            var planned = Plan;
            var entries = planned?.Modules;
            if (Status != "ok" || returnedExitCode != ToolExitCode.Success || Error is not null ||
                InspectContext is not null || List is not null ||
                planned?.Provider is not ("Sqlite" or "SqlServer" or "PostgreSql" or "MySql") ||
                entries is null || entries.Count == 0 ||
                entries.Select((entry, index) => entry is null || entry.Order != index + 1 ||
                    string.IsNullOrWhiteSpace(entry.Module) || string.IsNullOrWhiteSpace(entry.Assembly) ||
                    string.IsNullOrWhiteSpace(entry.Context) || string.IsNullOrWhiteSpace(entry.HistoryTable) ||
                    entry.From != "0" || entry.Count < 0 || entry.Ids is null ||
                    entry.Count != entry.Ids.Count || entry.DependsOn is null ||
                    entry.Ids.Any(string.IsNullOrWhiteSpace) || entry.DependsOn.Any(string.IsNullOrWhiteSpace))
                    .Any(invalid => invalid) ||
                entries.Select(entry => entry.Module).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count ||
                !ValidExplicitContext(ConfigurationContext))
                throw InvalidResponse();
            return null;
        }
        if (requestedCommand != "inspect-context")
            throw InvalidResponse();
        var context = ConfigurationContext;
        if (Status != "ok" || returnedExitCode != ToolExitCode.Success || Error is not null || List is not null || Plan is not null ||
            InspectContext?.Outcome is not ("no-resource-applicable" or "legacy-only") ||
            context?.Source is not ("workbench-json-v1" or "workbench-json-environment-v1") ||
            string.IsNullOrWhiteSpace(context.Environment) || context.Shell is not null || context.Resource is not null)
            throw InvalidResponse();

        if (context.Resolution != InspectContext.Outcome || context.TargetVerification != "not-performed" ||
            context.RuntimeParity != "unobserved" || context.Participants is null || context.Unresolved is null ||
            context.Unresolved.Any(string.IsNullOrWhiteSpace) ||
            context.Participants.Any(participant => participant is null || string.IsNullOrWhiteSpace(participant.Feature) ||
                string.IsNullOrWhiteSpace(participant.Module) || string.IsNullOrWhiteSpace(participant.Selection)))
            throw InvalidResponse();
        return InspectContext.Outcome;
    }

    private static WorkerRefusal InvalidResponse() => WorkerRefusal.Resolution(
        "context-capability-unavailable", "The selected host returned an invalid persistence context inspection response.");

    private static bool ValidExplicitContext(HostConfigurationContextFacts? facts) =>
        facts?.Source is "workbench-json-v1" or "workbench-json-environment-v1" &&
        !string.IsNullOrWhiteSpace(facts.Environment) && !string.IsNullOrWhiteSpace(facts.Shell) &&
        facts.Resolution is "resource" or "legacy" && facts.TargetVerification == "not-performed" &&
        facts.RuntimeParity == "unobserved" && facts.Participants is not null && facts.Unresolved is not null &&
        !facts.Unresolved.Any(string.IsNullOrWhiteSpace) &&
        !facts.Participants.Any(participant => participant is null || string.IsNullOrWhiteSpace(participant.Feature) ||
            string.IsNullOrWhiteSpace(participant.Module) || string.IsNullOrWhiteSpace(participant.Selection));
}

internal sealed class HostListPayload
{
    public IReadOnlyList<HostListModule>? Modules { get; init; }
}

internal sealed class HostListModule
{
    public string? Module { get; init; }
    public string? Assembly { get; init; }
    public string? Context { get; init; }
    public string? HistoryTable { get; init; }
    public IReadOnlyList<string>? DependsOn { get; init; }
    public IReadOnlyList<string>? Providers { get; init; }
}

internal sealed class HostPlanPayload
{
    public string? Provider { get; init; }
    public string? Schema { get; init; }
    public IReadOnlyList<HostPlanEntry>? Modules { get; init; }
}

internal sealed class HostPlanEntry
{
    public int Order { get; init; }
    public string? Module { get; init; }
    public string? Assembly { get; init; }
    public string? Context { get; init; }
    public string? HistoryTable { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
    public int Count { get; init; }
    public IReadOnlyList<string>? Ids { get; init; }
    public IReadOnlyList<string>? DependsOn { get; init; }
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
