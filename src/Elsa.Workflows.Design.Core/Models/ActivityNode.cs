using Elsa.Activities.Design.Core.Models;
namespace Elsa.Workflows.Design.Core.Models;

public sealed record ActivityNode(
    string NodeId,
    string ActivityDefinitionId,
    int ActivityVersion,
    string ReferenceKey,
    IEnumerable<ArgumentState> Inputs,
    IEnumerable<ArgumentState> Outputs,
    bool IsContainer,
    bool IsStart,
    bool IsTerminal,
    ActivityDisplayInfo? DisplayInfo,
    IEnumerable<ActivityNode> ChildActivities
);
