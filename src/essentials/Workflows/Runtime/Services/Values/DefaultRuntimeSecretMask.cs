using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Default <see cref="IRuntimeSecretMask"/>, registered scoped by the runtime: each scheduler work item's dependency
/// scope gets its own instance, which the activator registers resolved values with and the work handler's fault
/// boundaries mask with.
/// </summary>
/// <remarks>
/// <para>
/// A value is matched, ordinally, in three forms: as written, as the default JSON encoder writes it (which escapes
/// quotes, <c>+</c>, <c>&amp;</c>, angle brackets and every non-ASCII character as <c>\uXXXX</c>), and as the relaxed
/// JSON encoder writes it (which escapes only quotes, the backslash and control characters). One pass over the text
/// replaces, at each position, the longest form registered for the execution, so a marker that was inserted is never
/// matched again and a value that contains another registered value is masked whole.
/// </para>
/// <para>
/// Values are held until <see cref="Release"/> is called for their execution or the instance is dropped with its
/// scope. Releasing drops the references; .NET strings cannot be overwritten, so the text itself stays in memory until
/// the garbage collector reclaims it, as the hydrated activity's property does.
/// </para>
/// </remarks>
public sealed class DefaultRuntimeSecretMask : IRuntimeSecretMask
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, List<Replacement>> _registrations = new(StringComparer.Ordinal);

    public void Register(string activityExecutionId, string referenceName, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceName);
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
            return;

        var marker = $"[secret:{referenceName}]";
        var forms = new[]
            {
                value,
                JsonEncodedText.Encode(value).ToString(),
                JsonEncodedText.Encode(value, JavaScriptEncoder.UnsafeRelaxedJsonEscaping).ToString()
            }
            .Distinct(StringComparer.Ordinal)
            .Select(form => new Replacement(form, marker));
        lock (_lock)
        {
            if (!_registrations.TryGetValue(activityExecutionId, out var replacements))
                _registrations[activityExecutionId] = replacements = [];
            replacements.AddRange(forms.Where(replacement => !replacements.Contains(replacement)).ToArray());
        }
    }

    public bool HasRegistrations(string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        lock (_lock)
            return _registrations.ContainsKey(activityExecutionId);
    }

    public string Mask(string activityExecutionId, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        ArgumentNullException.ThrowIfNull(text);
        Replacement[] replacements;
        lock (_lock)
        {
            if (!_registrations.TryGetValue(activityExecutionId, out var registered))
                return text;
            replacements = [.. registered];
        }

        var next = FindNext(text, 0, replacements);
        if (next is null)
            return text;

        var masked = new StringBuilder(text.Length);
        var position = 0;
        while (next is { } match)
        {
            var (index, replacement) = match;
            masked.Append(text, position, index - position).Append(replacement.Marker);
            position = index + replacement.Form.Length;
            next = FindNext(text, position, replacements);
        }

        return masked.Append(text, position, text.Length - position).ToString();
    }

    public void Release(string activityExecutionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityExecutionId);
        lock (_lock)
            _registrations.Remove(activityExecutionId);
    }

    /// <summary>
    /// The earliest occurrence of a registered form at or after <paramref name="start"/>, and of the forms found there,
    /// the longest; of equally long ones, the first registered.
    /// </summary>
    private static (int Index, Replacement Replacement)? FindNext(string text, int start, IReadOnlyList<Replacement> replacements) =>
        replacements
            .Select(replacement => (Index: text.IndexOf(replacement.Form, start, StringComparison.Ordinal), Replacement: replacement))
            .Where(match => match.Index >= 0)
            .OrderBy(match => match.Index)
            .ThenByDescending(match => match.Replacement.Form.Length)
            .Select(match => ((int, Replacement)?)match)
            .FirstOrDefault();

    /// <summary>One form of a registered value and the marker it is replaced by.</summary>
    private sealed record Replacement(string Form, string Marker)
    {
        /// <summary>Prints the marker only: a record's generated <see cref="object.ToString"/> would print the value.</summary>
        public override string ToString() => Marker;
    }
}
