using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Constants;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Core.Events;
using Elsa.Expressions.JavaScript.Core.Models;
using Elsa.Mediator.Core;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Text.Unicode;

namespace Elsa.Expressions.JavaScript.Jint.EventHandlers
{
    public sealed partial class AddCommonFunctions : IDomainEventHandler<OnEvaluatingScript>
    {
        private readonly JsonSerializerOptions _jsonSerializerOptions = CreateJsonSerializerOptions();

        public ValueTask Handle(OnEvaluatingScript domainEvent, CancellationToken cancellationToken)
        {            
            var functions = GetFunctions();
            functions.ForEach(domainEvent.EvaluationContext.AddFunction);

            return ValueTask.CompletedTask;
        }

        private List<IJavaScriptFunction> GetFunctions()
        {
            return
            [
                new JavaScriptFunction<string>(FunctionNames.IsNullOrWhiteSpace, value => string.IsNullOrWhiteSpace(value)),
                new JavaScriptFunction<string>(FunctionNames.IsNullOrEmpty, value => string.IsNullOrEmpty(value)),
                new JavaScriptFunction<string>(FunctionNames.ToJson, Serialize),
                new JavaScriptFunction<string>(FunctionNames.ParseGuid, value => Guid.Parse(value)),
                new JavaScriptFunction(FunctionNames.NewGuid, () => Guid.NewGuid()),
                new JavaScriptFunction(FunctionNames.NewGuidString, () => Guid.NewGuid().ToString()),
                new JavaScriptFunction(FunctionNames.NewShortGuid, () => NewShortGuid()),

                new JavaScriptFunction<byte[]>(FunctionNames.BytesToString, Encoding.UTF8.GetString),
                new JavaScriptFunction<string>(FunctionNames.BytesFromString, Encoding.UTF8.GetBytes),
                new JavaScriptFunction<byte[]>(FunctionNames.BytesToBase64, Convert.ToBase64String),
                new JavaScriptFunction<string>(FunctionNames.BytesFromBase64, Convert.FromBase64String),

                new JavaScriptFunction<string>(FunctionNames.StringToBase64, (value) =>  Convert.ToBase64String(Encoding.UTF8.GetBytes(value))),
                new JavaScriptFunction<string>(FunctionNames.StringFromBase64, (value) =>  Encoding.UTF8.GetString(Convert.FromBase64String(value))),

                new JavaScriptFunction<Stream>(FunctionNames.StreamToBytes, StreamToBytes),
                new JavaScriptFunction<Stream>(FunctionNames.StreamToBase64, (value) =>  Convert.ToBase64String(StreamToBytes(value)))
            ];
        }


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
