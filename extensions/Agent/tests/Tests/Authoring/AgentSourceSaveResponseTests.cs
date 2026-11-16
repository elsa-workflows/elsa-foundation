using Elsa.Agent.Api.Endpoints;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Persistence.EntityFrameworkCore.Stores;
using Xunit;

namespace Elsa.Agent.Tests.Authoring;

public sealed partial class AgentApiHttpTests
{
    private sealed class RacingWriter(IAgentSourceWriter inner) : IAgentSourceWriter
    {
        public Task<AgentSourceDraft> CreateAsync(string agentId, string draftId, string name, AgentSourceContent content, CancellationToken cancellationToken = default) => inner.CreateAsync(agentId, draftId, name, content, cancellationToken);
        public async Task<AgentSourceDraft> SaveAsync(string agentId, string draftId, long expectedRevision, string name, AgentSourceContent content, CancellationToken cancellationToken = default)
        {
            var saved = await inner.SaveAsync(agentId, draftId, expectedRevision, name, content, cancellationToken);
            await inner.SaveAsync(agentId, draftId, saved.Revision, name, content with { Instructions = "Another author" }, cancellationToken);
            return saved;
        }
        public Task<AgentSourceRevision> CaptureAsync(string agentId, string draftId, long expectedRevision, string operationId, CancellationToken cancellationToken = default) => inner.CaptureAsync(agentId, draftId, expectedRevision, operationId, cancellationToken);
        public Task<AgentSourceDraft> ForkAsync(string agentId, string draftId, string snapshotId, string newAgentId, string newDraftId, string name, CancellationToken cancellationToken = default) => inner.ForkAsync(agentId, draftId, snapshotId, newAgentId, newDraftId, name, cancellationToken);
        public Task DiscardAsync(string agentId, string draftId, long expectedRevision, CancellationToken cancellationToken = default) => inner.DiscardAsync(agentId, draftId, expectedRevision, cancellationToken);
    }

    [Fact]
    public async Task Save_response_is_the_committed_revision_even_when_another_author_commits_before_the_reply()
    {
        await using var db = new AgentSourceTestContext(sourceConnection);
        var source = new EfAgentSources<AgentSourceTestContext>(db, new SourceAccess());
        var content = new AgentSourceContent("Original", new("fixture", "connection", "model"), [], [], System.Text.Json.JsonSerializer.SerializeToElement(new { }), new());
        await source.CreateAsync("writer", "draft", "Writer", content);
        var reply = await new SaveAgentSource(new RacingWriter(source)).HandleAsync(new("writer", "draft", 0, "Writer", content with { Instructions = "My edit" }), default);
        Assert.Equal(1, reply.Revision);
        Assert.Equal("My edit", reply.Content.Instructions);
        var current = await source.FindAsync("writer", "draft");
        Assert.Equal(2, current!.Revision);
        Assert.Equal("Another author", current.Content.Instructions);
    }
}
