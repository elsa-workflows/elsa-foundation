using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Actual capture-chain support for a retained policy, independent from new-admission settings.</summary>
public interface IRuntimeHistoryCaptureSupport
{
    bool CanCapture(RuntimeHistoryCapturePolicy policy);
}
