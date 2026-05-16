using Elsa.Expressions.JavaScript.Core.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Core.Models
{
    public sealed record JavaScriptObject(string Name, object Value)
        : IJavaScriptObject;
}
