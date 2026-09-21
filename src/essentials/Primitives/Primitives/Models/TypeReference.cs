namespace Elsa.Primitives.Models;

/// <summary>
/// A rename-proof reference to an authored argument's type, persisted as exactly two fields: a stable
/// element-type <see cref="Alias"/> and a <see cref="CollectionKind"/> shape. This is the sole way an
/// authored Variable/Input/Output type is stored or transferred — no namespace, assembly name, or
/// assembly version (FR-001). Serializes natively (camelCase) via the configured payload serializer.
/// </summary>
public sealed record TypeReference(string Alias, CollectionKind CollectionKind = CollectionKind.Single);
