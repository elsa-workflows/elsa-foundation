using Elsa.Activities.Design.Reconciliation.Core.Models;

namespace Elsa.Activities.Design.Reconciliation.Clr.Contracts;

/// <summary>
/// Reflection-only scanner that reads activity-bearing assemblies from a folder and produces one
/// <see cref="ActivityVersionReconciliationModel"/> per discovered activity. Exposed as a contract so
/// a feature can replace the scanning strategy in isolation without re-wiring the reconciliation
/// source that consumes it.
/// </summary>
public interface IClrAssemblyScanner
{
    IReadOnlyList<ActivityVersionReconciliationModel> Scan(string folderPath);
}
