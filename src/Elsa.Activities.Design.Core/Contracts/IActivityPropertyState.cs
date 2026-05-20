
using System.Text;

namespace Elsa.Activities.Design.Core.Contracts
{
    public interface IActivityPropertyState
    {
        string PropertyDefinitionId { get; }

        string ExpressionType { get; }

        string Value { get; }

        Encoding Encoding { get; }

        bool AutoEvaluate { get; }

        string? EvaluatorType { get; }

        string? StorageDriverType { get; }

        bool IsSensitive { get; set; }
    }
}
