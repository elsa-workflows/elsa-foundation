using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// Activity-owned handler that lets generic design and publishing code project and compile
/// composite children without interpreting activity-specific structure payloads.
/// </summary>
public interface IActivityStructureHandler
{
    string Kind { get; }

    string SchemaVersion { get; }

    IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity);

    ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections);

    ActivityNodeStructure CompileExecutableStructure(ActivityNode activity);
}
