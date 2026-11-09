using System.Net;
using Elsa.Integrations.Connectors.Microsoft365;
using Elsa.Integrations.Connectors.Microsoft365.Activities;
using Elsa.Integrations.Sdk.Harness;

namespace Elsa.Integrations.Connectors.Microsoft365.Tests;

public sealed class Microsoft365PagingAndUploadTests
{
    [Fact]
    public async Task ListMessages_follows_next_link_until_max_items()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.EnqueueJson("""{"value":[{"id":"m1","subject":"one"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skip=1"}""");
        harness.Http.EnqueueJson("""{"value":[{"id":"m2","subject":"two"},{"id":"m3","subject":"three"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skip=3"}""");
        var activity = Microsoft365TestSupport.Activity<ListMessages>(harness, list => list.MaxItems = 2);

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal(["m1", "m2"], result.Select(message => message.Id));
        Assert.Equal(2, harness.Http.Recorded.Count);
        Microsoft365TestSupport.AssertBearer(harness.Http.Recorded[0]);
        Microsoft365TestSupport.AssertBearer(harness.Http.Recorded[1]);
        Assert.Equal("https://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skip=1", harness.Http.Recorded[1].RequestUri?.ToString());
    }

    [Fact]
    public async Task ListEvents_does_not_follow_a_next_link_off_the_graph_host()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.EnqueueJson("""{"value":[{"id":"e1","subject":"one"}],"@odata.nextLink":"https://evil.example/steal"}""");
        harness.Http.EnqueueJson("""{"value":[{"id":"e2","subject":"two"}]}""");
        var activity = Microsoft365TestSupport.Activity<ListEvents>(harness, list =>
        {
            list.StartDateTime = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
            list.EndDateTime = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
        });

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal(["e1"], result.Select(item => item.Id));
        Assert.Single(harness.Http.Recorded);
    }

    [Fact]
    public async Task ListMessages_does_not_follow_an_http_next_link()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.EnqueueJson("""{"value":[{"id":"m1","subject":"one"}],"@odata.nextLink":"http://graph.microsoft.com/v1.0/me/mailFolders/inbox/messages?$skip=1"}""");
        var activity = Microsoft365TestSupport.Activity<ListMessages>(harness);

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal(["m1"], result.Select(message => message.Id));
        Assert.Single(harness.Http.Recorded);
    }

    [Fact]
    public async Task UploadFile_uses_simple_put_under_four_megabytes()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"id":"item-1","name":"note.txt","size":5,"webUrl":"https://1drv.ms/note"}""";
        var activity = Microsoft365TestSupport.Activity<UploadFile>(harness, upload =>
        {
            upload.FileName = "note.txt";
            upload.Content = "hello"u8.ToArray();
        });

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("item-1", result.Id);
        Assert.Equal("note.txt", result.Name);
        var request = Assert.Single(harness.Http.Recorded);
        Assert.Equal(HttpMethod.Put, request.Method);
        Microsoft365TestSupport.AssertBearer(request);
        Assert.StartsWith("https://graph.microsoft.com/v1.0/me/drive/root:/note.txt:/content", request.RequestUri?.ToString(), StringComparison.Ordinal);
        Microsoft365TestSupport.AssertConflictBehavior(request, "rename");
        Assert.Equal("hello", request.Body);
    }

    [Fact]
    public async Task UploadFile_simple_put_sends_an_explicit_conflict_behavior()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"id":"item-1","name":"note.txt","size":5}""";
        var activity = Microsoft365TestSupport.Activity<UploadFile>(harness, upload =>
        {
            upload.FileName = "note.txt";
            upload.Content = "hello"u8.ToArray();
            upload.ConflictBehavior = "replace";
        });

        Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        var request = Assert.Single(harness.Http.Recorded);
        Assert.Equal(HttpMethod.Put, request.Method);
        Microsoft365TestSupport.AssertConflictBehavior(request, "replace");
    }

    [Fact]
    public async Task UploadFile_faults_when_conflict_behavior_is_unknown()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<UploadFile>(harness, upload =>
        {
            upload.FileName = "note.txt";
            upload.Content = "hello"u8.ToArray();
            upload.ConflictBehavior = "upsert";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }

    [Fact]
    public async Task UploadFile_uses_a_session_across_two_or_more_chunks_without_authorization()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var ranges = new List<string>();
        harness.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("createUploadSession", StringComparison.Ordinal))
                return CannedHttpResponse.Json("""{"uploadUrl":"https://sn3302.up.1drv.com/up/abc"}""");
            if (request.RequestUri?.Host.EndsWith("1drv.com", StringComparison.OrdinalIgnoreCase) == true)
            {
                var range = request.Content?.Headers.ContentRange?.ToString() ?? "";
                ranges.Add(range);
                return range.Contains("8-8/9", StringComparison.Ordinal)
                    ? CannedHttpResponse.Json("""{"id":"item-2","name":"big.bin","size":9}""", HttpStatusCode.Created)
                    : CannedHttpResponse.WithStatus(HttpStatusCode.Accepted, "");
            }

            return CannedHttpResponse.Json(Microsoft365TestSupport.GraphError("invalidRequest", "unexpected"));
        };

        var activity = Microsoft365TestSupport.Activity<UploadFile>(harness, upload =>
        {
            upload.ItemPath = "/Reports/big.bin";
            upload.Content = "123456789"u8.ToArray();
            upload.ConflictBehavior = "replace";
            upload.Upload = new Microsoft365UploadOptions
            {
                SimpleUploadMaxBytes = 4,
                ChunkSizeBytes = 4
            };
        });

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("item-2", result.Id);
        Assert.Equal(4, harness.Http.Recorded.Count);

        Microsoft365TestSupport.AssertGraph(harness.Http.Recorded[0], HttpMethod.Post, "me/drive/root:/Reports/big.bin:/createUploadSession");
        Assert.Equal("replace", Microsoft365TestSupport.BodyJson(harness.Http.Recorded[0]).GetProperty("item").GetProperty("@microsoft.graph.conflictBehavior").GetString());

        for (var i = 1; i <= 3; i++)
        {
            var chunk = harness.Http.Recorded[i];
            Assert.Equal("https://sn3302.up.1drv.com/up/abc", chunk.RequestUri?.ToString());
            Assert.Null(chunk.Authorization);
            Assert.DoesNotContain(chunk.Headers, header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(["bytes 0-3/9", "bytes 4-7/9", "bytes 8-8/9"], ranges);
    }

    [Theory]
    [InlineData("https://example.com/up/abc")]
    [InlineData("https://blob.core.windows.net/up/abc")]
    [InlineData("https://upload.microsoft.com/up/abc")]
    [InlineData("https://login.live.com/up/abc")]
    public async Task UploadFile_faults_when_the_upload_url_is_not_a_sharepoint_or_onedrive_host(string uploadUrl)
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("createUploadSession", StringComparison.Ordinal))
                return CannedHttpResponse.Json("{\"uploadUrl\":\"" + uploadUrl + "\"}");
            return CannedHttpResponse.Json(Microsoft365TestSupport.GraphError("invalidRequest", "unexpected"));
        };

        var activity = Microsoft365TestSupport.Activity<UploadFile>(harness, upload =>
        {
            upload.FileName = "big.bin";
            upload.Content = "123456789"u8.ToArray();
            upload.Upload = new Microsoft365UploadOptions { SimpleUploadMaxBytes = 4, ChunkSizeBytes = 4 };
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_response", fault.Code);
        Assert.Single(harness.Http.Recorded);
    }

    [Fact]
    public async Task UploadFile_accepts_any_2xx_from_the_upload_host()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.Contains("createUploadSession", StringComparison.Ordinal))
                return CannedHttpResponse.Json("""{"uploadUrl":"https://contoso.sharepoint.com/up/abc"}""");
            if (request.RequestUri?.Host.Contains("sharepoint.com", StringComparison.OrdinalIgnoreCase) == true)
                return CannedHttpResponse.Json("""{"id":"item-3","name":"ok.bin"}""", HttpStatusCode.PartialContent);
            return CannedHttpResponse.Json(Microsoft365TestSupport.GraphError("invalidRequest", "unexpected"));
        };

        var activity = Microsoft365TestSupport.Activity<UploadFile>(harness, upload =>
        {
            upload.FileName = "ok.bin";
            upload.Content = "12345"u8.ToArray();
            upload.Upload = new Microsoft365UploadOptions { SimpleUploadMaxBytes = 2, ChunkSizeBytes = 8 };
        });

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("item-3", result.Id);
        Assert.Equal(2, harness.Http.Recorded.Count);
        Assert.Null(harness.Http.Recorded[1].Authorization);
    }
}
