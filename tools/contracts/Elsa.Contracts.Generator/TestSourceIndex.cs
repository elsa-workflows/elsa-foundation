using System.Text.RegularExpressions;

namespace Elsa.Contracts.Generator;

/// <summary>
/// Locates the body of a test method by its fully-qualified name, so Gate A can verify that the assertion a
/// claim rests on is really in the test the claim names (spec 150 FR-B2, hardened after a published claim
/// over-reached its pin).
/// </summary>
/// <remarks>
/// Text over Roslyn on purpose: the gate needs "is this assertion still here", not a semantic model, and a
/// compiler dependency in a build-time projection tool would cost far more than it buys. The method-body
/// extraction is brace-balanced rather than regex-terminated, which is what makes it reliable enough for
/// that question — a nested lambda or local function does not truncate the body.
/// </remarks>
public sealed class TestSourceIndex(string repoRoot)
{
    private readonly Dictionary<string, string?> _cache = new(StringComparer.Ordinal);
    private IReadOnlyList<string>? _sourceFiles;

    /// <summary>The body of <paramref name="fullyQualifiedMethod"/> (<c>Namespace.Type.Method</c>), or null.</summary>
    public string? MethodBody(string fullyQualifiedMethod)
    {
        if (_cache.TryGetValue(fullyQualifiedMethod, out var cached))
            return cached;

        var body = FindMethodBody(fullyQualifiedMethod);
        _cache[fullyQualifiedMethod] = body;
        return body;
    }

    private string? FindMethodBody(string fullyQualifiedMethod)
    {
        var lastDot = fullyQualifiedMethod.LastIndexOf('.');
        if (lastDot <= 0)
            return null;

        var methodName = fullyQualifiedMethod[(lastDot + 1)..];
        var typeName = fullyQualifiedMethod[..lastDot];
        var simpleTypeName = typeName[(typeName.LastIndexOf('.') + 1)..];

        // The declaring type's name is the file name by convention here; fall back to scanning every source
        // so a nested or differently-named file still resolves rather than silently reporting "not found".
        var candidates = SourceFiles()
            .OrderByDescending(path => string.Equals(Path.GetFileNameWithoutExtension(path), simpleTypeName, StringComparison.Ordinal));

        foreach (var path in candidates)
        {
            var text = File.ReadAllText(path);
            if (!text.Contains(simpleTypeName, StringComparison.Ordinal))
                continue;

            if (ExtractBody(text, methodName) is { } body)
                return body;
        }

        return null;
    }

    /// <summary>Brace-balanced body extraction, so nested blocks and lambdas do not truncate it.</summary>
    private static string? ExtractBody(string text, string methodName)
    {
        foreach (Match match in Regex.Matches(text, $@"\b{Regex.Escape(methodName)}\s*\(", RegexOptions.None))
        {
            var open = text.IndexOf('{', match.Index);
            if (open < 0)
                continue;

            // An expression-bodied member (`=> …;`) before the next brace is not a block body.
            var arrow = text.IndexOf("=>", match.Index, StringComparison.Ordinal);
            if (arrow >= 0 && arrow < open)
                continue;

            var depth = 0;
            for (var index = open; index < text.Length; index++)
            {
                if (text[index] == '{')
                    depth++;
                else if (text[index] == '}' && --depth == 0)
                    return text[open..(index + 1)];
            }
        }

        return null;
    }

    private IReadOnlyList<string> SourceFiles()
    {
        if (_sourceFiles is not null)
            return _sourceFiles;

        var testsRoot = Path.Combine(repoRoot, "tests");
        _sourceFiles = Directory.Exists(testsRoot)
            ? Directory.EnumerateFiles(testsRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) &&
                               !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                .ToArray()
            : [];
        return _sourceFiles;
    }
}
