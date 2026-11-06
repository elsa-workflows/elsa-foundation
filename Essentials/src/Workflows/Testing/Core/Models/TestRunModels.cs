using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Testing.Core.Serialization;

namespace Elsa.Workflows.Testing.Core.Models;

public enum TestRunLifecycle { Admitted, Preparing, Executing, Closing, Finalized }
public enum TestVerdict { NotEvaluated, Passed, Failed, Skipped, Cancelled, Unavailable, Incomplete }
public enum TestCheckVerdict { NotEvaluated, Passed, Failed, Unavailable }
public enum TestEvidenceAvailability { Available, Missing, Withheld, Unavailable }
public enum TestClosureState { Open, AdmissionClosed, CancellationRequested, Reconciling, Closed }

public sealed record TestDependencyIdentity(string Key, string Profile, string Revision, string Fingerprint);

/// <summary>Safe identity only; executable/candidate material is retained through its normal owning store.</summary>
public sealed record TestCandidateManifest
{
    public required string DefinitionId { get; init; }
    public required string SourceId { get; init; }
    public required string SourceFingerprint { get; init; }
    public required string ArtifactId { get; init; }
    public required string ArtifactFingerprint { get; init; }
    public required string ModelProfile { get; init; }
    public IReadOnlyList<TestDependencyIdentity> Dependencies { get; init; } = [];
}

public sealed record TestDiagnostic(string Code, string Message, string? Target = null, string? Member = null, string? Step = null);

/// <summary>Missing/withheld evidence is explicit and cannot compare equal to JSON null.</summary>
public sealed record TestObservationValue(TestEvidenceAvailability Availability,
    [property: JsonConverter(typeof(OptionalJsonElementConverter))]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Value,
    string? Reason = null);

/// <summary>Known absence differs from missing evidence. Interaction evidence is a complete invocation array; existence is derived from that array after settlement.</summary>
public sealed record TestSubjectObservation(TestObservationValue Evidence, bool Exists, bool Settled, string? Reference = null);

public sealed record TestCheckResult(
    string Key,
    string Profile,
    TestAssertionOperator Operator,
    string DefinitionDigest,
    TestCheckVerdict Verdict,
    TestSubject Subject,
    [property: JsonConverter(typeof(OptionalJsonElementConverter))]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] JsonElement? Expected,
    TestObservationValue Actual,
    string? Message = null);

/// <summary>Original model status remains separate from the assertion verdict.</summary>
public sealed record TestExecutionObservation(string ModelProfile, string Status, string? ExecutionId = null);

public sealed record TestClosureResult(TestClosureState State, bool CleanupSettled, IReadOnlyList<TestDiagnostic> Remaining);

public enum TestStepState { NotStarted, Executing, Settled, Skipped, Cancelled, Unavailable, Incomplete }
public sealed record TestStepResult(string Key, string Profile, string ActionDigest, TestStepState State, long ObservationSequence, IReadOnlyList<TestCheckResult> Checks);
public sealed record TestInteractionObservation(string Key, string DependencyKey, string Disposition, string RequestFingerprint, DateTimeOffset ObservedAt);

/// <summary>Exact authored assertion identity captured before execution, not inferred from observed results.</summary>
public sealed record TestCheckPlan(string Key, string Profile, string DefinitionDigest);

/// <summary>Ordered authored action and expected checks from the captured case revision.</summary>
public sealed record TestStepPlan(string Key, string ActionProfile, string ActionDigest, IReadOnlyList<TestCheckPlan> Checks);

public sealed record TestCasePlan(IReadOnlyList<TestCheckPlan> Checks, IReadOnlyList<TestStepPlan> Steps);

/// <summary>Admission freezes the expected plan from exact captured case content before any result exists.</summary>
public sealed record TestRunSelection(string Key, TestRevisionReference Case, TestCasePlan Plan, bool Required = true, string? SkipReason = null);

public sealed record TestCaseResult
{
    public required string AttemptId { get; init; }
    public required string SelectionKey { get; init; }
    public required TestRevisionReference Case { get; init; }
    public required TestCandidateManifest Candidate { get; init; }
    public required TestTargetBinding Target { get; init; }
    public required TestExecutionMode Mode { get; init; }
    public required TestTimePolicy Time { get; init; }
    public required TestVerdict Verdict { get; init; }
    public required TestExecutionObservation Execution { get; init; }
    public required TestClosureResult Closure { get; init; }
    public bool Required { get; init; } = true;
    public string? SkipReason { get; init; }
    public IReadOnlyList<TestCheckResult> Checks { get; init; } = [];
    public IReadOnlyList<TestStepResult> Steps { get; init; } = [];
    public IReadOnlyList<TestInteractionObservation> Interactions { get; init; } = [];
    public IReadOnlyList<string> Profiles { get; init; } = [];
    public IReadOnlyList<TestDiagnostic> Diagnostics { get; init; } = [];
}

public sealed record TestRunReport
{
    public const string Profile = "elsa.workflow-test-report/1";
    public required string RunId { get; init; }
    public required string RequestFingerprint { get; init; }
    public required string MaterialFingerprint { get; init; }
    public required TestRunLifecycle Lifecycle { get; init; }
    public required long ObservationSequence { get; init; }
    public required DateTimeOffset ObservedAt { get; init; }
    public ResolvedTestSuiteManifest? Suite { get; init; }
    public required IReadOnlyList<TestRunSelection> Selections { get; init; }
    public IReadOnlyList<TestCaseResult> Cases { get; init; } = [];
    public bool OptionalSkipsAllowed { get; init; }
    public bool UserCancellationRequested { get; init; }
}
