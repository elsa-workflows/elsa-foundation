using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core;

/// <summary>
/// Registry of <see cref="JsonConverter"/> instances contributed by feature modules. The
/// <see cref="JsonPayloadSerializer"/> reads from this registry synchronously while building
/// its <see cref="System.Text.Json.JsonSerializerOptions"/> — sync access is required because
/// <see cref="System.Text.Json"/> converter callbacks can't await async dispatch.
///
/// Population happens once at startup via the Registry + StartUp Task sub-pattern from
/// framework §2.6.1 (Elsa-side worked example: §E3.3). The startup task publishes
/// <see cref="OnJsonPayloadConvertersInitializing"/>; handlers contribute via
/// <c>AddConverter(...)</c>; the startup task then flushes the accumulated contributions
/// into this registry via <see cref="Register"/>. After startup, <see cref="Converters"/>
/// is read directly.
/// </summary>
public sealed class JsonPayloadConverterRegistry
{
    private readonly List<JsonConverter> _converters = new();

    /// <summary>
    /// Add a converter to the registry. Intended to be called by the startup task that
    /// drains <see cref="OnJsonPayloadConvertersInitializing.Converters"/>; not by handlers
    /// (handlers contribute through the event's <c>AddConverter</c> method).
    /// </summary>
    public void Register(JsonConverter converter) => _converters.Add(converter);

    /// <summary>
    /// Register a batch of converters. Convenience for the startup task's flush step.
    /// </summary>
    public void RegisterAll(IEnumerable<JsonConverter> converters) => _converters.AddRange(converters);

    /// <summary>
    /// Read-only view of the registered converters. Consumed by
    /// <see cref="JsonPayloadSerializer"/> at sync-access time.
    /// </summary>
    public IReadOnlyList<JsonConverter> Converters => _converters;
}
