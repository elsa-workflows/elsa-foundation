using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using System.Text;

namespace Elsa.Workflows.Design.Core.Contracts
{
    public interface IArgumentState
    {
        string ArgumentReferenceKey { get; }

        ArgumentValue Value { get; }

        bool AutoEvaluate { get; }

        TypeInformation? EvaluatorType { get; }

        TypeInformation? StorageDriverType { get; }

        bool IsSensitive { get; }
    }
}
