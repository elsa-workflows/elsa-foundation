using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Options;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityAvailabilityDiagnosticsProjector
{
    ActivityAvailabilityDiagnostics Project(
        IEnumerable<IActivityDefinition> activities,
        ActivityAvailabilityOptions options,
        ActivityAvailabilitySettings? managementSettings = null);
}
