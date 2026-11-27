using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>Contributes authoring findings for portable source without evaluating the expression.</summary>
public interface IPortableExpressionSourceValidator
{
    string Language { get; }
    IEnumerable<string> Validate(PortableExpressionSource source);
}
