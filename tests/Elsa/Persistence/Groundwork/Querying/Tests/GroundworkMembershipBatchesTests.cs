using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

public sealed class GroundworkMembershipBatchesTests
{
    [Fact]
    public void Deduplicates_sorts_ordinal_and_chunks_to_the_declared_cardinality()
    {
        var values = Enumerable.Range(0, 450).Select(index => $"id-{index:D3}").Reverse().ToList();
        values.AddRange(values.Take(25).ToArray());

        var batches = GroundworkMembershipBatches.Create(values);

        Assert.Equal(3, batches.Count);
        Assert.Equal([200, 200, 50], batches.Select(batch => batch.Length));
        Assert.Equal("id-000", batches[0][0]);
        Assert.Equal("id-449", batches[2][^1]);
        var flattened = batches.SelectMany(batch => batch).ToArray();
        Assert.Equal(flattened.OrderBy(value => value, StringComparer.Ordinal), flattened);
        Assert.Equal(450, flattened.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_same_logical_set_produces_identical_batches_regardless_of_caller_order()
    {
        var forward = Enumerable.Range(0, 333).Select(index => $"id-{index:D3}").ToArray();
        var shuffled = forward.OrderBy(value => value.GetHashCode()).ToArray();

        var first = GroundworkMembershipBatches.Create(forward);
        var second = GroundworkMembershipBatches.Create(shuffled);

        Assert.Equal(first.Count, second.Count);
        for (var index = 0; index < first.Count; index++)
            Assert.Equal(first[index], second[index]);
    }

    [Fact]
    public void Empty_input_produces_no_batches_and_invalid_sizes_are_rejected()
    {
        Assert.Empty(GroundworkMembershipBatches.Create([]));
        Assert.Throws<ArgumentOutOfRangeException>(() => GroundworkMembershipBatches.Create(["a"], 0));
        Assert.Throws<ArgumentNullException>(() => GroundworkMembershipBatches.Create(null!));
    }
}
