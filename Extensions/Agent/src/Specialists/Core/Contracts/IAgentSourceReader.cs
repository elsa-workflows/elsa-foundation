using Elsa.Agent.Specialists.Core.Models;

namespace Elsa.Agent.Specialists.Core.Contracts;

/// <summary>Replacement contract: one selected source backend per shell, paired with IAgentSourceWriter. Reads use trusted domain-authorized scope.</summary>
public interface IAgentSourceReader
{
    Task<AgentSourceDraft?> FindAsync(string agentId, string draftId, CancellationToken cancellationToken = default);
    Task<AgentSourceRevision?> FindRevisionAsync(string agentId, string draftId, string snapshotId, CancellationToken cancellationToken = default);
    Task<AgentSourceRevision?> FindSourceRevisionAsync(string agentId, string snapshotId, CancellationToken cancellationToken = default);
    Task<AgentRevisionPage> SourceHistoryAsync(string agentId, int skip = 0, int take = 50, CancellationToken cancellationToken = default);
    Task<AgentSourcePage> ListAsync(string? search, int skip, int take, CancellationToken cancellationToken = default);
    Task<AgentDefinitionPage> ListDefinitionsAsync(string? search, int skip, int take, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentSourceRevision>> HistoryAsync(string agentId, string draftId, int skip = 0, int take = 100, CancellationToken cancellationToken = default);
}

/// <summary>Replacement contract: one selected source backend per shell, paired with IAgentSourceReader. Typed capture is not executable publication.</summary>
public interface IAgentSourceWriter
{
    Task<AgentSourceDraft> CreateAsync(string agentId, string draftId, string name, AgentSourceContent content, CancellationToken cancellationToken = default);
    Task<AgentSourceDraft> ForkAsync(string agentId, string draftId, string snapshotId, string newAgentId, string newDraftId, string name, CancellationToken cancellationToken = default);
    Task<AgentSourceDraft> SaveAsync(string agentId, string draftId, long expectedRevision, string name, AgentSourceContent content, CancellationToken cancellationToken = default);
    Task<AgentSourceRevision> CaptureAsync(string agentId, string draftId, long expectedRevision, string operationId, CancellationToken cancellationToken = default);
    Task DiscardAsync(string agentId, string draftId, long expectedRevision, CancellationToken cancellationToken = default);
}
