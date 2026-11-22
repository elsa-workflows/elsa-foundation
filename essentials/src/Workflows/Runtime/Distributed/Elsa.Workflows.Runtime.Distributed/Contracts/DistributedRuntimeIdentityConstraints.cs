namespace Elsa.Workflows.Runtime.Distributed.Contracts;

/// <summary>
/// Defines the portable identity boundary shared by distributed-runtime contracts and implementations.
/// </summary>
public static class DistributedRuntimeIdentityConstraints
{
    /// <summary>
    /// Maximum UTF-16 code-unit length for workflow-execution and placement-owner identifiers.
    /// </summary>
    public const int MaximumLength = 128;

    /// <summary>
    /// Validates a non-blank, well-formed Unicode identifier that can be represented by every distributed store.
    /// </summary>
    public static string Validate(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);

        if (value.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A distributed-runtime identifier cannot exceed {MaximumLength} UTF-16 code units.",
                parameterName);
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (char.IsLowSurrogate(character))
                throw InvalidUnicode(parameterName);
            if (!char.IsHighSurrogate(character))
                continue;
            if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                throw InvalidUnicode(parameterName);
        }

        return value;
    }

    private static ArgumentException InvalidUnicode(string parameterName) =>
        new("A distributed-runtime identifier must contain well-formed Unicode.", parameterName);
}
