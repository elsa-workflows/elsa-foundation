using System.Reflection;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Reads <c>[ConsumerNote]</c> declarations out of an assembly's types and properties (RFC #1191 Part 2,
/// spec 150 FR-B1).
/// </summary>
/// <remarks>
/// <para>
/// A separate pass rather than a field on the reconciliation model, deliberately. That model feeds the
/// activity content hash: a note added there would change the hash, and under reconciliation Model X a
/// pre-existing database would then throw <c>ActivityVersionHashMismatchException</c>. Editing a sentence of
/// documentation must never force a new activity version, so notes are a build-time enrichment of the
/// published contract and are excluded from identity.
/// </para>
/// <para>
/// The consequence, stated rather than hidden: the running catalog endpoint does not serve notes, only the
/// fragments carry them. A consumer reading the artifacts gets them; a consumer introspecting a live server
/// does not.
/// </para>
/// </remarks>
public static class ConsumerNoteReader
{
    private const string AttributeName = "ConsumerNoteAttribute";

    /// <summary>Notes declared on <paramref name="type"/> itself.</summary>
    public static IReadOnlyList<ConsumerNoteContract> ForType(Type type) =>
        Read(SafeAttributes(type), member: null);

    /// <summary>Notes declared on the type's properties, keyed by property name.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<ConsumerNoteContract>> ForProperties(Type type)
    {
        var result = new Dictionary<string, IReadOnlyList<ConsumerNoteContract>>(StringComparer.Ordinal);
        PropertyInfo[] properties;
        try
        {
            properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        }
        catch (Exception)
        {
            // A partially resolvable type is skipped the same way the scanner skips one: an absent note is
            // an honest omission, a crashed generation is not.
            return result;
        }

        foreach (var property in properties)
        {
            var notes = Read(SafeAttributes(property), member: null);
            if (notes.Count > 0)
                result[property.Name] = notes;
        }

        return result;
    }

    private static IReadOnlyList<CustomAttributeData> SafeAttributes(MemberInfo member)
    {
        try
        {
            return member.GetCustomAttributesData().ToArray();
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static IReadOnlyList<ConsumerNoteContract> Read(IReadOnlyList<CustomAttributeData> attributes, string? member)
    {
        var notes = new List<ConsumerNoteContract>();
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeType.Name != AttributeName || attribute.ConstructorArguments.Count < 2)
                continue;

            // The enum arrives boxed as its underlying int; the wire form is the member name, camel-cased.
            var kind = attribute.ConstructorArguments[0].Value is { } rawKind && int.TryParse(rawKind.ToString(), out var ordinal)
                ? KindName(ordinal)
                : null;
            if (kind is null || attribute.ConstructorArguments[1].Value is not string note || string.IsNullOrWhiteSpace(note))
                continue;

            var explicitMember = attribute.NamedArguments
                .FirstOrDefault(argument => argument.MemberName == "Member").TypedValue.Value as string;

            notes.Add(new ConsumerNoteContract(kind, note, explicitMember ?? member));
        }

        return notes
            .OrderBy(note => note.Kind, StringComparer.Ordinal)
            .ThenBy(note => note.Member ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(note => note.Note, StringComparer.Ordinal)
            .ToArray();
    }

    // Mirrors Elsa.Primitives.Models.ConsumerNoteKind. Read by ordinal because the enum type itself lives in
    // the reflection-only context, where Enum.GetName cannot run.
    private static string? KindName(int ordinal) => ordinal switch
    {
        0 => "default",
        1 => "requirement",
        2 => "constraint",
        3 => "failureMode",
        4 => "limit",
        5 => "composition",
        _ => null
    };
}
