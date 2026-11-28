using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace Elsa.Workbench.Tests;

public sealed class WorkbenchUserTaskArchiveTests
{
    [Fact]
    public async Task Stock_actor_host_reads_its_inbox_and_reopens_published_task_forms()
    {
        await using var host = await WorkbenchProcess.StartAsync(WorkbenchShell.Development);
        using (var anonymous = await host.Client.GetAsync("/runtime/user-task-forms/definitions"))
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var client = await SignInAsync(host);
        var catalogue = await client.GetFromJsonAsync<JsonNode>("/capabilities");
        var taskCapability = Assert.Single(catalogue!["capabilities"]!.AsArray(),
            item => (string?)item?["id"] == "elsa.api.user-tasks");
        var links = taskCapability!["links"]!.AsArray().Select(link => (string)link!["rel"]!).ToArray();
        Assert.Contains("tasks", links);
        Assert.Contains("publish-form", links);
        Assert.DoesNotContain(links, link => link is "claim" or "release" or "reassign" or "complete");

        var inbox = await client.GetFromJsonAsync<JsonNode>("/runtime/user-tasks?view=mine");
        Assert.Empty(inbox!["items"]!.AsArray());
        using (var completion = await client.PostAsJsonAsync("/runtime/user-tasks/unowned/complete", new { }))
            Assert.Equal(HttpStatusCode.NotFound, completion.StatusCode);

        var form = new
        {
            id = "approval",
            version = "$draft",
            name = "Approval form",
            provider = "elsa.basic-forms/1",
            fields = new[] { new { key = "note", label = "Review note", kind = "text", required = true } }
        };
        using (var saved = await client.PostAsJsonAsync("/runtime/user-task-forms/approval/draft", new { revision = 0, form }))
        {
            saved.EnsureSuccessStatusCode();
            var draft = await saved.Content.ReadFromJsonAsync<JsonNode>();
            Assert.Equal(1, (int)draft!["revision"]!);
        }
        using (var stale = await client.PostAsJsonAsync("/runtime/user-task-forms/approval/publish", new { revision = 0, form }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);

        using (var published = await client.PostAsJsonAsync("/runtime/user-task-forms/approval/publish", new { revision = 1, form }))
        {
            published.EnsureSuccessStatusCode();
            var version = await published.Content.ReadFromJsonAsync<JsonNode>();
            Assert.Equal("1", (string?)version?["form"]?["version"]);
        }

        await using var restarted = await host.CrashAndRestartAsync();
        using var restartedClient = await SignInAsync(restarted);
        var reopened = await restartedClient.GetFromJsonAsync<JsonNode>("/runtime/user-task-forms/approval/versions/1");
        Assert.Equal("Review note", (string?)reopened?["form"]?["fields"]?[0]?["label"]);
    }

    private static async Task<HttpClient> SignInAsync(WorkbenchProcess host)
    {
        var client = new HttpClient(new HttpClientHandler { UseCookies = true }) { BaseAddress = host.Client.BaseAddress };
        try
        {
            using var login = await client.PostAsJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" });
            login.EnsureSuccessStatusCode();
            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
