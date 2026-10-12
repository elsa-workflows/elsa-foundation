namespace Elsa.Cluster.Core.Models;

/// <summary>
/// The limits a host id must meet (FR-003): non-blank, well-formed Unicode, at most 128 UTF-16 code units, compared
/// ordinally. They repeat the limits the distributed runtime sets on its node id, so placement can take its node id
/// from the host id unchanged; this contract cannot reference the runtime.
/// </summary>
public static class ClusterHostIdConstraints
{
    /// <summary>Maximum UTF-16 code-unit length of a host id.</summary>
    public const int MaximumLength = 128;

    /// <summary>Returns <paramref name="value"/> when it is a valid host id, and throws otherwise.</summary>
    public static string Validate(string? value, string parameterName)
    {
        var problem = Describe(value);
        return problem is null ? value! : throw new ArgumentException(problem, parameterName);
    }

    /// <summary>Describes what is wrong with a host id, or returns <see langword="null"/> when it is valid.</summary>
    public static string? Describe(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "A host id must not be blank.";
        if (value.Length > MaximumLength)
            return $"A host id cannot exceed {MaximumLength} UTF-16 code units; '{value[..16]}…' has {value.Length}.";
        return IsWellFormed(value) ? null : "A host id must contain well-formed Unicode.";
    }

    private static bool IsWellFormed(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsLowSurrogate(character))
                return false;
            if (char.IsHighSurrogate(character) && (++index >= value.Length || !char.IsLowSurrogate(value[index])))
                return false;
        }

        return true;
    }
}
