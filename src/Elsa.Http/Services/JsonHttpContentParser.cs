using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Serialization.Core;
using System.Dynamic;

namespace Elsa.Http.Services
{
    /// <summary>
    /// Reads application/json and text/json content type streams.
    /// </summary>
    internal sealed class JsonHttpContentParser(IObjectConverter objectConverter, IPayloadSerializer payloadSerializer) : IHttpContentParser
    {
        /// <inheritdoc />
        public int Priority => 0;

        /// <inheritdoc />
        public bool GetSupportsContentType(HttpResponseParserContext context) => context.ContentType.Contains("json", StringComparison.InvariantCultureIgnoreCase);

        /// <inheritdoc />
        public async Task<object> ReadAsync(HttpResponseParserContext context)
        {            
            var content = context.Content;
            using var reader = new StreamReader(content, leaveOpen: true);
            var json = await reader.ReadToEndAsync();
            var returnType = context.ReturnType;

            if (returnType == typeof(string))
                return json;

            if (returnType == null || returnType.IsPrimitive)
                return objectConverter.ConvertTo(json, returnType ?? typeof(string))!;

            if (returnType != typeof(ExpandoObject) && returnType != typeof(object))
            {
                return payloadSerializer.Deserialize(json, returnType);
            }                
            
            return payloadSerializer.Deserialize<object>(json)!;
        }
    }
}
