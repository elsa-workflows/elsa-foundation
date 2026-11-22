using Elsa.Http.Core.Contracts;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Elsa.Http.Services;

/// <summary>
/// A content writer that writes content in the application/x-www-form-urlencoded format. 
/// </summary>
internal sealed class FormUrlEncodedHttpContentFactory : IHttpContentFactory
{
    /// <inheritdoc />
    public IEnumerable<string> SupportedContentTypes => ["application/x-www-form-urlencoded"];

    /// <inheritdoc />
    public HttpContent CreateHttpContent(object content, string? contentType = null) => new FormUrlEncodedContent(GetContentAsDictionary(content));

    private static IDictionary<string, string> GetContentAsDictionary(object content)
    {
        if (content is IDictionary<string, object> dictionary)
            return dictionary.ToDictionary(x => x.Key, x => x.Value.ToString() ?? string.Empty);

        if (content is string or JsonObject)
            return (Dictionary<string, string>)Elsa.Serialization.Core.JsonConversionContract.For(typeof(Dictionary<string, string>))
                .Read(Elsa.Serialization.Core.JsonConversionContract.SerializeToElement(content))!;

        var jsonElement = Elsa.Serialization.Core.JsonConversionContract.SerializeToElement(content);
        return jsonElement.EnumerateObject().ToDictionary(x => x.Name, x => x.Value.ToString());
    }
}
