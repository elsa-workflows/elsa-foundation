using System.Globalization;
using System.Text;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>Provider-neutral logical identity policy for workflow definitions.</summary>
public static class WorkflowDefinitionIdentity
{
    public static bool Equals(string value, string other) =>
        StringComparer.Ordinal.Equals(Fold(value), Fold(other));

    public static string Fold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var builder = new StringBuilder(value.Length * 7);
        for (var index = 0; index < value.Length;)
        {
            var scalar = (int)value[index];
            if (char.IsHighSurrogate((char)scalar))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index]))
                    throw new ArgumentException("Workflow-definition identities must be well-formed UTF-16.", nameof(value));
                scalar = char.ConvertToUtf32(value[index - 1], value[index]);
                index++;
            }
            else
            {
                if (char.IsLowSurrogate((char)scalar))
                    throw new ArgumentException("Workflow-definition identities must be well-formed UTF-16.", nameof(value));
                index++;
            }

            var upperScalar = scalar <= char.MaxValue
                ? char.ToUpperInvariant((char)scalar)
                : Rune.ToUpperInvariant(new Rune(scalar)).Value;
            builder.Append('|').Append(upperScalar.ToString("X6", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}
