using Elsa.Diagnostics.OpenTelemetry.Core.Models;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork.Catalogs;

/// <summary>A resource catalog value together with its durable optimistic-concurrency revision.</summary>
public sealed record ResourceCatalogEntry(TelemetryResource Resource, long Revision);

/// <summary>An instrument catalog value together with its retention metadata and durable revision.</summary>
public sealed record InstrumentCatalogEntry(
    MetricInstrument Instrument,
    DateTimeOffset LastSeen,
    string RetentionTieBreaker,
    long Revision);
