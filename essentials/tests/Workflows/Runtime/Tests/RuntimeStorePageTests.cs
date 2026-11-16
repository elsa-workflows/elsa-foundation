using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeStorePageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(RuntimeStorePageRequest.MaximumLimit + 1)]
    public void Request_rejects_an_out_of_range_limit(int limit) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeStorePageRequest(limit));

    [Fact]
    public void Request_rejects_blank_or_oversized_continuations()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeStorePageRequest(continuationToken: " "));
        Assert.Throws<ArgumentException>(() => new RuntimeStorePageRequest(
            continuationToken: new string('a', RuntimeStorePageRequest.MaximumContinuationTokenLength + 1)));
    }

    [Fact]
    public void Page_preserves_one_finite_advancing_live_view()
    {
        var request = new RuntimeStorePageRequest(limit: 2, continuationToken: "before");

        var page = new RuntimeStorePage<string>(request, ["a", "b"], nextContinuationToken: "after");

        Assert.Equal(["a", "b"], page.Items);
        Assert.Equal("after", page.NextContinuationToken);
    }

    [Fact]
    public void Page_rejects_oversized_or_nonadvancing_results()
    {
        var request = new RuntimeStorePageRequest(limit: 1, continuationToken: "same");

        Assert.Throws<ArgumentException>(() =>
            new RuntimeStorePage<string>(request, ["a", "b"]));
        Assert.Throws<ArgumentException>(() =>
            new RuntimeStorePage<string>(request, ["a"], nextContinuationToken: "same"));
        Assert.Throws<ArgumentException>(() =>
            new RuntimeStorePage<string>(request, [], nextContinuationToken: "after"));
    }

    [Fact]
    public void Source_reference_batches_are_bounded_and_live_pages_require_an_explicit_time()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorkflowExecutableArtifactCandidateBatch(
                Enumerable.Range(0, RuntimeStorePageRequest.MaximumLimit + 1).Select(index => $"artifact-{index}")));
        Assert.Throws<ArgumentNullException>(() =>
            new WorkflowExecutableSourceReferencePageQuery(liveOnly: true));
    }
}
