using System.Text.Json;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Persistence.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Agent.Specialists.Persistence.EntityFrameworkCore.Stores;

/// <summary>Typed Agent adapter over the same revision engine used by Workflow authoring.</summary>
public sealed class EfAgentSources<TContext>(TContext db, IPersistenceAccessContextAccessor access) : IAgentSourceReader, IAgentSourceWriter where TContext : DbContext
{
    public const string Profile = "elsa.agent-source/1";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private EfSourceRevisions Store()
    {
        if (access.Current.AcrossScopes) throw new SourceRevisionException("scope-required");
        return new(db, new("authoring", access.Current.Scope?.Value, "agent", "agent-design"), singleCopyPerSource: true);
    }
    private static AgentSourceContent Read(string profile, string content)
    {
        if (profile != Profile) throw new SourceRevisionException("profile-unsupported");
        return JsonSerializer.Deserialize<AgentSourceContent>(content, Json) ?? throw new SourceRevisionException("content-invalid");
    }
    private static AgentSourceDraft View(SourceWorkingCopy row) => new(row.SourceId, row.CopyId, row.Name, row.Revision, Read(row.Profile, row.Content));
    private static AgentSourceRevision View(SourceSnapshot row) => new(row.Key, row.OperationId, row.Revision, row.Fingerprint, Read(row.Profile, row.Content));
    private static string Write(AgentSourceContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Instructions);
        ArgumentNullException.ThrowIfNull(content.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Model.ProviderKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Model.ConnectionName);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Model.ModelId);
        if (content.Limits is null || content.Limits.MaximumModelCalls < 1 || content.Limits.TimeoutMilliseconds < 1)
            throw new ArgumentException("Agent request limits must be positive.");
        foreach (var members in new[] { content.Inputs, content.Results })
        {
            ArgumentNullException.ThrowIfNull(members);
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var member in members)
            {
                if (member is null || string.IsNullOrWhiteSpace(member.Key) || member.Key.StartsWith('$') || !keys.Add(member.Key) ||
                    string.IsNullOrWhiteSpace(member.Name) || member.Type is null || string.IsNullOrWhiteSpace(member.Type.Alias) || !Enum.IsDefined(member.Type.CollectionKind))
                    throw new ArgumentException("Agent contract members must have unique stable keys and declared types.");
            }
        }
        return JsonSerializer.Serialize(content, Json);
    }
    public async Task<AgentSourceDraft?> FindAsync(string agentId, string draftId, CancellationToken cancellationToken = default)
    {
        var row = await Store().FindAsync(agentId, draftId, cancellationToken); return row is null ? null : View(row);
    }
    public async Task<AgentSourceRevision?> FindRevisionAsync(string agentId, string draftId, string snapshotId, CancellationToken cancellationToken = default)
    {
        var row = await Store().FindSnapshotAsync(agentId, draftId, snapshotId, cancellationToken); return row is null ? null : View(row);
    }
    public async Task<AgentSourcePage> ListAsync(string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        var page = await Store().ListAsync(search, skip, take, cancellationToken); return new(page.Total, page.Items.Select(View).ToArray());
    }
    public async Task<IReadOnlyList<AgentSourceRevision>> HistoryAsync(string agentId, string draftId, int skip = 0, int take = 100, CancellationToken cancellationToken = default) =>
        (await Store().HistoryAsync(agentId, draftId, skip, take, cancellationToken)).Select(View).ToArray();
    public async Task<AgentSourceDraft> CreateAsync(string agentId, string draftId, string name, AgentSourceContent content, CancellationToken cancellationToken = default) =>
        View(await Store().CreateAsync(agentId, draftId, name, Profile, Write(content), cancellationToken));
    public async Task<AgentSourceDraft> ForkAsync(string agentId, string draftId, string snapshotId, string newAgentId, string newDraftId, string name, CancellationToken cancellationToken = default) =>
        View(await Store().ForkAsync(agentId, draftId, snapshotId, newAgentId, newDraftId, name, cancellationToken));
    public async Task<AgentSourceDraft> SaveAsync(string agentId, string draftId, long expectedRevision, string name, AgentSourceContent content, CancellationToken cancellationToken = default) =>
        View(await Store().SaveAsync(agentId, draftId, expectedRevision, name, Profile, Write(content), cancellationToken));
    public async Task<AgentSourceRevision> CaptureAsync(string agentId, string draftId, long expectedRevision, string operationId, CancellationToken cancellationToken = default) =>
        View(await Store().CaptureAsync(agentId, draftId, expectedRevision, operationId, cancellationToken));
    public Task DiscardAsync(string agentId, string draftId, long expectedRevision, CancellationToken cancellationToken = default) =>
        Store().DiscardAsync(agentId, draftId, expectedRevision, cancellationToken);
}
