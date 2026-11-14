using System.Security.Claims;
using System.Text.Json;
using Acme.Home.Weaver;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Studio.Preferences.Core;
using Elsa.Studio.Preferences.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Acme.Weaver.UpgradeResource.Tests;

// These tests consume the independently packed fixture, never product internals.
// Synthetic invocation values exercise the observation boundary only; they are
// not old-writer captures or native upgrade qualification.
public sealed class UpgradeExecutionObservationTests
{
    [Theory]
    [InlineData(UpgradeContract.Read, false)]
    [InlineData(UpgradeContract.Read, true)]
    [InlineData(UpgradeContract.Write, false)]
    [InlineData(UpgradeContract.Write, true)]
    [InlineData(UpgradeContract.Admin, false)]
    [InlineData(UpgradeContract.Admin, true)]
    public async Task Missing_or_unauthenticated_current_owner_poisons_observation_without_fabricating_identity(string toolName, bool missingContext)
    {
        using var files = new JournalFiles();
        var accessor = new TestHttp { HttpContext = missingContext ? null : new DefaultHttpContext() };
        var owner = new UpgradeOwner(accessor, null!, null!, null!, new("upgrade-host", true));
        var journal = new WidgetExecutionJournal(new(files.Path));
        Assert.Empty(journal.Read(files.Key));
        var failure = await Assert.ThrowsAsync<WidgetFixtureException>(() => Tool(owner, journal, toolName).ExecuteAsync(Invocation(toolName)));
        Assert.IsType<UpgradeRefusal>(failure.InnerException);
        Assert.True(failure.ObservationInvalidationRetained);
        Assert.Equal(WidgetExecutionJournal.Header + "\n", File.ReadAllText(files.Path));
        Assert.True(File.Exists(files.Path + ".failed"));
        Assert.Throws<WidgetFixtureException>(() => journal.Read(files.Key));
        // A newly constructed observer must also refuse zero after the failed entry.
        Assert.Throws<WidgetFixtureException>(() => new WidgetExecutionJournal(new(files.Path)).Read(files.Key));
    }

    [Theory]
    [InlineData(UpgradeContract.Read)]
    [InlineData(UpgradeContract.Write)]
    [InlineData(UpgradeContract.Admin)]
    public async Task Cancellation_before_owner_resolution_poisons_observation_instead_of_leaving_valid_zero(string toolName)
    {
        using var files = new JournalFiles();
        var owner = new UpgradeOwner(new TestHttp(), null!, null!, null!, new("upgrade-host", true));
        var journal = new WidgetExecutionJournal(new(files.Path));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var failure = await Assert.ThrowsAsync<WidgetFixtureException>(() => Tool(owner, journal, toolName).ExecuteAsync(Invocation(toolName), cancellation.Token));
        Assert.IsType<OperationCanceledException>(failure.InnerException);
        Assert.True(failure.ObservationInvalidationRetained);
        Assert.Equal(WidgetExecutionJournal.Header + "\n", File.ReadAllText(files.Path));
        Assert.Throws<WidgetFixtureException>(() => new WidgetExecutionJournal(new(files.Path)).Read(files.Key));
    }

    [Theory]
    [InlineData(UpgradeContract.Read)]
    [InlineData(UpgradeContract.Write)]
    [InlineData(UpgradeContract.Admin)]
    public async Task An_already_invalidated_observation_exception_keeps_its_exact_cause_and_retention_state(string toolName)
    {
        using var files = new JournalFiles();
        var journal = new WidgetExecutionJournal(new(files.Path));
        var original = journal.Invalidate(new IOException("original observation failure"));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "actor")], "fixture-test")) };
        var owner = new UpgradeOwner(new TestHttp { HttpContext = context }, new ThrowingAuth(original), null!, null!, new("upgrade-host", true));
        var failure = await Assert.ThrowsAsync<WidgetFixtureException>(() => Tool(owner, journal, toolName).ExecuteAsync(Invocation(toolName)));
        Assert.Same(original, failure);
        Assert.True(failure.ObservationInvalidationRetained);
        Assert.Equal(WidgetExecutionJournal.Header + "\n", File.ReadAllText(files.Path));
        Assert.Throws<WidgetFixtureException>(() => new WidgetExecutionJournal(new(files.Path)).Read(files.Key));
    }

    private static IWeaverTool Tool(UpgradeOwner owner, WidgetExecutionJournal journal, string name) => name switch {
        UpgradeContract.Read => new UpgradeReadTool(owner, journal), UpgradeContract.Write => new UpgradeWriteTool(owner, journal),
        UpgradeContract.Admin => new UpgradeAdminTool(owner, journal), _ => throw new ArgumentException(nameof(name)) };

    private static WeaverPreparedToolInvocation Invocation(string name) => new("unit-invocation", "unit-session", "unit-turn", "unit-call", name, "1",
        UpgradeContract.Module, "requested-actor", "requested-tenant", new(UpgradeContract.Module, UpgradeContract.Resource, UpgradeContract.Widget, "revision"),
        JsonSerializer.SerializeToElement(new { visible = false }), "unit-observation-only", WeaverRisk.Admin,
        new(UpgradeContract.Result, "1", JsonSerializer.SerializeToElement(new { })), WeaverExecutionLocation.Server, true, null, DateTimeOffset.UtcNow.AddMinutes(5));

    private sealed class TestHttp : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } }
    private sealed class ThrowingAuth(Exception failure) : IAuthSessionService {
        public ValueTask<AuthSession> GetAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) => ValueTask.FromException<AuthSession>(failure);
    }
    private sealed class JournalFiles : IDisposable
    {
        private readonly string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "weaver-upgrade-entry-" + Guid.NewGuid().ToString("N"));
        internal string Path => System.IO.Path.Combine(root, "entries.jsonl");
        internal StudioPreferenceKey Key { get; } = new("actor", "tenant", "upgrade-host", StudioPreferenceNamespaces.HomeLayout);
        internal JournalFiles() {
            Directory.CreateDirectory(root);
            File.SetUnixFileMode(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            File.WriteAllText(Path, WidgetExecutionJournal.Header + "\n");
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        public void Dispose() => Directory.Delete(root, true);
    }
}
