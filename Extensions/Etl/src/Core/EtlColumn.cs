namespace Elsa.Etl.Core;

/// <summary>Declares one CSV field and its requiredness for the finite ETL topology.</summary>
/// <param name="Name">The case-sensitive CSV field name.</param>
/// <param name="Type">The supported scalar type.</param>
/// <param name="Required">Whether a missing or empty value rejects the record.</param>
public sealed record EtlColumn(string Name, EtlColumnType Type, bool Required);
