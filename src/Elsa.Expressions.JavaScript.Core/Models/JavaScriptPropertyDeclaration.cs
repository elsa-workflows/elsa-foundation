using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Expressions.JavaScript.Core.Models
{
    /// <summary>
    /// A property definition.
    /// </summary>
    public sealed class JavaScriptPropertyDeclaration
    {
        /// <summary>
        /// The name of the property.
        /// </summary>
        public string Name { get; set; } = default!;

        /// <summary>
        /// The type name of the property.
        /// </summary>
        public string Type { get; set; } = default!;

        /// <summary>
        /// A value indicating whether the property is optional or not.
        /// </summary>
        public bool IsOptional { get; set; }

        public static JavaScriptPropertyDeclaration TextProperty(string name, bool isOptional = false)
        {
            return new()
            {
                Name = name,
                IsOptional = isOptional,
                Type = "string"
            };
        }
    }
}
