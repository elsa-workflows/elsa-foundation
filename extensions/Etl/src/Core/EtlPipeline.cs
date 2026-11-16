namespace Elsa.Etl.Core;

/// <summary>The supported finite CSV-to-JSONL pipeline definition.</summary>
/// <remarks>
/// This is an authoring/runtime DTO. Its read-only list interfaces do not make caller-owned collection instances
/// immutable; published artifacts snapshot the canonical serialized document separately.
/// </remarks>
public sealed record EtlPipeline(
    EtlFileBindings Bindings,
    IReadOnlyList<EtlColumn> SourceSchema,
    IReadOnlyList<EtlColumn> LookupSchema,
    string SourceLookupField,
    string LookupKeyField,
    IReadOnlyList<EtlOutputField> Mapping,
    IReadOnlyList<EtlFilter> Filters,
    EtlLimits Limits);
