using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// Optional, read-only routing qualification on the actual selected portable evaluator object.
/// A separate service cannot qualify another evaluator. This query never evaluates source or performs I/O.
/// </summary>
public interface IPortableExpressionCapabilities
{
    bool CanEvaluate(ExpressionDefinition definition);
}
