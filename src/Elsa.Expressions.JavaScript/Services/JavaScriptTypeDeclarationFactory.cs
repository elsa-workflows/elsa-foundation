using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Primitives.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace Elsa.Expressions.JavaScript.Services
{
    /// <inheritdoc />
    internal sealed class JavaScriptTypeDeclarationFactory(IJavaScriptTypeAliasRegistry javaScriptTypeAliasRegistry) : IJavaScriptTypeDeclarationFactory
    {      
        /// <inheritdoc />
        public JavaScriptTypeDeclaration Create(Type type)
        {
            var typeDefinition = new JavaScriptTypeDeclaration
            {
                DeclarationKeyword = DeclarationKeywords.GetDeclarationKeyword(type),
                Name = type.Name,
                Properties = GetPropertyDefinitions(type).DistinctBy(x => x.Name).ToList(),
                Methods = [.. GetMethodDefinitions(type).DistinctBy(x => x.Name)]
            };

            return typeDefinition;
        }

        private IEnumerable<JavaScriptFunctionDeclaration> GetMethodDefinitions(Type type)
        {
            if (type.IsEnum)
                yield break;

            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(x => !x.IsSpecialName)
                .Where(x => x.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
                .ToList();

            foreach (var method in methods)
            {
                yield return new JavaScriptFunctionDeclaration
                {
                    Name = method.Name,
                    Parameters = [.. GetMethodParameters(method)],
                    ReturnType = javaScriptTypeAliasRegistry.GetAliasOrDefault(method.ReturnType)
                };
            }
        }

        private IEnumerable<JavaScriptParameterDeclaration> GetMethodParameters(MethodInfo method)
        {
            var parameters = method.GetParameters();

            foreach (var parameter in parameters)
            {
                yield return new JavaScriptParameterDeclaration
                {
                    Name = parameter.Name!,
                    Type = javaScriptTypeAliasRegistry.GetAliasOrDefault(parameter.ParameterType),
                    IsOptional = parameter.IsOptional
                };
            }
        }

        private IEnumerable<JavaScriptPropertyDeclaration> GetPropertyDefinitions([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type)
        {
            // If the type is an enum, enumerate its members.
            if (type.IsEnum)
            {
                foreach (var name in Enum.GetNames(type))
                {
                    yield return JavaScriptPropertyDeclaration.TextProperty(name);
                }

                yield break;
            }

            var properties = type.GetProperties();

            foreach (var property in properties)
            {
                yield return new JavaScriptPropertyDeclaration
                {
                    Name = property.Name,
                    Type = javaScriptTypeAliasRegistry.GetAliasOrDefault(property.PropertyType),
                    IsOptional = property.PropertyType.IsNullableType()
                };
            }
        }

      
    }
}
