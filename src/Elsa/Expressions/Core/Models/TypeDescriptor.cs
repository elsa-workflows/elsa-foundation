namespace Elsa.Expressions.Core.Models;

/// <summary>
/// The shared shape a <c>IVariableTypeDescriptorProvider</c> contributes for a selectable argument
/// element type. Read by both the design-time catalog (all fields, for the picker) and the runtime
/// registry seed (<see cref="Alias"/> + <see cref="ClrType"/> only). Keyed by <see cref="Alias"/>.
/// </summary>
/// <param name="Alias">Stable element-type identifier — bare for framework primitives, dotted/reverse-DNS for module types.</param>
/// <param name="ClrType">The CLR type the alias resolves to (resolution datum consumed by the registry seed).</param>
/// <param name="DisplayName">Picker label.</param>
/// <param name="Category">Grouping key (e.g. "Primitives", "Http").</param>
/// <param name="DefaultEditor">Open hint for the default-value editor (e.g. <c>text</c>, <c>checkbox</c>, <c>number</c>, <c>date</c>).</param>
public sealed record TypeDescriptor(string Alias, Type ClrType, string DisplayName, string Category, string DefaultEditor);
