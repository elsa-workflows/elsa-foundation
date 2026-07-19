using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Applies a conversion plan that was validated and pinned when the executable was published.
/// Implementations must not discover converters or reinterpret source content at runtime.
/// </summary>
public interface IRuntimeValueConversionExecutor
{
    ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan);
}
