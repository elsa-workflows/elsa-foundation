using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal static class BpmnIngressScope
{
    internal static void Require(IPersistenceAccessContextAccessor access, RuntimeModelExecutionState model)
    {
        access.Current.EnsureScope(model.Scope);
        if (model.IngressOrigin is null) access.Current.EnsureTenantScope(model.TenantId);
        else if (model.IngressOrigin.Scope != model.Scope)
            throw new BpmnModelValidationException("The physically admitted model root changed persistence scope.");
    }
}
