using Elsa.Activities.Http.Activities;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Unit coverage for <see cref="HttpEndpointStimulusOptions"/> as the single owner of the endpoint-options wire
/// format (#592 item 14): <see cref="HttpEndpointStimulusOptions.ToMetadata"/> (write) and
/// <see cref="HttpEndpointStimulusOptions.FromMetadata"/> (read) must round-trip, and defaults must contribute no
/// metadata so bindings stay lean.
/// </summary>
public sealed class HttpEndpointStimulusOptionsTests
{
    public static TheoryData<HttpEndpointStimulusOptions> RoundTripCases() => new()
    {
        HttpEndpointStimulusOptions.None,
        new HttpEndpointStimulusOptions(Authorize: true),
        new HttpEndpointStimulusOptions(Authorize: true, Policy: "admins"),
        new HttpEndpointStimulusOptions(RequestTimeout: TimeSpan.FromSeconds(30)),
        new HttpEndpointStimulusOptions(RequestSizeLimit: 1048576),
        new HttpEndpointStimulusOptions(
            Authorize: true,
            Policy: "admins",
            RequestTimeout: TimeSpan.FromMinutes(2) + TimeSpan.FromMilliseconds(500),
            RequestSizeLimit: 4096)
    };

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void FromMetadata_InvertsToMetadata(HttpEndpointStimulusOptions original)
    {
        var roundTripped = HttpEndpointStimulusOptions.FromMetadata(original.ToMetadata());

        Assert.Equal(original, roundTripped);
    }

    [Fact]
    public void Defaults_ContributeNoMetadata()
    {
        Assert.Empty(HttpEndpointStimulusOptions.None.ToMetadata());
    }

    [Fact]
    public void FromMetadata_Null_YieldsDefaults()
    {
        Assert.Equal(HttpEndpointStimulusOptions.None, HttpEndpointStimulusOptions.FromMetadata(null));
    }

    [Fact]
    public void FromMetadata_EmptyDictionary_YieldsDefaults()
    {
        Assert.Equal(
            HttpEndpointStimulusOptions.None,
            HttpEndpointStimulusOptions.FromMetadata(new Dictionary<string, string>()));
    }

    [Fact]
    public void FromMetadata_MalformedTimeoutAndSize_FallBackToNull()
    {
        var metadata = new Dictionary<string, string>
        {
            [Elsa.Http.Core.HttpEndpointRouting.RequestTimeoutMetadataKey] = "not-a-timespan",
            [Elsa.Http.Core.HttpEndpointRouting.RequestSizeLimitMetadataKey] = "-5"
        };

        var options = HttpEndpointStimulusOptions.FromMetadata(metadata);

        Assert.Null(options.RequestTimeout);
        Assert.Null(options.RequestSizeLimit);
    }

    [Fact]
    public void OnlyNonDefaultValuesEmitKeys()
    {
        var metadata = new HttpEndpointStimulusOptions(Authorize: true).ToMetadata();

        Assert.True(metadata.ContainsKey(Elsa.Http.Core.HttpEndpointRouting.AuthorizeMetadataKey));
        Assert.False(metadata.ContainsKey(Elsa.Http.Core.HttpEndpointRouting.PolicyMetadataKey));
        Assert.False(metadata.ContainsKey(Elsa.Http.Core.HttpEndpointRouting.RequestTimeoutMetadataKey));
        Assert.False(metadata.ContainsKey(Elsa.Http.Core.HttpEndpointRouting.RequestSizeLimitMetadataKey));
    }

    // ---- D-D5 fail-closed merge across divergent waiting bookmarks ----

    [Fact]
    public void MergeForResume_Empty_YieldsDefaults()
    {
        var merged = HttpEndpointStimulusOptions.MergeForResume([]);

        Assert.False(merged.Authorize);
        Assert.Empty(merged.Policies);
        Assert.Null(merged.RequestSizeLimit);
        Assert.Null(merged.RequestTimeout);
    }

    [Fact]
    public void MergeForResume_Authorize_TrueIfAnyBookmarkRequiresIt()
    {
        var merged = HttpEndpointStimulusOptions.MergeForResume(
        [
            new HttpEndpointStimulusOptions(Authorize: false).ToMetadata(),
            new HttpEndpointStimulusOptions(Authorize: true).ToMetadata()
        ]);

        Assert.True(merged.Authorize);
    }

    [Fact]
    public void MergeForResume_Policies_AreTheDistinctNonEmptyUnion()
    {
        var merged = HttpEndpointStimulusOptions.MergeForResume(
        [
            new HttpEndpointStimulusOptions(Authorize: true, Policy: "admins").ToMetadata(),
            new HttpEndpointStimulusOptions(Authorize: true, Policy: "auditors").ToMetadata(),
            new HttpEndpointStimulusOptions(Authorize: true, Policy: "admins").ToMetadata(), // duplicate
            new HttpEndpointStimulusOptions(Authorize: true).ToMetadata() // no policy — contributes nothing
        ]);

        Assert.Equal(new[] { "admins", "auditors" }, merged.Policies.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void MergeForResume_SizeLimit_IsMinOfSpecified_AbsentDoesNotRelax()
    {
        var merged = HttpEndpointStimulusOptions.MergeForResume(
        [
            new HttpEndpointStimulusOptions(RequestSizeLimit: 128).ToMetadata(),
            new HttpEndpointStimulusOptions().ToMetadata(), // absent limit
            new HttpEndpointStimulusOptions(RequestSizeLimit: 16).ToMetadata()
        ]);

        Assert.Equal(16, merged.RequestSizeLimit);
    }

    [Fact]
    public void MergeForResume_Timeout_IsMinOfSpecified()
    {
        var merged = HttpEndpointStimulusOptions.MergeForResume(
        [
            new HttpEndpointStimulusOptions(RequestTimeout: TimeSpan.FromSeconds(30)).ToMetadata(),
            new HttpEndpointStimulusOptions(RequestTimeout: TimeSpan.FromSeconds(5)).ToMetadata()
        ]);

        Assert.Equal(TimeSpan.FromSeconds(5), merged.RequestTimeout);
    }

    [Fact]
    public void MergeForResume_SingleBookmark_MatchesItsOptions()
    {
        var merged = HttpEndpointStimulusOptions.MergeForResume(
        [
            new HttpEndpointStimulusOptions(Authorize: true, Policy: "admins", RequestSizeLimit: 64, RequestTimeout: TimeSpan.FromSeconds(10)).ToMetadata()
        ]);

        Assert.True(merged.Authorize);
        Assert.Equal(new[] { "admins" }, merged.Policies);
        Assert.Equal(64, merged.RequestSizeLimit);
        Assert.Equal(TimeSpan.FromSeconds(10), merged.RequestTimeout);
    }
}
