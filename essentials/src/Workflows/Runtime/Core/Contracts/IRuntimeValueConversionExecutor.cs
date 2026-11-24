using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Applies a conversion plan that was validated and pinned when the executable was published.
/// Implementations apply only the selected profile and the target type's package-owned JSON contract;
/// they must not discover alternate profiles or reinterpret source content at runtime.
/// </summary>
public interface IRuntimeValueConversionExecutor
{
    ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan);

    /// <summary>Applies the pinned plan and records typed JSON diagnostics through the owning execution's sink.</summary>
    ValueEnvelope Convert(ValueEnvelope source, ValueConversionPlan plan, Action<JsonConversionDiagnostic>? report) => Convert(source, plan);
}
