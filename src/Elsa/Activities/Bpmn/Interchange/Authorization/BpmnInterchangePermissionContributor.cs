using Elsa.Activities.Bpmn.Interchange.Endpoints;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Activities.Bpmn.Interchange.Authorization;

public sealed class BpmnInterchangePermissionContributor : IPermissionContributor
{
    public string OwnerId => "Elsa.Activities.Bpmn.Interchange";

    public IEnumerable<Permission> Contribute() =>
    [
        new(BpmnInterchangePermissions.Read, "Read BPMN interchange", "BPMN interchange", "Analyze and export BPMN interchange documents."),
        new(BpmnInterchangePermissions.Manage, "Manage BPMN interchange", "BPMN interchange", "Import BPMN interchange documents.")
    ];
}
