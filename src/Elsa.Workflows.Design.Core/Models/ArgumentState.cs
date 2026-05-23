using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Models;

public sealed record ArgumentState(
    string ArgumentReferenceKey,
    ArgumentValue Value,
    bool AutoEvaluate,
    TypeInformation? EvaluatorType,
    TypeInformation? StorageDriverType,
    bool IsSensitive)

    : IArgumentState
{
   
}