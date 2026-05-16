using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Http.JavaScript
{
    public sealed class JavaScriptHttpFeatureOptions
    {
        public IEnumerable<JavaScriptVariableDeclaration> VariableDeclarations { get; set; } = [];

        public IEnumerable<JavaScriptTypeDescriptor> TypeDescriptors { get; set; } = [];

        public IEnumerable<JavaScriptTypeDeclaration> TypeDeclarations { get; set; } = [];
    }
}
