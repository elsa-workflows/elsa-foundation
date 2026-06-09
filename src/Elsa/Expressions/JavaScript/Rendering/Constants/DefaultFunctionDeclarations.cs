using Elsa.Expressions.JavaScript.Primitives.Constants;
using Elsa.Expressions.JavaScript.Rendering.Core.Models;

namespace Elsa.Expressions.JavaScript.Rendering.Constants;

internal static class DefaultFunctionDeclarations
{
    private const string valueParameterName = "value";

    internal static IEnumerable<JavaScriptFunctionDeclaration> Get() => GetFunctionDeclarations();

    private static List<JavaScriptFunctionDeclaration> GetFunctionDeclarations()
    {
        var result = new List<JavaScriptFunctionDeclaration>
        {
            Build(
                FunctionNames.GetConfiguration,
                returnType: WellKnownTypeNames.String,
                parameter: new JavaScriptParameterDeclaration("name", WellKnownTypeNames.String)
            ),

            Build(FunctionNames.NewShortGuid, WellKnownTypeNames.String),
            Build(FunctionNames.NewGuid, WellKnownTypeNames.String),
            Build(FunctionNames.NewGuidString, WellKnownTypeNames.String),

            Build(
                FunctionNames.IsNullOrWhiteSpace,
                WellKnownTypeNames.Bool,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
            ),
            Build(
                FunctionNames.IsNullOrEmpty,
                WellKnownTypeNames.Bool,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
            ),
            Build(
                FunctionNames.ParseGuid,
                WellKnownTypeNames.Guid,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
            ),

            Build(
                FunctionNames.ToJson,
                WellKnownTypeNames.String,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.Any)
            ),
            Build(
                FunctionNames.BytesToString,
                WellKnownTypeNames.String,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.ByteArray)
            ),
            Build(
                FunctionNames.BytesFromString,
                WellKnownTypeNames.ByteArray,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
            ),
            Build(
                FunctionNames.BytesToBase64,
                WellKnownTypeNames.String,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.ByteArray)
            ),
            Build(
                FunctionNames.BytesFromBase64,
                WellKnownTypeNames.ByteArray,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
            ),

            Build(
                FunctionNames.StringFromBase64,
                WellKnownTypeNames.String,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
            ),
            Build(
                FunctionNames.StringToBase64,
                WellKnownTypeNames.String,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
            ),
            Build(
                FunctionNames.StreamToBytes,
                WellKnownTypeNames.ByteArray,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.Stream)
            ),
            Build(
                FunctionNames.StreamToBase64,
                WellKnownTypeNames.String,
                new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.Stream)
            )
        };

        return result;
    }

    private static JavaScriptFunctionDeclaration Build(string name, string? returnType, IEnumerable<JavaScriptParameterDeclaration>? parameters = null)
    {
        return new JavaScriptFunctionDeclaration(name, returnType, parameters ?? []);
    }

    private static JavaScriptFunctionDeclaration Build(string name, string? returnType, JavaScriptParameterDeclaration parameter)
        => Build(name, returnType, [parameter]);
}