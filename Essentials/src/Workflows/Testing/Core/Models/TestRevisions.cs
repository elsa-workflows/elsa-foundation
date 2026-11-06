namespace Elsa.Workflows.Testing.Core.Models;

/// <summary>Exact immutable resource revision inside its authorized library.</summary>
public sealed record TestRevisionReference(string ResourceId, string RevisionId, string Fingerprint);

public sealed record TestCaseWorkingCopy(string Id, long Revision, TestCaseContent Content);
public sealed record TestCaseRevision(TestRevisionReference Reference, TestCaseContent Content);

public sealed record TestSuiteContent
{
    public const string Profile = "elsa.workflow-test-suite/1";
    public required string Name { get; init; }
    public string Purpose { get; init; } = "";
    public IReadOnlyList<TestSuiteMember> Members { get; init; } = [];
}

/// <summary>Logical membership resolves latest saved case content once at admission/export.</summary>
public sealed record TestSuiteMember(string Key, string CaseId, bool Required = true, string? SkipReason = null);
public sealed record TestSuiteWorkingCopy(string Id, long Revision, TestSuiteContent Content);
public sealed record TestSuiteRevision(TestRevisionReference Reference, TestSuiteContent Content);
public sealed record ResolvedTestSuiteMember(string Key, TestRevisionReference Case, bool Required, string? SkipReason);

/// <summary>Captured exact membership. Retries and portable execution do not refresh it from latest.</summary>
public sealed record ResolvedTestSuiteManifest(
    TestRevisionReference Suite,
    IReadOnlyList<ResolvedTestSuiteMember> Members,
    string Fingerprint);

public sealed record TestLibraryPage<T>(int Total, IReadOnlyList<T> Items);
