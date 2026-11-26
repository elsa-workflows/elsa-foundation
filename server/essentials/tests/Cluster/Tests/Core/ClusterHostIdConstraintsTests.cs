using Elsa.Cluster.Core.Models;

namespace Elsa.Cluster.Tests.Core;

public sealed class ClusterHostIdConstraintsTests
{
    // Not theory data: a lone surrogate does not survive the runner's serialization of test cases.
    private static readonly string?[] InvalidHostIds =
    [
        null,
        "",
        "   ",
        new string('h', ClusterHostIdConstraints.MaximumLength + 1),
        "host-\uD800",
        "\uDC00-host",
        "host-\uD800-x"
    ];

    [Fact]
    public void An_invalid_host_id_is_refused() =>
        Assert.All(InvalidHostIds, hostId =>
        {
            Assert.NotNull(ClusterHostIdConstraints.Describe(hostId));
            Assert.Throws<ArgumentException>(() => ClusterHostIdConstraints.Validate(hostId, "hostId"));
        });

    [Theory]
    [InlineData("web-1")]
    [InlineData("h😀st")]
    public void A_well_formed_host_id_is_accepted(string hostId) =>
        Assert.Equal(hostId, ClusterHostIdConstraints.Validate(hostId, "hostId"));

    [Fact]
    public void A_host_id_may_use_every_code_unit_up_to_the_limit()
    {
        var longest = new string('h', ClusterHostIdConstraints.MaximumLength);

        Assert.Null(ClusterHostIdConstraints.Describe(longest));
    }

    [Fact]
    public void Host_ids_compare_ordinally() =>
        Assert.NotEqual(
            new ClusterMemberIdentity("Web-1", new MemberIncarnation("a")),
            new ClusterMemberIdentity("web-1", new MemberIncarnation("a")));
}
