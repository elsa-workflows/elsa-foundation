using System.Net;
using System.Text;
using Elsa.Versioning.Publisher.Tests.Support;

namespace Elsa.Versioning.Publisher.Tests;

/// <summary>
/// The NuGet V3 client under the publisher: how each answer of a feed maps to what the publisher does. Getting a 409, or a
/// failed read, wrong is the silent direction: a duplicate taken for a success would skip FR-018, and an unanswered
/// listing taken for an empty one would let the bootstrap past old previews.
/// </summary>
public sealed class NuGetFeedTests : IDisposable
{
    private const string ApiKey = "the-api-key";
    private static readonly Uri ServiceIndex = new("https://feed.example/v3/index.json");

    private readonly StubFeed stub = new();
    private readonly HttpClient http;
    private readonly NuGetFeed feed;
    private readonly string package = Path.Join(Path.GetTempPath(), $"{nameof(NuGetFeedTests)}-{Guid.NewGuid():N}.nupkg");

    public NuGetFeedTests()
    {
        http = new HttpClient(stub);
        feed = new NuGetFeed(http, ServiceIndex, ApiKey);
        File.WriteAllBytes(package, SyntheticPackages.Bytes("Elsa.Tasks", "4.0.1-preview", "sha256:abc", new string('1', 40)));
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, PushStatus.Pushed)]
    [InlineData(HttpStatusCode.Accepted, PushStatus.Pushed)]
    [InlineData(HttpStatusCode.OK, PushStatus.Pushed)]
    [InlineData(HttpStatusCode.Conflict, PushStatus.AlreadyExists)]
    [InlineData(HttpStatusCode.BadRequest, PushStatus.Failed)]
    [InlineData(HttpStatusCode.Forbidden, PushStatus.Failed)]
    [InlineData(HttpStatusCode.InternalServerError, PushStatus.Failed)]
    public async Task A_push_maps_the_feeds_answer(HttpStatusCode answer, PushStatus expected)
    {
        stub.Answer = _ => new HttpResponseMessage(answer) { Content = new StringContent("the feed's reason") };

        Assert.Equal(expected, (await feed.PushAsync(package, CancellationToken.None)).Status);
    }

    /// <summary>The protocol's push: a multipart PUT to the publish resource, carrying the package and the key.</summary>
    [Fact]
    public async Task A_push_is_the_protocols_multipart_put_with_the_api_key()
    {
        stub.Answer = _ => new HttpResponseMessage(HttpStatusCode.Created);

        await feed.PushAsync(package, CancellationToken.None);

        var push = stub.Requests.Single(request => request.Method == HttpMethod.Put);
        Assert.Equal("https://feed.example/api/v2/package", push.Url);
        Assert.Equal(ApiKey, push.ApiKey);
        Assert.Equal("package", push.PartName);
        Assert.Equal(File.ReadAllBytes(package), push.Body);
    }

    /// <summary>A push that got no answer may have landed; it is a failure, never a success.</summary>
    [Fact]
    public async Task A_push_that_gets_no_answer_fails()
    {
        stub.Answer = _ => throw new HttpRequestException("The connection was reset.");

        var result = await feed.PushAsync(package, CancellationToken.None);

        Assert.Equal(PushStatus.Failed, result.Status);
        Assert.Contains("connection was reset", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versions_are_listed_from_the_package_content_resource_and_a_404_holds_none()
    {
        stub.Answer = request => request.RequestUri!.AbsolutePath switch
        {
            "/v3/flat/elsa.tasks/index.json" => Json("""{"versions":["4.0.0-preview.1","4.0.0-preview.2"]}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        };

        Assert.Equal(["4.0.0-preview.1", "4.0.0-preview.2"], await feed.ListVersionsAsync("Elsa.Tasks", CancellationToken.None));
        Assert.Empty(await feed.ListVersionsAsync("Elsa.Http", CancellationToken.None));
    }

    /// <summary>A listing the feed did not answer is not an empty one.</summary>
    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_listing_the_feed_does_not_answer_throws_rather_than_reading_as_empty(HttpStatusCode answer)
    {
        stub.Answer = _ => new HttpResponseMessage(answer);

        await Assert.ThrowsAsync<FeedException>(() => feed.ListVersionsAsync("Elsa.Tasks", CancellationToken.None));
    }

    [Fact]
    public async Task The_fingerprint_is_read_from_the_feeds_copy_of_the_version()
    {
        stub.Answer = request => request.RequestUri!.AbsolutePath == "/v3/flat/elsa.tasks/4.0.1-preview/elsa.tasks.4.0.1-preview.nupkg"
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(package)) }
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        Assert.Equal("sha256:abc", await feed.ReadFingerprintAsync("Elsa.Tasks", "4.0.1-Preview", CancellationToken.None));
        await Assert.ThrowsAsync<FeedException>(() => feed.ReadFingerprintAsync("Elsa.Tasks", "4.0.2-preview", CancellationToken.None));
    }

    /// <summary>A copy without a fingerprint, such as an old preview, cannot confirm anything.</summary>
    [Fact]
    public async Task A_copy_without_a_fingerprint_cannot_be_read()
    {
        stub.Answer = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(SyntheticPackages.Bytes("Elsa.Tasks", "4.0.1-preview", fingerprint: null, new string('1', 40)))
        };

        await Assert.ThrowsAsync<FeedException>(() => feed.ReadFingerprintAsync("Elsa.Tasks", "4.0.1-preview", CancellationToken.None));
    }

    /// <summary>A service index without the resources the protocol needs fails every request rather than guessing an address.</summary>
    [Fact]
    public async Task A_service_index_without_the_resources_fails_every_request()
    {
        stub.Index = """{"version":"3.0.0","resources":[]}""";

        Assert.Equal(PushStatus.Failed, (await feed.PushAsync(package, CancellationToken.None)).Status);
        await Assert.ThrowsAsync<FeedException>(() => feed.ListVersionsAsync("Elsa.Tasks", CancellationToken.None));
    }

    public void Dispose()
    {
        http.Dispose();
        File.Delete(package);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(HttpMethod Method, string Url, string? ApiKey, string? PartName, byte[]? Body);

    /// <summary>A feed's HTTP surface: its service index, and whatever <see cref="Answer"/> says to everything else.</summary>
    private sealed class StubFeed : HttpMessageHandler
    {
        public string Index { get; set; } = """
            {
              "version": "3.0.0",
              "resources": [
                { "@id": "https://feed.example/api/v2/package", "@type": "PackagePublish/2.0.0" },
                { "@id": "https://feed.example/v3/flat", "@type": "PackageBaseAddress/3.0.0" }
              ]
            }
            """;

        public Func<HttpRequestMessage, HttpResponseMessage> Answer { get; set; } = _ => new HttpResponseMessage(HttpStatusCode.NotFound);

        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri == ServiceIndex)
                return Json(Index);

            var part = (request.Content as MultipartFormDataContent)?.Single();
            Requests.Add(new CapturedRequest(
                request.Method,
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("X-NuGet-ApiKey", out var keys) ? keys.Single() : null,
                part?.Headers.ContentDisposition?.Name?.Trim('"'),
                part is null ? null : await part.ReadAsByteArrayAsync(cancellationToken)));
            return Answer(request);
        }
    }
}
