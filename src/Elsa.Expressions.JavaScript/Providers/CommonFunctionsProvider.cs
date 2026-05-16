using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Constants;
using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Models;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace Elsa.Expressions.JavaScript.Providers
{
    /// <summary>
    /// Provides a set of common JavaScript function declarations for use in code generation or analysis scenarios.
    /// </summary>
    /// <remarks>This provider supplies declarations for frequently used JavaScript utility functions, such as
    /// string manipulation, GUID parsing, and encoding conversions. The set of functions returned is fixed and intended
    /// to support interoperability or scripting scenarios where these common operations are needed.</remarks>
    internal sealed partial class CommonFunctionsProvider
    
        : IJavaScriptFunctionDeclarationProvider, IJavaScriptFunctionProvider
    {
        private readonly JsonSerializerOptions _jsonSerializerOptions = CreateJsonSerializerOptions();
        private const string valueParameterName = "value";

        public ValueTask<IEnumerable<JavaScriptFunctionDeclaration>> GetDeclarations(CancellationToken cancellationToken = default)
        {
            var result = GetFunctionDeclarations();
            return new(result);
        }

        public ValueTask<IEnumerable<IJavaScriptFunction>> GetFunctions(IExpressionExecutionContext expressionExecutionContext, IExpressionEvaluatorOptions? options, CancellationToken cancellationToken = default)
        {
            var result = GetFunctions();
            return new(result);
        }

        private List<IJavaScriptFunction> GetFunctions()
        {
            return
            [
                new JavaScriptFunction<string>(JavaScriptFunctionNames.IsNullOrWhiteSpace, value => string.IsNullOrWhiteSpace(value)),
                new JavaScriptFunction<string>(JavaScriptFunctionNames.IsNullOrEmpty, value => string.IsNullOrEmpty(value)),
                new JavaScriptFunction<string>(JavaScriptFunctionNames.ToJson, value => Serialize(value)),
                new JavaScriptFunction<string>(JavaScriptFunctionNames.ParseGuid, value => Guid.Parse(value)),
                new JavaScriptFunction(JavaScriptFunctionNames.NewGuid, () => Guid.NewGuid()),
                new JavaScriptFunction(JavaScriptFunctionNames.NewGuidString, () => Guid.NewGuid().ToString()),
                new JavaScriptFunction(JavaScriptFunctionNames.NewShortGuid, () => NewShortGuid()),

                new JavaScriptFunction<byte[]>(JavaScriptFunctionNames.BytesToString, (value) => Encoding.UTF8.GetString(value)),
                new JavaScriptFunction<string>(JavaScriptFunctionNames.BytesFromString, (value) => Encoding.UTF8.GetBytes(value)),
                new JavaScriptFunction<byte[]>(JavaScriptFunctionNames.BytesToBase64, (value) =>  Convert.ToBase64String(value)),
                new JavaScriptFunction<string>(JavaScriptFunctionNames.BytesFromBase64, (value) =>  Convert.FromBase64String(value)),

                new JavaScriptFunction<string>(JavaScriptFunctionNames.StringToBase64, (value) =>  Convert.ToBase64String(Encoding.UTF8.GetBytes(value))),
                new JavaScriptFunction<string>(JavaScriptFunctionNames.StringFromBase64, (value) =>  Encoding.UTF8.GetString(Convert.FromBase64String(value))),

                new JavaScriptFunction<Stream>(JavaScriptFunctionNames.StreamToBytes, (value) =>  StreamToBytes(value)),
                new JavaScriptFunction<Stream>(JavaScriptFunctionNames.StreamToBase64, (value) =>  Convert.ToBase64String(StreamToBytes(value)))
            ];
        }

        private static List<JavaScriptFunctionDeclaration> GetFunctionDeclarations()
        {
            return
            [
                Build(JavaScriptFunctionNames.NewShortGuid, WellKnownTypeNames.String),
                Build(JavaScriptFunctionNames.NewGuid, WellKnownTypeNames.String),
                Build(JavaScriptFunctionNames.NewGuidString, WellKnownTypeNames.String),

                Build(
                    JavaScriptFunctionNames.IsNullOrWhiteSpace, 
                    WellKnownTypeNames.Bool, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
                ),
                Build(
                    JavaScriptFunctionNames.IsNullOrEmpty, 
                    WellKnownTypeNames.Bool, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
                ),
                Build(
                    JavaScriptFunctionNames.ParseGuid, 
                    WellKnownTypeNames.Guid, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
                ),

                Build(
                    JavaScriptFunctionNames.ToJson, 
                    WellKnownTypeNames.String, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.Any)
                ),
                Build(
                    JavaScriptFunctionNames.BytesToString, 
                    WellKnownTypeNames.String, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.ByteArray)
                ),
                Build(
                    JavaScriptFunctionNames.BytesFromString, 
                    WellKnownTypeNames.ByteArray, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
                ),
                Build(
                    JavaScriptFunctionNames.BytesToBase64, 
                    WellKnownTypeNames.String, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.ByteArray)
                ),
                Build(
                    JavaScriptFunctionNames.BytesFromBase64, 
                    WellKnownTypeNames.ByteArray, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
                ),

                Build(
                    JavaScriptFunctionNames.StringFromBase64, 
                    WellKnownTypeNames.String, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
                ),
                Build(
                    JavaScriptFunctionNames.StringToBase64, 
                    WellKnownTypeNames.String, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.String)
                ),
                Build(
                    JavaScriptFunctionNames.StreamToBytes, 
                    WellKnownTypeNames.ByteArray, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.Stream)
                ),
                Build(
                    JavaScriptFunctionNames.StreamToBase64, 
                    WellKnownTypeNames.String, 
                    new JavaScriptParameterDeclaration(valueParameterName, WellKnownTypeNames.Stream)
                )
            ];
        }

        static JavaScriptFunctionDeclaration Build(string name, string? returnType, IEnumerable<JavaScriptParameterDeclaration>? parameters = null)
        {
            return new JavaScriptFunctionDeclaration(name, returnType, parameters ?? []);
        }

        static JavaScriptFunctionDeclaration Build(string name, string? returnType, JavaScriptParameterDeclaration parameter)
            => Build(name, returnType, [parameter]);

        private string Serialize(object value)
        {
            return JsonSerializer.Serialize(value, _jsonSerializerOptions);
        }

        private static JsonSerializerOptions CreateJsonSerializerOptions()
        {
            var options = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.Create(UnicodeRanges.All)
            };
            options.Converters.Add(new JsonStringEnumConverter());
            return options;
        }

        private static byte[] StreamToBytes(Stream stream)
        {
            using var memoryStream = new MemoryStream();
            stream.CopyTo(memoryStream);
            return memoryStream.ToArray();
        }

        private static string NewShortGuid()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            var result = Convert.ToBase64String(bytes);

            return ShortGuidRegex().Replace(result, "");
        }

        [GeneratedRegex("[/+=]")]
        private static partial Regex ShortGuidRegex();
    }
}
