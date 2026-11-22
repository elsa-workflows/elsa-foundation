using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>Spec 1208 FR-001 to FR-006: the three states of an outcome selection and their guards.</summary>
public sealed class OutcomeSelectionTests
{
    [Fact]
    public void Unspecified_resolves_to_Done()
    {
        var selection = OutcomeSelection.Unspecified;

        Assert.Equal(OutcomeSelectionState.Unspecified, selection.State);
        Assert.Empty(selection.Names);
        Assert.Equal(new[] { "Done" }, selection.ResolveNames());
    }

    [Fact]
    public void Empty_resolves_to_no_outcome_and_differs_from_unspecified()
    {
        var selection = OutcomeSelection.Empty;

        Assert.Equal(OutcomeSelectionState.Empty, selection.State);
        Assert.Empty(selection.ResolveNames());
        Assert.NotEqual(OutcomeSelection.Unspecified, selection);
    }

    [Fact]
    public void Named_keeps_the_selection_order_and_counts_a_repeat_once()
    {
        var selection = OutcomeSelection.Named("Approved", "Audit", "Approved");

        Assert.Equal(OutcomeSelectionState.Named, selection.State);
        Assert.Equal(new[] { "Approved", "Audit" }, selection.ResolveNames());
    }

    [Fact]
    public void Names_are_compared_ordinally()
    {
        Assert.Equal(new[] { "approved", "Approved" }, OutcomeSelection.Named("approved", "Approved").Names);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_name_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => OutcomeSelection.Named(name));
        Assert.Throws<ArgumentException>(() => OutcomeSelection.Named("Approved", name));
    }

    [Fact]
    public void A_named_selection_needs_at_least_one_name()
    {
        Assert.Throws<ArgumentException>(() => OutcomeSelection.Named(Array.Empty<string>()));
    }

    [Fact]
    public void Equal_selections_are_equal_and_hash_alike()
    {
        Assert.Equal(OutcomeSelection.Named("A", "B"), OutcomeSelection.Named(new List<string> { "A", "B" }));
        Assert.Equal(OutcomeSelection.Named("A", "B").GetHashCode(), OutcomeSelection.Named("A", "B").GetHashCode());
        Assert.NotEqual(OutcomeSelection.Named("A", "B"), OutcomeSelection.Named("B", "A"));
    }

    [Theory]
    [MemberData(nameof(AllStates))]
    public void Every_state_round_trips_through_json(OutcomeSelection selection)
    {
        var json = JsonSerializer.Serialize(selection, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Equal(selection, JsonSerializer.Deserialize<OutcomeSelection>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Json_with_names_on_a_non_named_state_is_refused()
    {
        Assert.ThrowsAny<Exception>(() => JsonSerializer.Deserialize<OutcomeSelection>(
            """{"state":"Empty","names":["A"]}""", new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    [Fact]
    public void Transition_overloads_carry_each_state()
    {
        Assert.Equal(OutcomeSelection.Unspecified, Selection(ActivityTransition.Complete(ActivityUnit.Value)));
        Assert.Equal(OutcomeSelection.Named("A"), Selection(ActivityTransition.Complete(ActivityUnit.Value, "A")));
        Assert.Equal(OutcomeSelection.Named("A", "B"), Selection(ActivityTransition.Complete(ActivityUnit.Value, new[] { "A", "B" })));
        Assert.Equal(OutcomeSelection.Empty, Selection(ActivityTransition.Complete(ActivityUnit.Value, OutcomeSelection.Empty)));
        Assert.Equal(OutcomeSelection.Empty, Selection(ActivityTransition<ActivityUnit, ActivityUnit>.Complete(ActivityUnit.Value, OutcomeSelection.Empty)));
    }

    public static TheoryData<OutcomeSelection> AllStates() =>
        [OutcomeSelection.Unspecified, OutcomeSelection.Empty, OutcomeSelection.Named("A"), OutcomeSelection.Named("A", "B")];

    private static OutcomeSelection Selection(ActivityTransition transition) =>
        Assert.IsAssignableFrom<IActivityCompletionTransition>(transition).Selection;
}
