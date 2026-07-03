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
/// <see cref="OnJsonPayloadConvertersInitializing"/>; the single <c>RegisterJsonConverters</c>
/// handler aggregates every <see cref="IJsonConverterSource"/>; the startup task then flushes the
/// accumulated contributions into this registry via <see cref="Register"/>. After startup,
/// <see cref="Converters"/> is read directly.
/// </summary>
public sealed class JsonPayloadConverterRegistry
{
    private readonly List<JsonConverter> _converters = new();
    private int _revision;

    /// <summary>
    /// Monotonically increasing revision that bumps on every mutation of the registry.
    /// Consumers (notably <see cref="Elsa.Serialization.SystemText.Services.JsonPayloadSerializer"/>)
    /// cache derived state — such as a built <see cref="System.Text.Json.JsonSerializerOptions"/> —
    /// against the revision they last observed and rebuild only when it changes. Read via
    /// <see cref="System.Threading.Volatile"/> so a rebuild is never missed across threads.
    /// </summary>
    public int Revision => Volatile.Read(ref _revision);

    /// <summary>
    /// Add a converter to the registry. Intended to be called by the startup task that
    /// drains <see cref="OnJsonPayloadConvertersInitializing.Converters"/>; not by sources
    /// (sources contribute by returning converters from <c>IJsonConverterSource.GetConverters</c>).
    /// </summary>
    public void Register(JsonConverter converter)
    {
        _converters.Add(converter);
        BumpRevision();
    }

    /// <summary>
    /// Register a batch of converters. Convenience for the startup task's flush step.
    /// </summary>
    public void RegisterAll(IEnumerable<JsonConverter> converters)
    {
        _converters.AddRange(converters);
        BumpRevision();
    }

    private void BumpRevision() => Interlocked.Increment(ref _revision);

    /// <summary>
    /// Read-only view of the registered converters. Consumed by
    /// <see cref="JsonPayloadSerializer"/> at sync-access time.
    /// </summary>
    public IReadOnlyList<JsonConverter> Converters => _converters;
}
