using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit;

public sealed class ActivityPresentationRecordTests
{
    [Fact]
    public void Constructor_trims_boundaries_and_preserves_internal_description_newlines()
    {
        var record = new ActivityPresentationRecord(
            "node-1",
            "  Notify buyer  ",
            "  First line\nSecond line  ");

        Assert.Equal("Notify buyer", record.DisplayName);
        Assert.Equal("First line\nSecond line", record.Description);
    }

    [Fact]
    public void Collection_omits_empty_records_and_rejects_duplicate_node_ids()
    {
        var normalized = ActivityPresentationRecord.NormalizeCollection(
        [
            new("node-1", "Notify buyer"),
            new("node-2", " ", "\n")
        ]);

        Assert.Equal("node-1", Assert.Single(normalized).NodeId);
        Assert.Throws<ArgumentException>(() =>
            ActivityPresentationRecord.NormalizeCollection(
            [
                new("node-1", "One"),
                new("node-1", description: "Two")
            ]));
    }

    [Theory]
    [InlineData(201, 0)]
    [InlineData(0, 2001)]
    public void Constructor_enforces_display_name_and_description_limits(
        int displayNameLength,
        int descriptionLength)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ActivityPresentationRecord(
            "node-1",
            displayNameLength == 0 ? null : new string('n', displayNameLength),
            descriptionLength == 0 ? null : new string('d', descriptionLength)));
    }
}
