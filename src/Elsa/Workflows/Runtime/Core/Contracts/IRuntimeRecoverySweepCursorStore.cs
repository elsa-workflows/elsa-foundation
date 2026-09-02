using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Retains one bounded recovery page continuation per resumption scope and scanner.</summary>
public interface IRuntimeRecoverySweepCursorStore
{
    RuntimeRecoverySweepCursor? Get(string scope, string scanner);

    void Set(string scope, string scanner, RuntimeRecoverySweepCursor cursor);

    void Clear(string scope, string scanner);
}
