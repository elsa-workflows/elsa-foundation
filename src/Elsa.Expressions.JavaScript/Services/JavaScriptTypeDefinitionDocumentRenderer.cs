using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using System.Text;

namespace Elsa.Expressions.JavaScript.Services
{
    /// <inheritdoc />
    internal sealed class JavaScriptTypeDefinitionDocumentRenderer
        
        : IJavaScriptDeclarationsDocumentRenderer
    {
        /// <inheritdoc />
        public string Render(JavaScriptDeclarationsDocument document)
        {
            var stringBuilder = new StringBuilder();

            foreach (var functionDeclaration in document.Functions)
                Render(functionDeclaration, stringBuilder);

            foreach (var typeDeclaration in document.Types)
                Render(typeDeclaration, stringBuilder);

            foreach (var variableDeclaration in document.Variables)
                Render(variableDeclaration, stringBuilder);

            return stringBuilder.ToString();
        }

        private static void Render(JavaScriptFunctionDeclaration functionDefinition, StringBuilder output)
        {
            var returnType = functionDefinition.ReturnType != null ? $": {functionDefinition.ReturnType}" : "";
            output.AppendLine($"declare function {functionDefinition.Name}({RenderParameters(functionDefinition.Parameters)}){returnType};");
        }

        private static void RenderMethod(JavaScriptFunctionDeclaration functionDefinition, StringBuilder output)
        {
            var returnType = functionDefinition.ReturnType != null ? $" => {functionDefinition.ReturnType}" : "";
            output.AppendLine($"{functionDefinition.Name}: ({RenderParameters(functionDefinition.Parameters)}){returnType};");
        }

        private static void Render(JavaScriptTypeDeclaration typeDefinition, StringBuilder output)
        {
            output.AppendLine($"declare {typeDefinition.DeclarationKeyword} {typeDefinition.Name} {{");

            if (typeDefinition.DeclarationKeyword == "enum")
            {
                foreach (var property in typeDefinition.Properties)
                    RenderEnumMember(property, output);
            }
            else
            {
                foreach (var property in typeDefinition.Properties)
                    Render(property, output);
            }

            foreach (var method in typeDefinition.Methods)
                RenderMethod(method, output);

            output.AppendLine("}");
        }

        private static void Render(JavaScriptPropertyDeclaration property, StringBuilder output) 
            => output.AppendLine($"{property.Name}{(property.IsOptional ? "?" : "")}: {property.Type};");
        private static void RenderEnumMember(JavaScriptPropertyDeclaration property, StringBuilder output) 
            => output.AppendLine($"{property.Name} = \"{property.Name}\",");
        private static void Render(JavaScriptVariableDeclaration variable, StringBuilder output) 
            => output.AppendLine($"declare var {variable.Name}: {variable.Type};");
        private static string RenderParameter(JavaScriptParameterDeclaration parameter) 
            => $"{parameter.Name}{(parameter.IsOptional ? "?" : "")}: {parameter.Type}";
        private static string RenderParameters(IEnumerable<JavaScriptParameterDeclaration> parameters) 
            => string.Join(", ", parameters.Select(RenderParameter));
    }
    
}
