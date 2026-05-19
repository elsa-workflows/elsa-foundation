namespace Elsa.Expressions.JavaScript.Core.Models
{
    public sealed class JavaScriptDeclarationsDocument
    {
        /// <summary>
        /// A collection of function definitions.
        /// </summary>
        public IEnumerable<JavaScriptFunctionDeclaration> Functions { get; set; } = [];

        /// <summary>
        /// A collection of type definitions.
        /// </summary>
        public IEnumerable<JavaScriptTypeDeclaration> Types { get; set; } = [];

        /// <summary>
        /// A collection of global variables.
        /// </summary>
        public IEnumerable<JavaScriptVariableDeclaration> Variables { get; set; } = [];
    }
}
