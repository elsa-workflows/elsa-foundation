namespace Kernel.Review5;

/// <summary>
/// A deterministic text report of one case under one rule set, with its assertions (copied from Review4.Blockers).
/// A run can also stop: it then reports the trace it ran and the reason it cannot state an expected outcome.
/// </summary>
sealed class Report(string caseId, string rules, string invariant)
{
    private readonly List<string> _lines = [];
    private readonly List<(bool Ok, string Text)> _checks = [];
    private string? _stopped;

    public string Case => caseId;
    public string Rules => rules;
    public bool Passed => _stopped is null && _checks.All(c => c.Ok);
    /// <summary>PASS, FAIL or STOPPED.</summary>
    public string Outcome => _stopped is not null ? "STOPPED" : Passed ? "PASS" : "FAIL";

    public void Line(string text) => _lines.Add(text);

    public void Check(bool ok, string text) => _checks.Add((ok, text));

    public void Expect<T>(string what, T actual, T expected) =>
        Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{what}: {actual} (expected {expected})");

    public void ExpectSequence(string what, IReadOnlyList<string> actual, IReadOnlyList<string> expected) =>
        Check(actual.SequenceEqual(expected, StringComparer.Ordinal),
            $"{what}: [{string.Join(" ; ", actual)}] (expected [{string.Join(" ; ", expected)}])");

    /// <summary>The rule this run needs is not stated: the run reports its trace and stops.</summary>
    public void Stop(string reason) => _stopped = reason;

    public string Render()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"== {caseId} | rules={rules} ==");
        text.AppendLine($"invariant: {invariant}");
        foreach (var line in _lines) text.AppendLine("  " + line);
        text.AppendLine("  assertions:");
        foreach (var (ok, check) in _checks) text.AppendLine($"    {(ok ? "PASS" : "FAIL")} {check}");
        if (_stopped is not null) text.AppendLine($"  STOPPED: {_stopped}");
        text.AppendLine($"  real prototype calls: {Real.Summary()}");
        text.AppendLine($"RESULT {caseId} rules={rules}: {Outcome}");
        return text.ToString();
    }
}
