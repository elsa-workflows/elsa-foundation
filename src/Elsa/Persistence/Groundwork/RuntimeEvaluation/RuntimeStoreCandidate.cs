using Groundwork.Core.Workloads;

namespace Elsa.Persistence.Groundwork.RuntimeEvaluation;

public sealed record RuntimeStoreCandidate(
    string Name,
    WorkloadFamily Family,
    bool RequiresAtomicCommit,
    bool RequiresPostCommitDispatch,
    bool IsOperationalStream,
    bool RequiresLeaseOrMailbox,
    bool AllowsGroundworkDefault);
