using Elsa.Expressions.Core.Contracts;
using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Core.Models
{
    public sealed class JavaScriptDeclarationsDocument
    {
        /// <summary>
        /// A collection of function definitions.
        /// </summary>
        public ICollection<JavaScriptFunctionDeclaration> Functions { get; set; } = [];

        /// <summary>
        /// A collection of type definitions.
        /// </summary>
        public ICollection<JavaScriptTypeDeclaration> Types { get; set; } = [];

        /// <summary>
        /// A collection of global variables.
        /// </summary>
        public ICollection<JavaScriptVariableDeclaration> Variables { get; set; } = [];
    }
}
