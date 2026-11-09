namespace Elsa.Etl.Core;

/// <summary>Maps one source or lookup field to an output field.</summary>
/// <param name="Name">The output property name.</param>
/// <param name="Origin">The record supplying the value.</param>
/// <param name="Field">The input field name.</param>
public sealed record EtlOutputField(string Name, EtlFieldOrigin Origin, string Field);
