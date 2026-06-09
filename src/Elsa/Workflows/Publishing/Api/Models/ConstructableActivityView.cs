namespace Elsa.Workflows.Publishing.Api.Models;

/// <summary>
/// A catalog row as seen from the publishing side: enough to identify it and to show which descriptor
/// kind it carries. The <see cref="DescriptorType"/> is the whole point of the listing — it makes the
/// seam visible: CLR-discovered rows carry <c>Elsa.Primitives.Models.TypeInformation</c>; a
/// workflow-as-activity row carries <c>Elsa.Workflows.Primitives.Models.WorkflowIdentity</c>.
/// </summary>
public sealed record ConstructableActivityView(
    string ActivityVersionId,
    string DefinitionId,
    string Version,
    string DescriptorType
);
