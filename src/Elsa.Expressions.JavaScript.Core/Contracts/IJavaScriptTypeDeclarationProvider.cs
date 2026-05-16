using Elsa.Expressions.JavaScript.Core.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Core.Contracts
{
    public interface IJavaScriptTypeDeclarationProvider
    {
        ValueTask<IEnumerable<JavaScriptTypeDeclaration>> GetDeclarations(CancellationToken cancellationToken = default);
    }
}
