namespace Elsa.Expressions.JavaScript.Core.Models
{
    /// <summary>
    /// A method parameter definition.
    /// </summary>
    public sealed class JavaScriptParameterDeclaration
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public JavaScriptParameterDeclaration()
        {
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        public JavaScriptParameterDeclaration(string name, string? type, bool isOptional = false)
        {
            Name = name;
            Type = type;
            IsOptional = isOptional;
        }

        /// <summary>
        /// The name of the parameter.
        /// </summary>
        public string Name { get; set; } = default!;

        /// <summary>
        /// The type name of the parameter.
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// A value indicating whether the parameter is optional or not.
        /// </summary>
        public bool IsOptional { get; set; }
    }
}
