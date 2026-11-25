using System.Net;
using System.Text.Json.Nodes;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The built <c>Elsa.Essentials.Host</c> as a child process, the way an operator runs it in Development: its committed
/// <c>appsettings.json</c> and <c>shells.json</c>, an empty feed, SQLite in its content root and the seeded admin user (#5713).
/// Production blanks the seeded password and demo keys and refuses to activate without them, so the boot runs in Development,
/// which <see cref="ChildHostProcess"/> sets. One host serves every test here.
/// </summary>
public sealed class EssentialsHostBootTests(EssentialsHostBootTests.Host host) : IClassFixture<EssentialsHostBootTests.Host>
{
    private const string WriteLineType = "Elsa.Activities.Primitives.Activities.WriteLine";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    [Fact]
    public async Task The_essentials_host_boots_in_development_and_reports_live_and_ready()
    {
        var process = host.Process;

        var live = await process.GetAsync("/health/live");
        var ready = await process.GetAsync("/health/ready");

        Assert.True(live.Status == HttpStatusCode.OK, $"/health/live answered {live.Status}: {live.Body}");
        Assert.True(ready.Status == HttpStatusCode.OK, $"/health/ready answered {ready.Status}: {ready.Body}");
    }

    /// <summary>
    /// The curated features serve a workflow end to end: the seeded admin signs in, a one-activity WriteLine workflow is
    /// submitted, published and executed through the host's REST endpoints, and its line reaches the host's console.
    /// </summary>
    [Fact]
    public async Task A_workflow_is_submitted_published_and_executed_on_the_essentials_host()
    {
        var process = host.Process;
        var line = $"essentials boot test {Guid.NewGuid():N}";
        Expect(HttpStatusCode.OK, "sign-in", await process.PostJsonAsync("/_elsa/identity/login", new { username = "admin", password = "Password123!" }));
        var writeLine = await ActivityVersionAsync(process, WriteLineType);

        var submitted = Expect(HttpStatusCode.OK, "submit", await process.PostJsonAsync("/design/workflows/definitions/submit", new
        {
            name = "Essentials boot test",
            description = "A WriteLine workflow the Essentials boot test runs",
            state = new
            {
                variables = Array.Empty<object>(),
                inputs = Array.Empty<object>(),
                outputs = Array.Empty<object>(),
                rootActivity = WriteLine(writeLine, line)
            }
        }));
        var published = Expect(HttpStatusCode.Created, "publish",
            await process.PostJsonAsync($"/publishing/workflows/{submitted["version"]!["id"]!.GetValue<string>()}/publish", new { }));
        Expect(HttpStatusCode.OK, "execute", await process.PostJsonAsync(
            $"/runtime/workflows/executables/{published["artifactId"]!.GetValue<string>()}/execute",
            new { sourceReferenceId = published["sourceReferenceId"]!.GetValue<string>() }));

        await Polling.UntilAsync(() => process.Output.Contains(line, StringComparison.Ordinal), Patience, TimeSpan.FromMilliseconds(100),
            $"The workflow's line '{line}' never reached the host's console.");
    }

    /// <summary>The head version of <paramref name="type"/> in the activity catalog, which the host reconciles as its shell activates.</summary>
    private static async Task<string> ActivityVersionAsync(ChildHostProcess process, string type)
    {
        string? version = null;
        var last = default((HttpStatusCode Status, string Body));
        await Polling.UntilAsync(
            async () =>
            {
                last = await process.GetAsync($"/design/activities/definitions?search={Uri.EscapeDataString(type)}");
                version = last.Status == HttpStatusCode.OK
                    ? JsonNode.Parse(last.Body)?["items"]?.AsArray()
                        .FirstOrDefault(item => item?["definition"]?["activityTypeKey"]?.GetValue<string>() == type)?["definition"]?["headVersionId"]?.GetValue<string>()
                    : null;
                return version is not null;
            },
            Patience,
            TimeSpan.FromMilliseconds(200),
            () => $"{type} is not in the activity catalog; it last answered {last.Status}: {last.Body}{Environment.NewLine}Host output:{Environment.NewLine}{process.Output}");
        return version!;
    }

    private static JsonObject WriteLine(string version, string text) => new()
    {
        ["nodeId"] = "writeLine",
        ["activityVersionId"] = version,
        ["inputs"] = new JsonArray(new JsonObject
        {
            ["referenceKey"] = "text",
            ["value"] = new JsonObject { ["expressionType"] = "Literal", ["value"] = text }
        }),
        ["outputs"] = new JsonArray(),
        ["structure"] = null,
        ["executionPlacement"] = 0
    };

    private JsonNode Expect(HttpStatusCode expected, string step, (HttpStatusCode Status, string Body) answer)
    {
        Assert.True(answer.Status == expected,
            $"The {step} answered {answer.Status}, not {expected}: {answer.Body}{Environment.NewLine}Host output:{Environment.NewLine}{host.Process.Output}");
        return JsonNode.Parse(answer.Body) ?? new JsonObject();
    }

    /// <summary>The Essentials host, started once for the class from its committed Development configuration.</summary>
    public sealed class Host : IAsyncLifetime
    {
        private const string Name = "Elsa.Essentials.Host";
        private ChildHostProcess? _process;

        internal ChildHostProcess Process => _process ?? throw new InvalidOperationException($"{Name} has not started.");

        public async Task InitializeAsync()
        {
            var source = Path.Join(ChildHostProcess.RepoRoot, "apps", Name);
            _process = await ChildHostProcess.StartAsync(
                Name,
                source,
                "elsa-essentials-host-boot-",
                File.ReadAllText(Path.Join(source, "shells.json")),
                [],
                new Dictionary<string, string>());
        }

        public async Task DisposeAsync()
        {
            if (_process is not null)
                await _process.DisposeAsync();
        }
    }
}
