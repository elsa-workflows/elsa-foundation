using Elsa.Agent.Specialists.Core.Models;

namespace Elsa.Agent.Specialists.Core.Contracts;

/// <summary>
/// Per-turn authority supplied by the runtime. A qualified provider calls it immediately before
/// every physical model request, below SDK retry/redirect behavior. A refusal permits no send.
/// </summary>
public interface ISpecialistModelDispatchAuthority
{
    /// <summary>Persists a conservative request/output reservation under the current journal fence.</summary>
    ValueTask BeforeSendAsync(SpecialistModelDispatchObservation observation, CancellationToken cancellationToken);
}
