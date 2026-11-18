namespace Kernel.Review6;

/// <summary>A deterministic text report of one case under one rule set, with its assertions.</summary>
sealed class Report(string caseId, string rules, string invariant)
{
    private readonly List<string> _lines = [];
    private readonly List<(bool Ok, string Text)> _checks = [];

    public string Case => caseId;
    public string Rules => rules;
    public bool Passed => _checks.All(c => c.Ok);

    public void Line(string text) => _lines.Add(text);

    public void Check(bool ok, string text) => _checks.Add((ok, text));

    public void Expect<T>(string what, T actual, T expected) =>
        Check(EqualityComparer<T>.Default.Equals(actual, expected), $"{what}: {actual} (expected {expected})");

    public void ExpectSequence(string what, IReadOnlyList<string> actual, IReadOnlyList<string> expected) =>
        Check(actual.SequenceEqual(expected, StringComparer.Ordinal),
            $"{what}: [{string.Join(" ; ", actual)}] (expected [{string.Join(" ; ", expected)}])");

    public string Render()
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine($"== {caseId} | rules={rules} ==");
        text.AppendLine($"invariant: {invariant}");
        foreach (var line in _lines) text.AppendLine("  " + line);
        text.AppendLine("  assertions:");
        foreach (var (ok, check) in _checks) text.AppendLine($"    {(ok ? "PASS" : "FAIL")} {check}");
        text.AppendLine($"  real prototype calls: {Real.Summary()}");
        text.AppendLine($"RESULT {caseId} rules={rules}: {(Passed ? "PASS" : "FAIL")}");
        return text.ToString();
    }
}
