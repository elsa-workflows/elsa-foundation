namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Declared actual semantic owners; capture independently qualifies the selected source chain.</summary>
public sealed class RuntimeHistorySourceSupport
{
    public RuntimeHistorySourceSupport(IReadOnlyList<RuntimeHistorySourceFactKind> kinds, bool timerOwner = false)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        if (kinds.Any(kind => !Enum.IsDefined(kind))) throw new ArgumentException("Unknown history source kind.", nameof(kinds));
        Kinds = Array.AsReadOnly(kinds.Distinct().ToArray());
        TimerOwner = timerOwner;
    }
    public IReadOnlyList<RuntimeHistorySourceFactKind> Kinds { get; }
    public bool TimerOwner { get; }
    public string? SourceIntentKind { get; private init; }
    public static RuntimeHistorySourceSupport ForSourceOwner(string intentKind, IReadOnlyList<RuntimeHistorySourceFactKind> kinds)
    {
        RuntimePostCommitIntent.ValidateKind(intentKind, nameof(intentKind));
        return new(kinds) { SourceIntentKind = intentKind };
    }
}
