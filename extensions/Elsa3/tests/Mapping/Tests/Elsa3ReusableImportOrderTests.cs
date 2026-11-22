using Elsa3.Activities.Design.Import.Models;
using Elsa3.Mapping.Services;
using Xunit;

namespace Elsa3.Mapping.Tests;

/// <summary>
/// Spec 246 R-5: reusable versions are converted and published after the versions they use, and the versions of one
/// definition in ascending order, because the publisher compiles against published dependencies and only publishes
/// above the head.
/// </summary>
public sealed class Elsa3ReusableImportOrderTests
{
    [Fact]
    public void A_chain_publishes_from_the_leaf()
    {
        var order = Order(Item("a", 1), Item("b", 1, "a-v1"), Item("c", 1, "b-v1"));

        Assert.Equal(["a-v1", "b-v1", "c-v1"], order);
    }

    [Fact]
    public void A_diamond_publishes_the_shared_dependency_once_and_first()
    {
        var order = Order(Item("top", 1, "left-v1", "right-v1"), Item("left", 1, "base-v1"), Item("right", 1, "base-v1"), Item("base", 1));

        Assert.Equal(["base-v1", "left-v1", "right-v1", "top-v1"], order);
    }

    [Fact]
    public void Versions_of_one_definition_publish_ascending_before_a_caller_of_the_older_one()
    {
        var order = Order(Item("caller", 1, "inner-v1"), Item("inner", 2), Item("inner", 1));

        Assert.Equal(["inner-v1", "caller-v1", "inner-v2"], order);
    }

    [Fact]
    public void Independent_versions_keep_a_stable_order()
    {
        Assert.Equal(["a-v1", "b-v1", "c-v1"], Order(Item("c", 1), Item("a", 1), Item("b", 1)));
    }

    [Fact]
    public void Versions_that_wait_on_each_other_are_reported_unordered()
    {
        // a v1 uses b v2 and b v1 uses a v2; with each definition ascending, nothing can go first.
        var items = new[] { Item("a", 1, "b-v2"), Item("a", 2), Item("b", 1, "a-v2"), Item("b", 2) };

        var ordered = Elsa3ReusableImportOrder.Order(items, out var unordered);

        Assert.Empty(ordered);
        Assert.Equal(["a-v1", "a-v2", "b-v1", "b-v2"], unordered);
    }

    private static string[] Order(params ReusableActivityImportItem[] items)
    {
        var ordered = Elsa3ReusableImportOrder.Order(items, out var unordered);
        Assert.Empty(unordered);
        return ordered.Select(item => item.SourceVersionId).ToArray();
    }

    private static ReusableActivityImportItem Item(string definitionId, int version, params string[] uses) => new(
        definitionId,
        $"{definitionId}-v{version}",
        version,
        "sha256:test",
        true,
        definitionId,
        $"{definitionId}-v{version}",
        $"activity-{definitionId}",
        $"activity-version-{definitionId}-v{version}",
        $"elsa3.{definitionId}",
        uses.Select(target => new ReusableActivityDependency(
            $"{definitionId}-v{version}", target.Split('-')[0], target, $"activity-version-{target}", $"node-{target}")).ToArray(),
        [],
        [],
        []);
}
