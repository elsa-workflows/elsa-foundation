using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

public interface IRuntimeInputBindingResolver
{
    RuntimeResolvedInput Resolve(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context);
}
