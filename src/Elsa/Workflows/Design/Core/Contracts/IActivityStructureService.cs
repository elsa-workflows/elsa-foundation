using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

public interface IActivityStructureService
{
    IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity);

    ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections);

    ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity);

    /// <summary>
    /// Projects the container-scoped variables declared by <paramref name="activity"/>. Returns an
    /// empty collection when the node has no structure handler or declares no container variables.
    /// </summary>
    IReadOnlyCollection<VariableDefinition> ProjectScopedVariables(ActivityNode activity);
}
