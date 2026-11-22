using System.Text;

namespace Elsa.Primitives.Extensions;

public static class StringExtensions
{
    // Initialisms that read better fully uppercased than title-cased. Kept deliberately small: only
    // tokens that commonly appear title-cased or lowercased in a member name yet should render as an
    // acronym ("Url" → "URL", "Bpmn" → "BPMN"). Matched case-insensitively against a whole word — never
    // against a fragment of a longer word. Acronyms authored as an all-caps run (e.g. "HTTPRequest",
    // "XMLParser") are already preserved verbatim by the splitter and need no entry here.
    private static readonly HashSet<string> KnownAcronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "id", "url", "uri", "api", "bpmn", "ip", "sql"
    };

    /// <summary>
    /// Turns a member-style identifier (<c>PascalCase</c>, <c>camelCase</c>, <c>snake_case</c>, or
    /// <c>kebab-case</c>) into spaced Title Case for display — e.g. <c>"ExpectedStatusCodes"</c> becomes
    /// <c>"Expected Status Codes"</c> and <c>"ControlFlow"</c> becomes <c>"Control Flow"</c>. Words that are
    /// known initialisms (<c>"Url"</c>, <c>"Bpmn"</c>) are fully uppercased (<c>"URL"</c>, <c>"BPMN"</c>).
    /// Already-spaced input is preserved. Returns the original value when it is null, empty, or whitespace.
    /// </summary>
    public static string Humanize(this string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var words = new List<string>();
        var current = new StringBuilder();

        void Flush()
        {
            if (current.Length == 0)
                return;
            words.Add(current.ToString());
            current.Clear();
        }

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c is '_' or '-' or ' ')
            {
                Flush();
                continue;
            }

            if (current.Length > 0 && char.IsUpper(c))
            {
                var previous = value[i - 1];
                // Break before an uppercase that starts a new word: after a lowercase/digit (fooBar),
                // or when it begins a lowercased run following an acronym run (HTTPRequest → HTTP|Request).
                var startsNewWord = !char.IsUpper(previous) || (i + 1 < value.Length && char.IsLower(value[i + 1]));
                if (startsNewWord)
                    Flush();
            }

            current.Append(c);
        }

        Flush();

        return string.Join(' ', words.Select(NormalizeWord));
    }

    private static string NormalizeWord(string word)
    {
        if (KnownAcronyms.Contains(word))
            return word.ToUpperInvariant();

        return word.Length == 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word[1..];
    }

    public static string Camelize(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;
        if (value.Length == 1)
            return value.ToLower();

        return char.ToLower(value[0]) + value[1..];
    }

    public static string Pascalize(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (value.Length == 1)
            return value.ToUpper();

        return char.ToUpper(value[0]) + value[1..];
    }

    /// <summary>
    /// Resolves a <see cref="Type"/> from its name. First tries <see cref="Type.GetType(string)"/> (which only
    /// searches the calling assembly, <c>Elsa.Primitives</c>, and corelib); when that returns null — the common
    /// case for an unqualified name whose type lives in some other loaded assembly — it falls back to scanning
    /// every assembly currently loaded into the app domain and returns the first match. Throws when the type still
    /// cannot be resolved.
    /// </summary>
    public static Type GetLoadedType(this string typeName)
    {
        var type = Type.GetType(typeName);
        if (type is not null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = assembly.GetType(typeName);
            if (type is not null)
                return type;
        }

        throw new ArgumentException($"Type with name '{typeName}' cannot be loaded");
    }
}