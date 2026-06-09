using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core;

/// <summary>
/// Contributor interface for the JSON payload serializer. A feature implements this and registers
/// it via DI; the single <c>RegisterJsonConverters</c> handler resolves every
/// <see cref="IJsonConverterSource"/>, collects their converters, and flushes them onto the
/// <c>OnJsonPayloadConvertersInitializing</c> event. The interface — not the event — describes the
/// intent.
/// </summary>
public interface IJsonConverterSource
{
    /// <summary>Return the <see cref="JsonConverter"/> instances this source contributes.</summary>
    IEnumerable<JsonConverter> GetConverters();
}
