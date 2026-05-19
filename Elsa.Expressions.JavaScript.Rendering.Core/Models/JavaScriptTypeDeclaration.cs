using Elsa.Expressions.JavaScript.Core.Constants;

namespace Elsa.Expressions.JavaScript.Core.Models
{
    /// <summary>
    /// A base type that represents a type.
    /// </summary>
    public sealed class JavaScriptTypeDeclaration
    {
        /// <summary>
        /// The keyword that declares this type. E.g. "<see cref="DeclarationKeywords.Class"/>", "<see cref="DeclarationKeywords.Type"/>", or "<see cref="DeclarationKeywords.Interface"/>".
        /// </summary>
        public string DeclarationKeyword { get; set; } = DeclarationKeywords.Class;

        /// <summary>
        /// The name of the type.
        /// </summary>
        public string Name { get; set; } = default!;

        public string? Alias { get; set; }

        /// <summary>
        /// A list of fields.
        /// </summary>
        public ICollection<JavaScriptPropertyDeclaration> Properties { get; set; } = [];

        /// <summary>
        /// A list of methods.
        /// </summary>
        public ICollection<JavaScriptFunctionDeclaration> Methods { get; set; } = [];
    }
}
