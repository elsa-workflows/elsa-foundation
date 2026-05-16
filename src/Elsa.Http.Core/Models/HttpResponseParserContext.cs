using System;
using System.Collections.Generic;
using System.Text;

namespace Elsa.Http.Core.Models
{
    /// <summary>
    /// Represents the context in which an HTTP response is being parsed.
    /// </summary>
    public record HttpResponseParserContext(Stream Content, string ContentType, Type? ReturnType, IDictionary<string, string[]> Headers, CancellationToken CancellationToken);
}
