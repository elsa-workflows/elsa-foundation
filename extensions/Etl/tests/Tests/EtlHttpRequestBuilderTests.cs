using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Bindings;
using Elsa.Etl.Runtime.Http;

namespace Elsa.Etl.Tests;

public sealed class EtlHttpRequestBuilderTests : IDisposable
{
    private static readonly Uri InitialSeed = new("https://source.example/api/v1/items?tenant=acme");
    private static readonly string Handle = new('a', 64);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "elsa-etl-http-request-builder-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Token_request_replaces_only_its_named_query_parameter_and_escapes_the_opaque_value()
    {
        var seed = new Uri("https://source.example/api/v1/items?tenant=a%2Fb&cursor=old&x=1");
        var builder = Builder(new EtlTokenPagination("/next", "cursor"), seed);

        var uri = builder.RequestUri(new EtlHttpRequest("shard-a", "opaque /+雪"));

        Assert.Equal(new Uri("https://source.example/api/v1/items?tenant=a%2Fb&cursor=opaque%20%2F%2B%E9%9B%AA&x=1"), uri);
    }

    [Fact]
    public void Token_request_refuses_ambiguous_duplicate_encoded_parameter_names()
    {
        var seed = new Uri("https://source.example/api/v1/items?cursor=one&%63ursor=two");
        var builder = Builder(new EtlTokenPagination("/next", "cursor"), seed);

        var error = Assert.Throws<EtlHttpRequestBuilderException>(() =>
            builder.RequestUri(new EtlHttpRequest("shard-a", "next")));

        Assert.Equal("etl.http.query-parameter-ambiguous", error.Code);
        Assert.DoesNotContain("cursor", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Token_request_decodes_raw_plus_as_space_but_preserves_encoded_literal_plus()
    {
        var seed = new Uri("https://source.example/api/v1/items?page+token=old&page%2Btoken=literal-plus&tenant=acme");
        var builder = Builder(new EtlTokenPagination("/next", "page token"), seed);

        var uri = builder.RequestUri(new EtlHttpRequest("shard-a", "next /+"));

        Assert.Equal(new Uri("https://source.example/api/v1/items?page+token=next%20%2F%2B&page%2Btoken=literal-plus&tenant=acme"), uri);
    }

    [Fact]
    public void Token_request_refuses_duplicate_form_decoded_plus_and_percent_encoded_names()
    {
        var seed = new Uri("https://source.example/api/v1/items?page+token=one&page%20token=two");
        var builder = Builder(new EtlTokenPagination("/next", "page token"), seed);

        var error = Assert.Throws<EtlHttpRequestBuilderException>(() =>
            builder.RequestUri(new EtlHttpRequest("shard-a", "next")));

        Assert.Equal("etl.http.query-parameter-ambiguous", error.Code);
    }

    [Fact]
    public void Token_request_appends_its_parameter_when_the_seed_does_not_have_it()
    {
        var seed = new Uri("https://source.example/api/v1/items?tenant=acme");
        var builder = Builder(new EtlTokenPagination("/next", "cursor"), seed);

        var uri = builder.RequestUri(new EtlHttpRequest("shard-a", "next-1"));

        Assert.Equal(new Uri("https://source.example/api/v1/items?tenant=acme&cursor=next-1"), uri);
    }

    [Fact]
    public void Relative_next_links_resolve_against_each_current_page_and_store_canonical_absolute_uris()
    {
        var builder = Builder(new EtlNextLinkPagination("/next"));
        var firstRequest = new EtlHttpRequest("shard-a", null);

        var firstContinuation = builder.NextContinuation(firstRequest, "nested/page2?page=2");
        var secondRequest = new EtlHttpRequest("shard-a", firstContinuation);
        var secondUri = builder.RequestUri(secondRequest);
        var secondContinuation = builder.NextContinuation(secondRequest, "page3?page=3");
        var thirdRequest = new EtlHttpRequest("shard-a", secondContinuation);
        var thirdContinuation = builder.NextContinuation(thirdRequest, "?page=4");

        Assert.Equal("https://source.example/api/v1/nested/page2?page=2", firstContinuation);
        Assert.Equal(new Uri(firstContinuation!), secondUri);
        Assert.Equal("https://source.example/api/v1/nested/page3?page=3", secondContinuation);
        Assert.Equal(new Uri(secondContinuation!), builder.RequestUri(thirdRequest));
        Assert.Equal("https://source.example/api/v1/nested/page3?page=4", thirdContinuation);
    }

    [Theory]
    [InlineData("https://other.example/api/v1/next")]
    [InlineData("../outside")]
    [InlineData("https://source.example/api/v1/items/child")]
    public void Next_links_cannot_escape_the_pinned_origin_and_exact_path_policy(string nextLink)
    {
        var builder = Builder(new EtlNextLinkPagination("/next"));

        var error = Assert.Throws<EtlHttpRequestBuilderException>(() =>
            builder.NextContinuation(new EtlHttpRequest("shard-a", null), nextLink));

        Assert.Equal("etl.http-endpoint-unavailable", error.Code);
        Assert.DoesNotContain("source.example", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Next_link_byte_limit_applies_after_absolute_uri_normalization()
    {
        var policy = EtlV2TestPipelines.Create().Http with { MaxContinuationBytes = 8 };
        var builder = Builder(new EtlNextLinkPagination("/next"), InitialSeed, policy);

        var error = Assert.Throws<EtlHttpRequestBuilderException>(() =>
            builder.NextContinuation(new EtlHttpRequest("shard-a", null), "next"));

        Assert.Equal("etl.http.continuation-too-large", error.Code);
    }

    [Fact]
    public void Next_link_equivalent_to_current_seed_is_rejected_before_it_can_create_a_sentinel_page()
    {
        var builder = Builder(new EtlNextLinkPagination("/next"));

        var error = Assert.Throws<EtlHttpRequestBuilderException>(() =>
            builder.NextContinuation(new EtlHttpRequest("shard-a", null), "?tenant=acme"));

        Assert.Equal("etl.http.pagination-cycle", error.Code);
    }

    [Fact]
    public void Later_next_link_back_to_the_shard_seed_is_rejected_before_a_duplicate_request()
    {
        var builder = Builder(new EtlNextLinkPagination("/next"));
        var current = new EtlHttpRequest("shard-a", "https://source.example/api/v1/next?page=2");

        var error = Assert.Throws<EtlHttpRequestBuilderException>(() =>
            builder.NextContinuation(current, "items?tenant=acme"));

        Assert.Equal("etl.http.pagination-cycle", error.Code);
    }

    [Fact]
    public void Token_continuation_matching_existing_seed_cursor_is_rejected_before_a_duplicate_request()
    {
        var seed = new Uri("https://source.example/api/v1/items?tenant=acme&cursor=first");
        var builder = Builder(new EtlTokenPagination("/next", "cursor"), seed);

        var error = Assert.Throws<EtlHttpRequestBuilderException>(() =>
            builder.NextContinuation(new EtlHttpRequest("shard-a", null), "first"));

        Assert.Equal("etl.http.pagination-cycle", error.Code);
    }

    [Fact]
    public void Token_continuation_matching_current_request_is_rejected_as_a_cycle()
    {
        var builder = Builder(new EtlTokenPagination("/next", "cursor"));
        var current = new EtlHttpRequest("shard-a", "repeat");

        var error = Assert.Throws<EtlHttpRequestBuilderException>(() => builder.NextContinuation(current, "repeat"));

        Assert.Equal("etl.http.pagination-cycle", error.Code);
    }

    [Fact]
    public async Task Page_cache_stores_raw_json_but_recomputes_normalized_next_link_on_reopen()
    {
        var source = Source(new EtlNextLinkPagination("/next"));
        var pipeline = EtlV2TestPipelines.Create() with { Source = source };
        var binding = Binding(InitialSeed);
        var admission = Admission(pipeline, binding);
        var builder = new EtlHttpRequestBuilder(source, binding, pipeline.Http);
        var cache = new EtlHttpPageCache(_root, admission, 0, builder);
        var rawBody = Encoding.UTF8.GetBytes("{\"records\":[{\"id\":1,\"name\":\"Ada\"}],\"next\":\"next?page=2\"}");
        var request = new EtlHttpRequest("shard-a", null);

        var published = await cache.PublishAsync(0, request, Guid.Parse("65a0e0be-f89b-4c10-8cf8-4878bffbc10c"),
            1, new DateTimeOffset(2026, 10, 21, 12, 30, 0, TimeSpan.Zero), rawBody);
        var reopened = await new EtlHttpPageCache(_root, admission, 0,
            new EtlHttpRequestBuilder(source, Binding(InitialSeed), pipeline.Http)).ReadAsync(0, request);

        Assert.NotNull(reopened);
        Assert.Equal("https://source.example/api/v1/next?page=2", published.Descriptor.NextContinuation);
        Assert.Equal(published.Descriptor, reopened.Descriptor);
        Assert.Equal(published.Descriptor.NextContinuation, reopened.DecodedPage.Continuation);
        Assert.Equal(rawBody, await File.ReadAllBytesAsync(Path.Combine(_root, published.Descriptor.Body.Key)));
        Assert.Equal(rawBody, reopened.RawBody.ToArray());
    }

    [Fact]
    public async Task Invalid_next_link_does_not_publish_cache_objects()
    {
        var source = Source(new EtlNextLinkPagination("/next"));
        var pipeline = EtlV2TestPipelines.Create() with { Source = source };
        var binding = Binding(InitialSeed);
        var cache = new EtlHttpPageCache(_root, Admission(pipeline, binding), 0,
            new EtlHttpRequestBuilder(source, binding, pipeline.Http));
        var rawBody = Encoding.UTF8.GetBytes("{\"records\":[],\"next\":\"https://other.example/api/v1/next\"}");

        var error = await Assert.ThrowsAsync<EtlHttpPageCacheException>(() => cache.PublishAsync(0,
            new EtlHttpRequest("shard-a", null), Guid.NewGuid(), 1,
            new DateTimeOffset(2026, 10, 21, 12, 30, 0, TimeSpan.Zero), rawBody).AsTask());

        Assert.Equal("etl.http-endpoint-unavailable", error.Code);
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public void Cache_builder_must_match_the_admitted_source_policy_and_shard_pin()
    {
        var source = Source(new EtlNextLinkPagination("/next"));
        var pipeline = EtlV2TestPipelines.Create() with { Source = source };
        var admittedBinding = Binding(InitialSeed);
        var admission = Admission(pipeline, admittedBinding);
        var wrongRevision = Binding(InitialSeed, revision: "source-revision-2");
        var wrongShard = Binding(InitialSeed, shardId: "shard-b");
        var wrongSeed = Binding(new Uri("https://source.example/api/v1/next?tenant=acme"), handle: new string('c', 64));
        var wrongPolicy = pipeline.Http with { MaxContinuationBytes = pipeline.Http.MaxContinuationBytes - 1 };

        Assert.Throws<EtlHttpPageCacheException>(() => new EtlHttpPageCache(_root, admission, 0,
            new EtlHttpRequestBuilder(source, wrongRevision, pipeline.Http)));
        Assert.Throws<EtlHttpPageCacheException>(() => new EtlHttpPageCache(_root, admission, 0,
            new EtlHttpRequestBuilder(source, wrongShard, pipeline.Http)));
        Assert.Throws<EtlHttpPageCacheException>(() => new EtlHttpPageCache(_root, admission, 0,
            new EtlHttpRequestBuilder(source, wrongSeed, pipeline.Http)));
        Assert.Throws<EtlHttpPageCacheException>(() => new EtlHttpPageCache(_root, admission, 0,
            new EtlHttpRequestBuilder(source, admittedBinding, wrongPolicy)));
    }

    private static EtlHttpRequestBuilder Builder(EtlHttpPagination pagination, Uri? seed = null, EtlHttpPolicy? policy = null)
    {
        var source = Source(pagination);
        var binding = Binding(seed ?? InitialSeed);
        return new EtlHttpRequestBuilder(source, binding, policy ?? EtlV2TestPipelines.Create().Http);
    }

    private static EtlHttpJsonSource Source(EtlHttpPagination pagination) => new("source", "/records",
    [
        new EtlJsonField("id", "/id", EtlColumnType.Int64, Required: true),
        new EtlJsonField("name", "/name", EtlColumnType.String, Required: true)
    ], pagination);

    private static HostHttpResolvedBinding Binding(Uri seed, string revision = "source-revision-1", string shardId = "shard-a",
        string? handle = null) =>
        new("source", "Source", revision, handle ?? Handle, "Idempotency-Key",
            new HttpEndpointPolicy("https://source.example/api/v1/items",
                ["/api/v1/items", "/api/v1/next", "/api/v1/nested/page2", "/api/v1/nested/page3"], allowHttp: false),
            [new HostHttpShard(shardId, seed)], null, null);

    private static EtlConnectorRunAdmission Admission(EtlPipelineV2 pipeline, HostHttpResolvedBinding binding)
    {
        var document = EtlArtifactV2Json.Write(pipeline);
        var hash = EtlArtifactJson.Hash(document);
        var artifact = new EtlArtifact(EtlArtifactV2Json.FormatVersion,
            EtlArtifactJson.CreateArtifactId(EtlArtifactV2Json.FormatVersion, "orders", 1, hash), hash,
            "orders", 1, document);
        var scope = new EtlScope("tenant-a", "partition-a");
        var invocation = EtlRuntimeFixtures.Admission("request-builder", scope).Invocation with { Artifact = artifact };
        var source = new EtlConnectorBindingRevision(EtlConnectorKind.Http,
            new EtlBindingRevision(binding.Alias, binding.Revision, binding.Handle, null));
        var fileOutput = new EtlConnectorBindingRevision(EtlConnectorKind.Jsonl,
            new EtlBindingRevision("output", "output-revision", new string('b', 64), null));
        return new EtlConnectorRunAdmission(invocation, new EtlConnectorRunBindings(source, null,
            fileOutput, fileOutput, Array.AsReadOnly(binding.Shards.Select(shard => shard.Id).ToArray())));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
