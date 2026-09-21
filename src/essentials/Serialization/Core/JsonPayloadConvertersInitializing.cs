using Elsa.Events.Core.Contracts;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core;

/// <summary>
/// Contribution event published by <see cref="JsonPayloadConverterRegistry"/>'s startup task.
/// The single <c>RegisterJsonConverters</c> handler resolves every <see cref="IJsonConverterSource"/>
/// and adds their converters to <see cref="Converters"/>; the startup task then flushes the
/// collected set into the registry.
/// </summary>
/// <remarks>
/// <see cref="Converters"/> is a directly-accessible <see cref="ICollection{T}"/>: the aggregating
/// handler writes into it; the startup task reads it after the chain completes. Sources do not
/// touch the event — they return their converters via <see cref="IJsonConverterSource.GetConverters"/>.
/// </remarks>
public sealed class JsonPayloadConvertersInitializing : IEvent
{
    /// <summary>The accumulated converters. Populated by the aggregating handler.</summary>
    public ICollection<JsonConverter> Converters { get; } = [];
}
