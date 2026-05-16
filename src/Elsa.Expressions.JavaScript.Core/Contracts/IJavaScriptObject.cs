using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptObject
    {
        string Name { get; }

        object? Value { get; }
    }
}
