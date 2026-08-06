using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeCheckpointRecoveryAuthorityAccessor
{
    RuntimeCheckpointRecoveryAuthority? Current { get; }

    IDisposable Push(RuntimeCheckpointRecoveryAuthority authority);
}
