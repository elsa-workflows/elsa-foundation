namespace Elsa.Activities.Runtime.Core.Abstractions;

/// <summary>
/// Optional semantic base for code-first activity authors. It intentionally adds no runtime behavior over
/// <see cref="ActivityBase"/>; execution semantics come from the implemented activity transition contract.
/// </summary>
public abstract class CodeActivity : ActivityBase
{
}
