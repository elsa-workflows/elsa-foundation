using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Testing.Core.Serialization;

namespace Elsa.Workflows.Testing.Core.Models;

/// <summary>Authored scenario content. Capture serializes and retains an exact revision before execution.</summary>
public sealed record TestCaseContent
{
    public const string Profile = "elsa.workflow-test-case/1";

    public required string Name { get; init; }
    public string Purpose { get; init; } = "";
    public required TestTargetBinding Target { get; init; }
    public TestSetup Setup { get; init; } = new();
    public TestExecutionMode Mode { get; init; } = TestExecutionMode.Controlled;
    public TestTimePolicy Time { get; init; } = new();
    public IReadOnlyList<TestDependencyBinding> Dependencies { get; init; } = [];
    public IReadOnlyList<TestFixtureDefinition> Fixtures { get; init; } = [];
    public IReadOnlyList<TestScenarioStep> Steps { get; init; } = [];
    public IReadOnlyList<TestAssertion> Assertions { get; init; } = [];
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(1);
    public TimeSpan CleanupTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

public enum TestTargetKind { Workflow, ActivityOccurrence, ActivityDefinition }
public enum TestExecutionMode { Controlled, Integration }
public enum TestClockKind { Wall, Controlled }

/// <summary>Logical association and compatibility facts; a run separately pins exact candidate content.</summary>
public sealed record TestTargetBinding
{
    public required TestTargetKind Kind { get; init; }
    public required string DefinitionId { get; init; }
    public required string ModelProfile { get; init; }
    public string? NodeId { get; init; }
    public IReadOnlyList<string> OwningPath { get; init; } = [];
    public string? ContractFingerprint { get; init; }
    public string? ActivityDefinitionId { get; init; }
    public string? ActivityVersion { get; init; }
    public TestProfileData? Start { get; init; }
}

/// <summary>Explicit test data; missing members remain absent instead of becoming JSON null.</summary>
public sealed record TestSetup
{
    public IReadOnlyDictionary<string, JsonElement> WorkflowInputs { get; init; } = new Dictionary<string, JsonElement>();
    public IReadOnlyList<TestInputOverride> InputOverrides { get; init; } = [];
    public IReadOnlyList<TestVariableFixture> Variables { get; init; } = [];
    public IReadOnlyList<TestPriorResultFixture> PriorResults { get; init; } = [];
}

public sealed record TestInputOverride(string NodeId, string MemberKey, JsonElement Value);
public sealed record TestVariableFixture(string DeclaringScopeId, string VariableKey, JsonElement Value, string? IterationKey = null);

/// <summary>Outside-target context, never evidence that its producer executed.</summary>
public sealed record TestPriorResultFixture(
    string ProducerNodeId,
    IReadOnlyList<string> OwningPath,
    string ProjectionKey,
    string ContractFingerprint,
    JsonElement Value,
    string? CausalSlot = null);

/// <summary>Versioned module-owned authored data. A profile identifier conveys no execution authority.</summary>
public sealed record TestProfileData(string Profile, JsonElement Data);
public sealed record TestFixtureDefinition(string Key, TestProfileData Configuration);
public sealed record TestDependencyBinding(string Key, TestExecutionMode Mode, TestProfileData Configuration);

public sealed record TestTimePolicy
{
    public TestClockKind Kind { get; init; } = TestClockKind.Controlled;
    public DateTimeOffset InitialInstant { get; init; } = DateTimeOffset.UnixEpoch;
}

/// <summary>A semantic selector is rebound to one exact wait in the admitted attempt, never a previous run ID.</summary>
public sealed record TestWaitSelector(string NodeId, string Profile, string? Key = null, string? IterationKey = null);

public sealed record TestScenarioStep
{
    public required string Key { get; init; }
    public required TestProfileData Action { get; init; }
    public TestWaitSelector? Wait { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
    public IReadOnlyList<TestAssertion> Assertions { get; init; } = [];
}

public enum TestSubjectKind { Output, Outcome, Fault, Wait, Path, Interaction, Variable, Extension }
public enum TestAssertionOperator { Equals, Exists, Absent, Contains, CountEquals }

public sealed record TestSubject
{
    public required TestSubjectKind Kind { get; init; }
    public string? NodeId { get; init; }
    public string? MemberKey { get; init; }
    public string? DependencyKey { get; init; }
    public string? Code { get; init; }
    public TestWaitSelector? Wait { get; init; }
    public string? ObservationInterval { get; init; }
    public TestProfileData? Extension { get; init; }
}

public sealed record TestAssertion
{
    public required string Key { get; init; }
    public string Profile { get; init; } = "elsa.workflow-test-assertion/1";
    public required TestSubject Subject { get; init; }
    public required TestAssertionOperator Operator { get; init; }
    [JsonConverter(typeof(OptionalJsonElementConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Expected { get; init; }
}
