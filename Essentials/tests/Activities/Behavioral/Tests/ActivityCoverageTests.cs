using Elsa.Activities.Behavioral.Infrastructure;
using Xunit;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// The point of the behavioural drive (#1119): a contract diff can prove an outcome is <em>declared</em>; only a
/// run can prove it is <em>reachable</em>. These assertions compare the declared contract surface of every shipped
/// activity against what the engine actually committed when the drives ran, so a declared-but-dead outcome — the
/// failure mode a contract diff structurally cannot see — fails here.
/// </summary>
public sealed class ActivityOutcomeReachabilityTests(ActivityDriveFixture fixture) : ActivityDriveTestBase(fixture)
{
    public static TheoryData<string, string> DeclaredOutcomes =>
        ActivityMemberPairs(ActivityContractInventory.DeclaredOutcomes, UndrivenCoverage.ReasonFor);

    [Theory]
    [MemberData(nameof(DeclaredOutcomes))]
    public void Every_declared_outcome_is_reachable(string activityTypeName, string outcome)
    {
        var activityType = Activity(activityTypeName);

        Assert.True(
            Recorder.ObservedOutcome(activityType, outcome),
            $"'{activityTypeName}' declares outcome '{outcome}' but no drive reached it. " +
            $"Outcomes actually observed: {Describe(Recorder.ObservedOutcomes(activityType))}. " +
            "Either the outcome is dead (a real defect — an author can wire that port and nothing will ever take it), " +
            "or the drive for this activity does not exercise it yet.");
    }

    /// <summary>
    /// The mirror assertion: an outcome the engine emits that the contract never declared is just as broken — the
    /// designer would offer no port to wire it to, so the branch silently goes nowhere.
    /// </summary>
    [Theory]
    [MemberData(nameof(DrivenActivities))]
    public void Every_observed_outcome_is_declared(string activityTypeName)
    {
        var activityType = Activity(activityTypeName);
        var declared = ActivityContractInventory.DeclaredOutcomes(activityType);
        var undeclared = Recorder.ObservedOutcomes(activityType)
            .Where(outcome => !declared.Contains(outcome, StringComparer.Ordinal))
            .Where(outcome => !DynamicOutcomeActivities.Contains(activityTypeName, StringComparer.Ordinal))
            .ToArray();

        Assert.True(
            undeclared.Length == 0,
            $"'{activityTypeName}' emitted outcome(s) {Describe(undeclared)} that its contract does not declare. " +
            $"Declared: {Describe(declared)}.");
    }

    public static TheoryData<string> DrivenActivities => ActivityNames(UndrivenCoverage.ReasonForActivity);

    // Activities whose ports are per-node data pinned at publish time from an authored input
    // ([ActivityValueOutcomes]); their emitted outcome names are authored values, not a fixed declared set, so the
    // "observed ⊆ declared" direction does not apply. Their reachability is covered by their own drives instead.
    private static readonly string[] DynamicOutcomeActivities =
    [
        "Elsa.Activities.Http.Activities.SendHttpRequest",
        "Elsa.Activities.Scripting.Activities.RunJavaScript",
        // Switch case ports and BpmnDecision outcomes are likewise authored data, not a declared enum.
        "Elsa.Activities.Switch.Activities.Switch",
        "Elsa.Activities.Bpmn.Activities.BpmnDecision",
        // A graph boundary's ports are its authored outcome mappings, pinned per node at publish time. Its
        // *declared* side is the implicit Done an unmapped boundary completes with, which the drive still reaches.
        "Elsa.Activities.Graph.Runtime.Activities.GraphActivity"
    ];

    private static string Describe(IReadOnlyCollection<string> outcomes) =>
        outcomes.Count == 0 ? "(none)" : "[" + string.Join(", ", outcomes) + "]";
}

/// <summary>
/// Every declared output must actually be populated by some drive. A declared-but-never-written output is the
/// output-side twin of a dead outcome: an author binds it and always reads null.
/// </summary>
public sealed class ActivityOutputPopulationTests(ActivityDriveFixture fixture) : ActivityDriveTestBase(fixture)
{
    public static TheoryData<string, string> DeclaredOutputs =>
        ActivityMemberPairs(ActivityContractInventory.DeclaredOutputs, UndrivenCoverage.ReasonFor);

