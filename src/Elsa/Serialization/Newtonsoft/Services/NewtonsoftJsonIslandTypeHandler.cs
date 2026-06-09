using Elsa.Serialization.Core;
using Newtonsoft.Json.Linq;

namespace Elsa.Serialization.Newtonsoft.Services;

public sealed class NewtonsoftJsonIslandTypeHandler : IJsonIslandTypeHandler
{
    public bool CanHandle(Type type) => type == typeof(JObject) || type == typeof(JArray);

    public object Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new JObject();

        var token = JToken.Parse(json);
        return token;
    }

    public string Write(object value) => value.ToString();
}
