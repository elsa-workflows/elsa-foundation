using Acornima.Ast;
using Jint;

namespace Elsa.Expressions.JavaScript.Jint.Contracts;

public interface IPreparedScriptFactory
{
    Prepared<Script> Create(string expression);
}