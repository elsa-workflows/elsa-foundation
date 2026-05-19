using Jint;

namespace Elsa.Expressions.JavaScript.Jint.Contracts
{
    public interface IJintValueNormalizer
    {
        object? Normalize(Engine engine, object? value);
    }
}
