using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 1208 FR-002a, FR-016, FR-017, FR-018 and US3: the run-time check of a selection against the node's pinned
/// outcome set and multi-outcome flag. Every failure faults with the VF-ACT-006 code, never a silent route.
/// </summary>
public sealed class ActivityOutcomeSelectionRulesTests
{
    [Fact]
    public void A_declared_name_an_empty_selection_and_unspecified_with_Done_are_valid()
    {
        var contract = Contract(["Done", "Approved", "Rejected"]);

        ActivityOutcomeSelectionRules.Validate(contract, OutcomeSelection.Named("Approved"));
        ActivityOutcomeSelectionRules.Validate(contract, OutcomeSelection.Empty);
        ActivityOutcomeSelectionRules.Validate(contract, OutcomeSelection.Unspecified);
    }

    /// <summary>US3 scenario 2 and 3: an unknown computed name faults; no node setting turns it into another outcome.</summary>
    [Fact]
    public void A_name_outside_the_pinned_set_faults_with_VF_ACT_006()
    {
        var exception = Assert.Throws<ActivityOutcomeSelectionException>(() =>
            ActivityOutcomeSelectionRules.Validate(Contract(["Done", "Approved", "Review"]), OutcomeSelection.Named("Aproved")));

        Assert.Equal("VF-ACT-006", ((IRuntimeFaultClassification)exception).FailureCode);
        Assert.Contains("Aproved", exception.Message);
        Assert.Contains("'Approved'", exception.Message);
    }

    /// <summary>US3 scenario 4 / FR-017.</summary>
    [Fact]
    public void Several_names_on_a_node_that_allows_one_fault()
    {
        var exception = Assert.Throws<ActivityOutcomeSelectionException>(() =>
            ActivityOutcomeSelectionRules.Validate(Contract(["A", "B"]), OutcomeSelection.Named("A", "B")));

        Assert.Contains("allows one outcome", exception.Message);
    }

    [Fact]
    public void Several_names_on_a_node_that_allows_several_are_valid()
    {
        ActivityOutcomeSelectionRules.Validate(Contract(["Done", "A", "B", "C"], allowsMultiple: true), OutcomeSelection.Named("A", "B"));
    }

    /// <summary>FR-002: Done is never selected together with a branch.</summary>
    [Fact]
    public void Done_together_with_a_branch_faults()
    {
        Assert.Throws<ActivityOutcomeSelectionException>(() =>
            ActivityOutcomeSelectionRules.Validate(Contract(["Done", "A"], allowsMultiple: true), OutcomeSelection.Named("Done", "A")));
    }

    /// <summary>FR-002a: an unspecified selection on a node without Done points at the activity's code.</summary>
    [Fact]
    public void Unspecified_on_a_node_without_Done_faults_naming_the_activity()
    {
        var exception = Assert.Throws<ActivityOutcomeSelectionException>(() =>
            ActivityOutcomeSelectionRules.Validate(Contract(["True", "False"]), OutcomeSelection.Unspecified));

        Assert.Contains("completed without selecting an outcome", exception.Message);
        Assert.Contains("Test.Activity", exception.Message);
    }

    /// <summary>The projector faults with the code, carried on the durable fault record through the classification.</summary>
    [Fact]
    public void The_completion_projector_checks_through_the_same_rules()
    {
        var exception = Assert.Throws<ActivityOutcomeSelectionException>(() => new ActivityCompletionProjector().Project(
            "invocation-1",
            new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow),
            Contract(["Done", "A"]),
            ActivityTransition.Complete(ActivityUnit.Value, "B"),
            DateTimeOffset.UtcNow));

        Assert.Equal(ActivityOutcomeSelectionRules.FaultCode, exception.FailureCode);
    }

    [Fact]
    public void The_projector_records_each_selection_state_on_the_completion()
    {
        var projector = new ActivityCompletionProjector();
        var contract = Contract(["Done", "A", "B"], allowsMultiple: true);
        foreach (var selection in new[] { OutcomeSelection.Unspecified, OutcomeSelection.Empty, OutcomeSelection.Named("A", "B") })
        {
            var projected = projector.Project(
                "invocation-1",
                new ActivityAttempt("attempt-1", "invocation-1", 1, ActivityAttemptReason.Initial, DateTimeOffset.UtcNow),
                contract,
                ActivityTransition.Complete(ActivityUnit.Value, selection),
                DateTimeOffset.UtcNow);

            Assert.Equal(selection, projected.Completion.Selection);
        }
    }

    [Fact]
    public void The_flag_is_part_of_the_contract_fingerprint_only_when_set()
    {
        Assert.Equal(Contract(["A"]).SchemaFingerprint, Contract(["A"]).SchemaFingerprint);
        Assert.NotEqual(Contract(["A"]).SchemaFingerprint, Contract(["A"], allowsMultiple: true).SchemaFingerprint);
        Assert.DoesNotContain("allowsMultipleOutcomes", JsonSerializer.Serialize(Contract(["A"])), StringComparison.OrdinalIgnoreCase);
    }

    private static ActivityContract Contract(IReadOnlyCollection<string> outcomes, bool allowsMultiple = false) =>
        new(
            "Test.Activity",
            "1.0.0",
            "clr",
            JsonSerializer.SerializeToElement(new { typeAlias = "Test.Activity" }),
            [],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Activities.Runtime.Core.Models.ActivityUnit"), true, ActivityValuePolicy.Default, []),
            outcomes,
            new ActivityActivationRequirement("clr", "Test.Activity"),
            SideEffectProfile.External,
            supportsBackgroundExecution: false,
            allowsMultipleOutcomes: allowsMultiple);
}
