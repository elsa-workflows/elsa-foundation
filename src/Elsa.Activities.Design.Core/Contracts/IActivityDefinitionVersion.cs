using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IActivityDefinitionVersion
{
    string Id { get; }

    int Version { get; }

    /// <summary>
    /// Information about the type where the activity lives
    /// </summary>
    TypeInformation TypeInfo { get; }

    /// <summary>
    /// 
    /// </summary>
    IActivityDefinition Definition { get; }

    /// <summary>
    /// 
    /// </summary>
    IEnumerable<InputDefinition> Inputs { get; }

    /// <summary>
    /// 
    /// </summary>
    IEnumerable<OutputDefinition> Outputs { get; }

    /// <summary>
    /// 
    /// </summary>
    IEnumerable<ActivityPortDefinition> Ports { get; }


    /// <summary>
    /// The kind of activity.
    /// </summary>
    ActivityKind Kind { get; }
}
