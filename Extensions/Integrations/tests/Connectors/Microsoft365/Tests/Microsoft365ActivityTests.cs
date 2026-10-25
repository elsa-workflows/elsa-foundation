using System.Text.Json;
using Elsa.Integrations.Connectors.Microsoft365.Activities;
using Elsa.Integrations.Sdk.Harness;

namespace Elsa.Integrations.Connectors.Microsoft365.Tests;

public sealed class Microsoft365ActivityTests
{
    [Fact]
    public async Task SendMail_posts_me_sendMail_with_bearer()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseStatus = System.Net.HttpStatusCode.Accepted;
        harness.Http.ResponseContent = "";
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com, grace@example.com";
            send.Cc = """["cc@example.com"]""";
            send.Subject = "Hello";
            send.Body = "World";
            send.BodyType = "HTML";
            send.SaveToSentItems = false;
        });

        var transition = await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context());
        Assert.Equal(Elsa.Activities.Runtime.Core.Models.ActivityTransitionKind.Complete, transition.Kind);
        var request = Assert.Single(harness.Http.Recorded);
        Microsoft365TestSupport.AssertGraph(request, HttpMethod.Post, "me/sendMail");
        var body = Microsoft365TestSupport.BodyJson(request);
        Assert.Equal("Hello", body.GetProperty("message").GetProperty("subject").GetString());
        Assert.Equal("HTML", body.GetProperty("message").GetProperty("body").GetProperty("contentType").GetString());
        Assert.Equal(2, body.GetProperty("message").GetProperty("toRecipients").GetArrayLength());
        Assert.Equal("cc@example.com", body.GetProperty("message").GetProperty("ccRecipients")[0].GetProperty("emailAddress").GetProperty("address").GetString());
        Assert.False(body.GetProperty("saveToSentItems").GetBoolean());
    }

    [Fact]
    public async Task SendMail_uses_users_path_when_user_id_is_set()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseStatus = System.Net.HttpStatusCode.Accepted;
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Shared";
            send.Body = "box";
            send.UserId = "shared@contoso.com";
        });

        await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context());
        Microsoft365TestSupport.AssertGraph(
            Assert.Single(harness.Http.Recorded),
            HttpMethod.Post,
            "users/shared%40contoso.com/sendMail");
    }

    [Fact]
    public async Task SendMail_faults_when_to_json_is_malformed()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "[not-json";
            send.Subject = "Hello";
            send.Body = "World";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }

    [Fact]
    public async Task SendMail_faults_when_body_type_is_unknown()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
            send.BodyType = "Markdown";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }

    [Fact]
    public async Task ListMessages_gets_inbox_with_filter_and_top()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"value":[{"id":"m1","subject":"Hi","bodyPreview":"Hello","isRead":false,"from":{"emailAddress":{"address":"ada@example.com","name":"Ada"}}}]}""";
        var activity = Microsoft365TestSupport.Activity<ListMessages>(harness, list =>
        {
            list.Filter = "isRead eq false";
            list.Top = 10;
        });

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        var message = Assert.Single(result);
        Assert.Equal("m1", message.Id);
        Assert.Equal("Hi", message.Subject);
        Assert.Equal("ada@example.com", message.From?.Address);
        var request = Assert.Single(harness.Http.Recorded);
        Microsoft365TestSupport.AssertBearer(request);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/me/mailFolders/inbox/messages", request.RequestUri?.ToString(), StringComparison.Ordinal);
        Assert.Contains("$top=10", request.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Contains("isRead", request.RequestUri?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateEvent_posts_calendar_event()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"id":"e1","subject":"Sync","start":{"dateTime":"2026-10-25T14:00:00","timeZone":"UTC"},"end":{"dateTime":"2026-10-25T15:00:00","timeZone":"UTC"},"location":{"displayName":"Room A"},"webLink":"https://outlook.office.com/e1"}""";
        var activity = Microsoft365TestSupport.Activity<CreateEvent>(harness, create =>
        {
            create.Subject = "Sync";
            create.Start = new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.Zero);
            create.End = new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);
            create.Location = "Room A";
            create.Attendees = """["ada@example.com"]""";
        });

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("e1", result.Id);
        Assert.Equal("Sync", result.Subject);
        Assert.Equal("Room A", result.Location);
        var request = Assert.Single(harness.Http.Recorded);
        Microsoft365TestSupport.AssertGraph(request, HttpMethod.Post, "me/events");
        var body = Microsoft365TestSupport.BodyJson(request);
        Assert.Equal("Sync", body.GetProperty("subject").GetString());
        Assert.Equal("2026-10-25T14:00:00", body.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("UTC", body.GetProperty("start").GetProperty("timeZone").GetString());
        Assert.Equal("ada@example.com", body.GetProperty("attendees")[0].GetProperty("emailAddress").GetProperty("address").GetString());
    }

    [Fact]
    public async Task CreateEvent_converts_a_plus_two_offset_to_the_utc_default()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"id":"e1","subject":"Offset"}""";
        var activity = Microsoft365TestSupport.Activity<CreateEvent>(harness, create =>
        {
            create.Subject = "Offset";
            create.Start = new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.FromHours(2));
            create.End = new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.FromHours(2));
        });

        Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        var body = Microsoft365TestSupport.BodyJson(Assert.Single(harness.Http.Recorded));
        Assert.Equal("2026-10-25T12:00:00", body.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("UTC", body.GetProperty("start").GetProperty("timeZone").GetString());
        Assert.Equal("2026-10-25T13:00:00", body.GetProperty("end").GetProperty("dateTime").GetString());
        Assert.Equal("UTC", body.GetProperty("end").GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task CreateEvent_converts_an_explicit_zone_across_a_dst_boundary()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"id":"e1","subject":"DST"}""";
        var activity = Microsoft365TestSupport.Activity<CreateEvent>(harness, create =>
        {
            create.Subject = "DST";
            create.Start = new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.FromHours(2));
            create.End = new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.FromHours(2));
            create.TimeZone = "Europe/Amsterdam";
        });

        Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        var body = Microsoft365TestSupport.BodyJson(Assert.Single(harness.Http.Recorded));
        Assert.Equal("2026-10-25T13:00:00", body.GetProperty("start").GetProperty("dateTime").GetString());
        Assert.Equal("Europe/Amsterdam", body.GetProperty("start").GetProperty("timeZone").GetString());
        Assert.Equal("2026-10-25T14:00:00", body.GetProperty("end").GetProperty("dateTime").GetString());
        Assert.Equal("Europe/Amsterdam", body.GetProperty("end").GetProperty("timeZone").GetString());
    }

    [Fact]
    public async Task CreateEvent_faults_when_the_time_zone_is_unknown()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<CreateEvent>(harness, create =>
        {
            create.Subject = "Nope";
            create.Start = new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.Zero);
            create.End = new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);
            create.TimeZone = "Not/AZone";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }

    [Fact]
    public async Task CreateEvent_faults_when_attendees_is_not_an_array()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<CreateEvent>(harness, create =>
        {
            create.Subject = "Sync";
            create.Start = new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.Zero);
            create.End = new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.Zero);
            create.Attendees = """{"email":"ada@example.com"}""";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }

    [Fact]
    public async Task ListEvents_gets_calendar_view()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"value":[{"id":"e1","subject":"Standup","start":{"dateTime":"2026-10-25T09:00:00","timeZone":"UTC"},"end":{"dateTime":"2026-10-25T09:15:00","timeZone":"UTC"}}]}""";
        var activity = Microsoft365TestSupport.Activity<ListEvents>(harness, list =>
        {
            list.StartDateTime = new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.Zero);
            list.EndDateTime = new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.Zero);
            list.Top = 25;
        });

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("Standup", Assert.Single(result).Subject);
        var request = Assert.Single(harness.Http.Recorded);
        Microsoft365TestSupport.AssertBearer(request);
        Assert.Contains("/me/calendarView?", request.RequestUri?.ToString(), StringComparison.Ordinal);
        Assert.Contains("startDateTime=", request.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Contains("endDateTime=", request.RequestUri?.Query, StringComparison.Ordinal);
        Assert.Contains("$top=25", request.RequestUri?.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListEvents_converts_a_plus_two_offset_to_utc()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"value":[]}""";
        var activity = Microsoft365TestSupport.Activity<ListEvents>(harness, list =>
        {
            list.StartDateTime = new DateTimeOffset(2026, 10, 25, 14, 0, 0, TimeSpan.FromHours(2));
            list.EndDateTime = new DateTimeOffset(2026, 10, 25, 15, 0, 0, TimeSpan.FromHours(2));
        });

        Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        var query = Uri.UnescapeDataString(Assert.Single(harness.Http.Recorded).RequestUri?.Query ?? "");
        Assert.Contains("startDateTime=2026-10-25T12:00:00", query, StringComparison.Ordinal);
        Assert.Contains("endDateTime=2026-10-25T13:00:00", query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetUser_returns_user()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"id":"u1","displayName":"Ada Lovelace","mail":"ada@example.com","userPrincipalName":"ada@example.com"}""";
        var activity = Microsoft365TestSupport.Activity<GetUser>(harness, get => get.UserId = "ada@example.com");

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.True(result.Found);
        Assert.Equal("u1", result.User?.Id);
        Assert.Equal("Ada Lovelace", result.User?.DisplayName);
        Microsoft365TestSupport.AssertGraph(
            Assert.Single(harness.Http.Recorded),
            HttpMethod.Get,
            "users/ada%40example.com");
    }

    [Fact]
    public async Task GetUser_returns_not_found_instead_of_faulting()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseStatus = System.Net.HttpStatusCode.NotFound;
        harness.Http.ResponseContent = Microsoft365TestSupport.GraphError("Request_ResourceNotFound", "User not found");
        var activity = Microsoft365TestSupport.Activity<GetUser>(harness, get => get.UserId = "missing@example.com");

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.False(result.Found);
        Assert.Null(result.User);
    }

    [Fact]
    public async Task DownloadFile_gets_content_by_path()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = "hello-file";
        harness.Http.ResponseHeaders["Content-Disposition"] = "attachment; filename=\"note.txt\"";
        var activity = Microsoft365TestSupport.Activity<DownloadFile>(harness, download => download.ItemPath = "/Reports/note.txt");

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("hello-file"u8.ToArray(), result.Content);
        Assert.Equal("note.txt", result.FileName);
        var request = Assert.Single(harness.Http.Recorded);
        Microsoft365TestSupport.AssertGraph(request, HttpMethod.Get, "me/drive/root:/Reports/note.txt:/content");
    }

    [Fact]
    public async Task DownloadFile_round_trips_non_utf8_bytes()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var bytes = new byte[] { 0x00, 0xFF, 0xC3, 0x28 };
        harness.Http.Enqueue(CannedHttpResponse.Bytes(bytes));
        var activity = Microsoft365TestSupport.Activity<DownloadFile>(harness, download => download.ItemPath = "/Reports/binary.bin");

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal(bytes, result.Content);
        Microsoft365TestSupport.AssertGraph(
            Assert.Single(harness.Http.Recorded),
            HttpMethod.Get,
            "me/drive/root:/Reports/binary.bin:/content");
    }

    [Fact]
    public async Task DownloadFile_gets_content_by_item_id()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = "bytes";
        var activity = Microsoft365TestSupport.Activity<DownloadFile>(harness, download =>
        {
            download.DriveId = "b!drive";
            download.ItemId = "01ITEM";
        });

        await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context());
        Microsoft365TestSupport.AssertGraph(
            Assert.Single(harness.Http.Recorded),
            HttpMethod.Get,
            "drives/b%21drive/items/01ITEM/content");
    }

    [Fact]
    public async Task PostTeamsChannelMessage_posts_html_and_attachments()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"id":"msg1","webUrl":"https://teams.microsoft.com/l/message/msg1"}""";
        var activity = Microsoft365TestSupport.Activity<PostTeamsChannelMessage>(harness, post =>
        {
            post.TeamId = "team-1";
            post.ChannelId = "channel-1";
            post.Content = "<p>hello</p><attachment id=\"1\"></attachment>";
            post.ContentType = "html";
            post.Attachments = """[{"id":"1","contentType":"application/vnd.microsoft.card.adaptive","content":"{}"}]""";
        });

        var result = Microsoft365TestSupport.Complete(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("msg1", result.Id);
        var request = Assert.Single(harness.Http.Recorded);
        Microsoft365TestSupport.AssertGraph(request, HttpMethod.Post, "teams/team-1/channels/channel-1/messages");
        var body = Microsoft365TestSupport.BodyJson(request);
        Assert.Equal("html", body.GetProperty("body").GetProperty("contentType").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("attachments").ValueKind);
    }

    [Fact]
    public async Task PostTeamsChannelMessage_faults_when_attachments_is_not_json()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<PostTeamsChannelMessage>(harness, post =>
        {
            post.TeamId = "team-1";
            post.ChannelId = "channel-1";
            post.Content = "hello";
            post.Attachments = "{not-json";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }

    [Fact]
    public async Task PostTeamsChannelMessage_faults_when_attachments_are_not_referenced_in_html()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<PostTeamsChannelMessage>(harness, post =>
        {
            post.TeamId = "team-1";
            post.ChannelId = "channel-1";
            post.Content = "hello";
            post.ContentType = "text";
            post.Attachments = """[{"id":"1","contentType":"application/vnd.microsoft.card.adaptive","content":"{}"}]""";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }

    [Fact]
    public async Task PostTeamsChannelMessage_faults_when_html_is_missing_the_attachment_tag()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<PostTeamsChannelMessage>(harness, post =>
        {
            post.TeamId = "team-1";
            post.ChannelId = "channel-1";
            post.Content = "<p>hello</p>";
            post.ContentType = "html";
            post.Attachments = """[{"id":"1","contentType":"application/vnd.microsoft.card.adaptive","content":"{}"}]""";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }

    [Fact]
    public async Task PostTeamsChannelMessage_faults_when_content_type_is_unknown()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<PostTeamsChannelMessage>(harness, post =>
        {
            post.TeamId = "team-1";
            post.ChannelId = "channel-1";
            post.Content = "hello";
            post.ContentType = "markdown";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }

    [Fact]
    public async Task Graph_base_url_must_be_https()
    {
        await using var harness = Microsoft365TestSupport.CreateHarness();
        var activity = Microsoft365TestSupport.Activity<SendMail>(harness, send =>
        {
            send.To = "ada@example.com";
            send.Subject = "Hello";
            send.Body = "World";
            send.GraphBaseUrl = "http://graph.microsoft.com";
        });

        var fault = Microsoft365TestSupport.Fault(await activity.ExecuteForTestsAsync(Microsoft365TestSupport.Context()));
        Assert.Equal("invalid_input", fault.Code);
        Assert.Empty(harness.Http.Recorded);
    }
}
