using System.Net;
using Elsa.Integrations.Connectors.Slack.Activities;
using Elsa.Integrations.Sdk.Harness;

namespace Elsa.Integrations.Connectors.Slack.Tests;

public sealed class ListChannelsAndUploadTests
{
    [Fact]
    public async Task ListChannels_follows_cursors_until_empty()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.EnqueueJson("""{"ok":true,"channels":[{"id":"C1","name":"one","is_private":false,"is_archived":false}],"response_metadata":{"next_cursor":"page-2"}}""");
        harness.Http.EnqueueJson("""{"ok":true,"channels":[{"id":"C2","name":"two","is_private":true,"is_archived":false}],"response_metadata":{"next_cursor":""}}""");
        var activity = SlackTestSupport.Activity<ListChannels>(harness, list =>
        {
            list.Types = "public_channel,private_channel";
            list.Limit = 50;
            list.MaxPages = 5;
        });

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal(2, result.Count);
        Assert.Equal("C1", result[0].Id);
        Assert.Equal("two", result[1].Name);
        Assert.True(result[1].IsPrivate);
        Assert.Equal(2, harness.Http.Recorded.Count);
        SlackTestSupport.AssertSlackMethod(harness.Http.Recorded[0], "conversations.list");
        SlackTestSupport.AssertSlackMethod(harness.Http.Recorded[1], "conversations.list");
        Assert.Equal("page-2", SlackTestSupport.BodyJson(harness.Http.Recorded[1]).GetProperty("cursor").GetString());
    }

    [Fact]
    public async Task ListChannels_stops_at_max_pages()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.EnqueueJson("""{"ok":true,"channels":[{"id":"C1","name":"one"}],"response_metadata":{"next_cursor":"page-2"}}""");
        harness.Http.EnqueueJson("""{"ok":true,"channels":[{"id":"C2","name":"two"}],"response_metadata":{"next_cursor":"page-3"}}""");
        harness.Http.EnqueueJson("""{"ok":true,"channels":[{"id":"C3","name":"three"}],"response_metadata":{"next_cursor":""}}""");
        var activity = SlackTestSupport.Activity<ListChannels>(harness, list => list.MaxPages = 2);

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal(["C1", "C2"], result.Select(channel => channel.Id));
        Assert.Equal(2, harness.Http.Recorded.Count);
    }

    [Fact]
    public async Task UploadFile_skips_authorization_on_the_upload_url()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.Respond = (request, _) =>
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (path.EndsWith("files.getUploadURLExternal", StringComparison.Ordinal))
                return CannedHttpResponse.Json("""{"ok":true,"upload_url":"https://files.slack.com/upload/v1/abc","file_id":"F1"}""");
            if (request.RequestUri?.Host == "files.slack.com")
                return CannedHttpResponse.WithStatus(HttpStatusCode.OK, "OK");
            if (path.EndsWith("files.completeUploadExternal", StringComparison.Ordinal))
                return CannedHttpResponse.Json("""{"ok":true,"files":[{"id":"F1"}]}""");
            return CannedHttpResponse.Json("""{"ok":false,"error":"unknown_method"}""");
        };

        var activity = SlackTestSupport.Activity<UploadFile>(harness, upload =>
        {
            upload.Channel = "C1";
            upload.Filename = "note.txt";
            upload.Content = "hello"u8.ToArray();
            upload.Title = "Note";
            upload.InitialComment = "see attached";
            upload.ThreadTs = "123.456";
        });

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal(["F1"], result.FileIds);
        Assert.Equal(3, harness.Http.Recorded.Count);

        SlackTestSupport.AssertSlackMethod(harness.Http.Recorded[0], "files.getUploadURLExternal");
        var start = SlackTestSupport.BodyJson(harness.Http.Recorded[0]);
        Assert.Equal("note.txt", start.GetProperty("filename").GetString());
        Assert.Equal(5, start.GetProperty("length").GetInt32());

        var upload = harness.Http.Recorded[1];
        Assert.Equal("https://files.slack.com/upload/v1/abc", upload.RequestUri?.ToString());
        Assert.Null(upload.Authorization);
        Assert.DoesNotContain(upload.Headers, header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("hello", upload.Body);

        SlackTestSupport.AssertSlackMethod(harness.Http.Recorded[2], "files.completeUploadExternal");
        var complete = SlackTestSupport.BodyJson(harness.Http.Recorded[2]);
        Assert.Equal("C1", complete.GetProperty("channel_id").GetString());
        Assert.Equal("F1", complete.GetProperty("files")[0].GetProperty("id").GetString());
        Assert.Equal("Note", complete.GetProperty("files")[0].GetProperty("title").GetString());
        Assert.Equal("see attached", complete.GetProperty("initial_comment").GetString());
        Assert.Equal("123.456", complete.GetProperty("thread_ts").GetString());
    }
}
