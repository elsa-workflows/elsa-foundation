using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models.Alterations;

/// <summary>One deliberately value-free reason a migration cannot safely reinterpret retained runtime state.</summary>
public sealed record WorkflowMigrationCompatibilityFinding
{
    public WorkflowMigrationCompatibilityFinding(string code, string? subjectId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (subjectId is not null && string.IsNullOrWhiteSpace(subjectId))
            throw new ArgumentException("A migration finding subject cannot be blank when supplied.", nameof(subjectId));
        Code = code;
        SubjectId = subjectId;
    }

    public string Code { get; }
    public string? SubjectId { get; }
}

/// <summary>Value-free result of validating an exact executable migration target.</summary>
public sealed record WorkflowMigrationCompatibilityReport
{
    public WorkflowMigrationCompatibilityReport(
        bool isCompatible,
        WorkflowExecutableIdentity source,
        WorkflowExecutableIdentity target,
        IReadOnlyCollection<WorkflowMigrationCompatibilityFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(findings);
        var snapshot = findings
            .OrderBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.SubjectId, StringComparer.Ordinal)
            .ToArray();
        if (isCompatible != (snapshot.Length == 0))
            throw new ArgumentException("Migration compatibility must agree with its findings.", nameof(isCompatible));
        IsCompatible = isCompatible;
        Source = source;
        Target = target;
        Findings = snapshot;
    }

    public bool IsCompatible { get; }
    public WorkflowExecutableIdentity Source { get; }
    public WorkflowExecutableIdentity Target { get; }
    public IReadOnlyCollection<WorkflowMigrationCompatibilityFinding> Findings { get; }

    public static WorkflowMigrationCompatibilityReport Compatible(WorkflowExecutableIdentity source, WorkflowExecutableIdentity target) =>
        new(true, source, target, []);

    public static WorkflowMigrationCompatibilityReport Incompatible(
        WorkflowExecutableIdentity source,
        WorkflowExecutableIdentity target,
        IEnumerable<WorkflowMigrationCompatibilityFinding> findings) =>
        new(false, source, target, findings.ToArray());
}

/// <summary>Value-free proof that no active runtime lane can race an alteration migration.</summary>
public sealed record WorkflowMigrationQuiescenceReport
{
    public WorkflowMigrationQuiescenceReport(bool isQuiescent, IReadOnlyCollection<WorkflowMigrationCompatibilityFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);
        var snapshot = findings.OrderBy(finding => finding.Code, StringComparer.Ordinal).ThenBy(finding => finding.SubjectId, StringComparer.Ordinal).ToArray();
        if (isQuiescent != (snapshot.Length == 0))
            throw new ArgumentException("Migration quiescence must agree with its findings.", nameof(isQuiescent));
        IsQuiescent = isQuiescent;
        Findings = snapshot;
    }

    public bool IsQuiescent { get; }
    public IReadOnlyCollection<WorkflowMigrationCompatibilityFinding> Findings { get; }

    public static WorkflowMigrationQuiescenceReport Quiescent() => new(true, []);
    public static WorkflowMigrationQuiescenceReport Busy(IEnumerable<WorkflowMigrationCompatibilityFinding> findings) => new(false, findings.ToArray());
}
