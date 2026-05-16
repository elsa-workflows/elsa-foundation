using Elsa.Http.Core.Converters;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Serialization;

namespace Elsa.Http.Core.Models
{
    /// <summary>
    /// Represents the headers of an HTTP message.
    /// </summary>
    [JsonConverter(typeof(HttpHeadersConverter))]
    public sealed class HttpHeaders : Dictionary<string, string[]>
    {
        /// <inheritdoc />
        public HttpHeaders()
        {
        }

        /// <inheritdoc />
        public HttpHeaders(IDictionary<string, string[]> source)
        {
            foreach (var item in source)
                Add(item.Key, item.Value);
        }

        /// <inheritdoc />
        public HttpHeaders(HttpResponseHeaders source)
        {
            foreach (var item in source)
                Add(item.Key, item.Value.ToArray());
        }

        /// <inheritdoc />
        public HttpHeaders(HttpContentHeaders source)
        {
            foreach (var item in source)
                Add(item.Key, item.Value.ToArray());
        }

        /// <summary>
        /// Gets the content type.
        /// </summary>
        public string? ContentType => TryGetValue("content-type", out var result) && result.Length > 0
            ? result[0]
            : null;
    }
}
