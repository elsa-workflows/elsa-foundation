using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

public interface IActivityStructureService
{
    IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity);

    ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections);

    ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity);
}