    [Theory]
    [MemberData(nameof(DeclaredOutputs))]
    public void Every_declared_output_is_populated(string activityTypeName, string outputKey)
    {
        var activityType = Activity(activityTypeName);

        Assert.True(
            Recorder.ObservedOutput(activityType, outputKey),
            $"'{activityTypeName}' declares output '{outputKey}' but no drive produced a non-null value for it. " +
            "Either nothing ever writes it, or the drive does not exercise the path that does.");
    }
}

/// <summary>
/// The fourth assertion of the brief: a required input that is absent must fault cleanly, rather than the activity
/// completing as if nothing were wrong. A required input nothing enforces is a contract the runtime does not keep.
/// </summary>
public sealed class RequiredInputEnforcementTests(ActivityDriveFixture fixture) : ActivityDriveTestBase(fixture)
{
    public static TheoryData<string, string> RequiredInputs =>
        ActivityMemberPairs(ActivityContractInventory.RequiredInputs, UndrivenCoverage.ReasonFor);

    [Theory]
    [MemberData(nameof(RequiredInputs))]
    public void Every_required_input_faults_cleanly_when_absent(string activityTypeName, string inputKey)
    {
        var activityType = Activity(activityTypeName);

        Assert.True(
            Recorder.ObservedRequiredInputFault(activityType, inputKey),
            $"'{activityTypeName}' declares input '{inputKey}' as required, but running the activity without it did " +
            "not fault: the run completed or was abandoned instead. A required input nothing enforces is a contract " +
            "the runtime does not keep — either enforce it, or drop the requiredness from the contract.");
    }
}

/// <summary>Coverage of the drives themselves: every shipped activity has one, and none of them threw.</summary>
public sealed class ActivityDriveHealthTests(ActivityDriveFixture fixture) : ActivityDriveTestBase(fixture)
{
    public static TheoryData<string> ShippedActivities => ActivityNames(UndrivenCoverage.ReasonForActivity);

    [Theory]
    [MemberData(nameof(ShippedActivities))]
    public void Every_shipped_activity_is_driven_through_a_real_workflow(string activityTypeName)
    {
        var activityType = Activity(activityTypeName);

        Assert.True(
            Recorder.WasDriven(activityType),
            $"'{activityTypeName}' ships in the activity library but no IActivityDrive exercises it. " +
            "Add one under Drives/ so its contract is measured against a real run, not just against its attributes.");
    }

    [Theory]
    [MemberData(nameof(ShippedActivities))]
    public void No_drive_faulted(string activityTypeName)
    {
        var failure = Fixture.FailureFor(Activity(activityTypeName));

        Assert.True(failure is null, $"The drive for '{activityTypeName}' threw: {failure}");
    }

    /// <summary>
    /// Catches a drive that threw but whose key is not a shipped activity — <c>No_drive_faulted</c> enumerates
    /// shipped activities only, so a cross-activity drive could otherwise fail silently and leave its coverage
    /// looking merely "not yet reached".
    /// </summary>
    [Fact]
    public void No_drive_failed_unattributed()
    {
        var unattributed = Fixture.Failures
            .Where(entry => ActivityContractInventory.Activities.All(activity => activity != entry.Key))
            .Select(entry => $"{entry.Key.Name}: {entry.Value}")
            .ToArray();

        Assert.True(unattributed.Length == 0, string.Join(Environment.NewLine, unattributed));
    }

    /// <summary>Guards the reflection sweep itself: an empty inventory would make every assertion above vacuous.</summary>
    [Fact]
    public void The_shipped_activity_inventory_is_not_empty() =>
        Assert.NotEmpty(ActivityContractInventory.Activities);

    /// <summary>
    /// The ratchet. Every shipped activity is now driven, so the undriven list is empty and must stay that way:
    /// an entry re-appearing means an activity dropped out of measured coverage, which has to be argued for in
    /// review rather than slipped in to quiet a failing assertion.
    /// </summary>
    [Fact]
    public void Undriven_coverage_is_empty() =>
        Assert.True(
            UndrivenCoverage.Describe().Count == 0,
            "Coverage regressed — these contract members are no longer measured against a real run: " +
            string.Join(Environment.NewLine, UndrivenCoverage.Describe()));
}
