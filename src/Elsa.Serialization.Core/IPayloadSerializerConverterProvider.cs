using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core
{
    public interface IPayloadSerializerConverterProvider
    {
        JsonConverter Get();
    }
}
