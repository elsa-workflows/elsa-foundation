using Elsa.Mediator.Core.Contracts;
using System.Text.Json.Serialization;

namespace Elsa.Serialization.Core;

/// <summary>
/// Contribution event published by <see cref="JsonPayloadConverterRegistry"/>'s startup task.
/// Source modules (the default Serialization feature, the Expressions feature, any other
/// contributor) handle this event and contribute their <see cref="JsonConverter"/> instances
/// via <see cref="AddConverter"/>.
///
/// Exposes a method-based contribution API per framework §2.6.1's "intent-revealing methods,
/// not raw collections" sub-rule (Unit C Phase-3 amendment, 2026-05-28). The backing list is
/// private; read access is via the public <see cref="Converters"/> property typed as
/// <see cref="IReadOnlyList{T}"/> — non-mutating by type.
///
/// Canonical worked example of the framework §2.6.1 Registry + StartUp Task sub-pattern;
/// see Elsa §E3.3.
/// </summary>
public sealed class OnJsonPayloadConvertersInitializing : IDomainEvent
{
    private readonly List<JsonConverter> _converters = new();

    /// <summary>
    /// Contribute a converter to the JSON payload serializer's options. Called by handlers
    /// that own a converter (built-in: enum / TimeSpan / polymorphic-object / Type;
    /// contributed: variable / func-expression-value; etc.).
    /// </summary>
    public void AddConverter(JsonConverter converter) => _converters.Add(converter);

    /// <summary>
    /// Read-only view of contributed converters. Consumed by the startup task to flush
    /// into <see cref="JsonPayloadConverterRegistry"/> after the handler chain completes.
    /// </summary>
    public IReadOnlyList<JsonConverter> Converters => _converters;
}
