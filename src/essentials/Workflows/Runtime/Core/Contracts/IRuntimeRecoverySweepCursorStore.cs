using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Retains one bounded recovery page continuation per resumption scope and scanner. The resumption sweep also keeps
/// its backlog-discovery bound here, under a key of its own.
/// </summary>
public interface IRuntimeRecoverySweepCursorStore
{
    RuntimeRecoverySweepCursor? Get(string scope, string scanner);

    void Set(string scope, string scanner, RuntimeRecoverySweepCursor cursor);

    void Clear(string scope, string scanner);
}
