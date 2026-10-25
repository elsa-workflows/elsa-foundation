using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Integrations.Connectors.Slack.Activities;
using Elsa.Integrations.Connectors.Slack.Models;
using Elsa.Integrations.Sdk.Harness;

namespace Elsa.Integrations.Connectors.Slack.Tests;

public sealed class SlackActivityTests
{
    [Fact]
    public async Task SendMessage_posts_json_with_bearer_and_returns_channel_and_ts()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":true,"channel":"C1","ts":"123.456"}""";
        var activity = SlackTestSupport.Activity<SendMessage>(harness, send =>
        {
            send.Channel = "C1";
            send.Text = "hello";
            send.Blocks = """[{"type":"section","text":{"type":"plain_text","text":"hi"}}]""";
            send.UnfurlLinks = false;
        });

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));

        Assert.Equal("C1", result.Channel);
        Assert.Equal("123.456", result.Ts);
        var request = Assert.Single(harness.Http.Recorded);
        SlackTestSupport.AssertSlackMethod(request, "chat.postMessage");
        var body = SlackTestSupport.BodyJson(request);
        Assert.Equal("C1", body.GetProperty("channel").GetString());
        Assert.Equal("hello", body.GetProperty("text").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("blocks").ValueKind);
        Assert.False(body.GetProperty("unfurl_links").GetBoolean());
    }

    [Fact]
    public async Task ReplyInThread_sends_thread_ts()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":true,"channel":"C1","ts":"123.789"}""";
        var activity = SlackTestSupport.Activity<ReplyInThread>(harness, reply =>
        {
            reply.Channel = "C1";
            reply.ThreadTs = "123.456";
            reply.Text = "thread";
            reply.ReplyBroadcast = true;
        });

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal("123.789", result.Ts);
        var body = SlackTestSupport.BodyJson(Assert.Single(harness.Http.Recorded));
        SlackTestSupport.AssertSlackMethod(harness.Http.LastRecorded!, "chat.postMessage");
        Assert.Equal("123.456", body.GetProperty("thread_ts").GetString());
        Assert.True(body.GetProperty("reply_broadcast").GetBoolean());
    }

    [Fact]
    public async Task UpdateMessage_posts_chat_update()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":true,"channel":"C1","ts":"123.456"}""";
        var activity = SlackTestSupport.Activity<UpdateMessage>(harness, update =>
        {
            update.Channel = "C1";
            update.Ts = "123.456";
            update.Text = "edited";
        });

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal("C1", result.Channel);
        SlackTestSupport.AssertSlackMethod(Assert.Single(harness.Http.Recorded), "chat.update");
        Assert.Equal("123.456", SlackTestSupport.BodyJson(harness.Http.LastRecorded!).GetProperty("ts").GetString());
    }

    [Fact]
    public async Task AddReaction_posts_emoji_name()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":true}""";
        var activity = SlackTestSupport.Activity<AddReaction>(harness, react =>
        {
            react.Channel = "C1";
            react.Ts = "123.456";
            react.EmojiName = ":thumbsup:";
        });

        var transition = await activity.ExecuteForTestsAsync(SlackTestSupport.Context());
        Assert.Equal(ActivityTransitionKind.Complete, transition.Kind);
        SlackTestSupport.AssertSlackMethod(Assert.Single(harness.Http.Recorded), "reactions.add");
        var body = SlackTestSupport.BodyJson(harness.Http.LastRecorded!);
        Assert.Equal("thumbsup", body.GetProperty("name").GetString());
        Assert.Equal("123.456", body.GetProperty("timestamp").GetString());
    }

    [Fact]
    public async Task LookupChannel_returns_channel()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":true,"channel":{"id":"C1","name":"general","is_private":false,"is_archived":false}}""";
        var activity = SlackTestSupport.Activity<LookupChannel>(harness, lookup => lookup.ChannelId = "C1");

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.Equal("C1", result.Id);
        Assert.Equal("general", result.Name);
        SlackTestSupport.AssertSlackMethod(Assert.Single(harness.Http.Recorded), "conversations.info");
    }

    [Fact]
    public async Task LookupUserByEmail_returns_user()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":true,"user":{"id":"U1","name":"ada","real_name":"Ada Lovelace","tz":"Europe/Amsterdam"}}""";
        var activity = SlackTestSupport.Activity<LookupUserByEmail>(harness, lookup => lookup.Email = "ada@example.com");

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.True(result.Found);
        Assert.Equal("U1", result.User?.Id);
        Assert.Equal("Ada Lovelace", result.User?.RealName);
        SlackTestSupport.AssertSlackMethod(Assert.Single(harness.Http.Recorded), "users.lookupByEmail");
        Assert.Equal("ada@example.com", SlackTestSupport.BodyJson(harness.Http.LastRecorded!).GetProperty("email").GetString());
    }

    [Fact]
    public async Task LookupUserByEmail_returns_not_found_instead_of_faulting()
    {
        await using var harness = SlackTestSupport.CreateHarness();
        harness.Http.ResponseContent = """{"ok":false,"error":"users_not_found"}""";
        var activity = SlackTestSupport.Activity<LookupUserByEmail>(harness, lookup => lookup.Email = "missing@example.com");

        var result = SlackTestSupport.Complete(await activity.ExecuteForTestsAsync(SlackTestSupport.Context()));
        Assert.False(result.Found);
        Assert.Null(result.User);
    }
}
