using Esprima.Ast;
using Jint;

namespace Elsa.Expressions.JavaScript.Jint3.Contracts
{
    public interface IPreparedScriptFactory
    {
        Prepared<Script> Create(string expression);
    }
}
