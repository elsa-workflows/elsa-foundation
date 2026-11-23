using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Tests;

public sealed class WeaverSessionIdentityTests
{
    [Fact]
    public async Task A_case_variant_session_cannot_read_its_session_messages_or_context()
    {
        using var root = WeaverTestServices.Create();
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var session = await CreateSessionAsync(sessions);
        var message = await sessions.AddMessageAsync(session.Id, new(WeaverRole.Assistant, "Retained answer", WeaverMessageStatus.Completed, null, [], []));
        await sessions.AddContextAsync(session.Id, [Context("ContextA")]);
        var alias = SwapCase(session.Id);
        Assert.NotEqual(session.Id, alias);

        Assert.Null(await sessions.FindAsync(alias));
        Assert.Empty(await sessions.ListMessagesAsync(alias));
        Assert.Null(await sessions.FindLatestMessageAsync(alias));
        Assert.Null(await sessions.FindMessageAsync(alias, message.Id));
        Assert.Empty(await sessions.ListContextAsync(alias));
        Assert.Equal(session, await sessions.FindAsync(session.Id));
        Assert.Equal(message, Assert.Single(await sessions.ListMessagesAsync(session.Id)));
        Assert.Equal("ContextA", Assert.Single(await sessions.ListContextAsync(session.Id)).Id);
    }

    [Fact]
    public async Task A_case_variant_session_cannot_append_input_context_or_change_message_status()
    {
        using var root = WeaverTestServices.Create();
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var session = await CreateSessionAsync(sessions);
        var message = await sessions.AddMessageAsync(session.Id, new(WeaverRole.Assistant, "Retained answer", WeaverMessageStatus.Completed, null, [], []));
        await sessions.AddContextAsync(session.Id, [Context("original")]);
        var alias = SwapCase(session.Id);
        Assert.NotEqual(session.Id, alias);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sessions.AddMessageAsync(alias,
            new(WeaverRole.User, "Wrong-case input", WeaverMessageStatus.Pending, null, [], [])));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sessions.AddContextAsync(alias, [Context("unexpected")]));
        await sessions.SetMessageStatusAsync(alias, message.Id, WeaverMessageStatus.Cancelled);

        Assert.Equal(message, Assert.Single(await sessions.ListMessagesAsync(session.Id)));
        Assert.Equal("original", Assert.Single(await sessions.ListContextAsync(session.Id)).Id);
    }

    [Fact]
    public async Task Message_lookup_requires_its_exact_opaque_identity()
    {
        using var root = WeaverTestServices.Create();
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var session = await CreateSessionAsync(sessions);
        var message = await sessions.AddMessageAsync(session.Id, new(WeaverRole.Assistant, "Retained answer", WeaverMessageStatus.Completed, null, [], []));
        var alias = SwapCase(message.Id);
        Assert.NotEqual(message.Id, alias);

        Assert.Null(await sessions.FindMessageAsync(session.Id, alias));
        await sessions.SetMessageStatusAsync(session.Id, alias, WeaverMessageStatus.Cancelled);
        Assert.Equal(message, await sessions.FindMessageAsync(session.Id, message.Id));
    }

    [Fact]
    public async Task Context_deduplication_keeps_distinct_case_sensitive_identities()
    {
        using var root = WeaverTestServices.Create();
        using var scope = root.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<IWeaverSessionService>();
        var session = await CreateSessionAsync(sessions);
        await sessions.AddContextAsync(session.Id, [Context("ContextA")]);

        await sessions.AddContextAsync(session.Id, [Context("contexta"), Context("ContextA")]);

        var retained = await sessions.ListContextAsync(session.Id);
        Assert.Equal(2, retained.Count);
        Assert.Contains(retained, x => x.Id == "ContextA");
        Assert.Contains(retained, x => x.Id == "contexta");
    }

    [Fact]
    public async Task Audit_filtering_keeps_distinct_opaque_session_identities_separate()
    {
        using var root = WeaverTestServices.Create();
        using var scope = root.CreateScope();
        var sink = scope.ServiceProvider.GetRequiredService<IWeaverAuditSink>();
        var reader = scope.ServiceProvider.GetRequiredService<IWeaverAuditReader>();
        var now = DateTimeOffset.UtcNow;
        var first = new WeaverAuditEvent("audit-A", WeaverAuditEventKind.SessionCreated, "SessionA", "actor-1", "First session", now, new Dictionary<string, string>());
        var second = first with { Id = "audit-a", SessionId = "sessiona", Summary = "Second session" };
        await sink.EmitAsync(first);
        await sink.EmitAsync(second);

        Assert.Equal(first, Assert.Single(await reader.ListAsync("SessionA")));
        Assert.Equal(second, Assert.Single(await reader.ListAsync("sessiona")));
        Assert.Empty(await reader.ListAsync("SESSIONA"));
    }

    private static Task<WeaverSession> CreateSessionAsync(IWeaverSessionService sessions)
        => sessions.CreateAsync(new("tenant-1", "actor-1", "conversation-1", "test-provider", "explain", "Identity test", WeaverPolicy.Default, new Dictionary<string, string>()));

    private static WeaverContextAttachment Context(string id)
        => new(id, "test", "resource-1", "Resource", "test.resource", WeaverContextSensitivity.Internal, "selection", "Owned test context", null, new Dictionary<string, string>());

    private static string SwapCase(string value)
        => new(value.Select(x => char.IsUpper(x) ? char.ToLowerInvariant(x) : char.ToUpperInvariant(x)).ToArray());
}
