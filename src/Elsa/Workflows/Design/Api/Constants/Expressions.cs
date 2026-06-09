using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using System.Linq.Expressions;

namespace Elsa.Workflows.Design.Api.Constants;

internal static class Expressions
{
    public static readonly Expression<Func<WorkflowDefinition, WorkflowDefinitionView>> DefinitionSelector = (e) => new WorkflowDefinitionView(e.Id, e.Name, e.Description, e.CreatedAt, e.LastModifiedAt);

    public static readonly Expression<Func<WorkflowDefinitionVersion, WorkflowDefinitionVersionInfo>> VersionSelector = (e) => new(e.Id, e.Version, e.CreatedAt);
}
