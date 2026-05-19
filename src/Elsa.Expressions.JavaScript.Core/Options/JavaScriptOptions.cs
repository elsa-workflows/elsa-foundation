using Elsa.Expressions.JavaScript.Core.Models;

namespace Elsa.Expressions.JavaScript.Core.Options
{
    public sealed class JavaScriptOptions
    {
        public ICollection<JavaScriptTypeDescriptor> TypeDescriptors { get; } = [];
    }
}
