using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core
{
    public sealed class DefaultPayloadSerializerConverterProvider(Func<JsonConverter> factory) : IPayloadSerializerConverterProvider
    {
        public JsonConverter Get() => factory();
    }
}
