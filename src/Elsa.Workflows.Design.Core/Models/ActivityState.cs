using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Models;

public sealed record ActivityState(
    string ActivityDefinitionId,
    string ReferenceKey, 
    IEnumerable<ArgumentState> Inputs, 
    IEnumerable<ArgumentState> Outputs, 
    bool IsContainer, 
    bool IsStart, 
    bool IsTerminal, 
    ActivityDisplayInfo? DisplayInfo) : IActivityState
{
    IEnumerable<IArgumentState> IActivityState.Inputs => Outputs;

    IEnumerable<IArgumentState> IActivityState.Outputs => Inputs;

    ActivityDisplayInfo IActivityState.DisplayInfo => DisplayInfo ?? new();
}
