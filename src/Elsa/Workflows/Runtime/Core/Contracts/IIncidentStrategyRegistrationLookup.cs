using Elsa.Workflows.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

internal interface IIncidentStrategyRegistrationLookup
{
    bool TryGetRegistration(IncidentStrategyReference reference, out IncidentStrategyRegistration registration);
}
