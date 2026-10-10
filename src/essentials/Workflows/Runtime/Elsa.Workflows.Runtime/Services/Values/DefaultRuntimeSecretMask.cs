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

    // Per execution, every registered form in registration order. Each registration replaces the array, so Mask reads
    // the current one without copying it.
    private readonly Dictionary<string, Replacement[]> _registrations = new(StringComparer.Ordinal);

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
            var registered = _registrations.GetValueOrDefault(activityExecutionId, []);
            _registrations[activityExecutionId] = [.. registered, .. forms.Where(replacement => !registered.Contains(replacement))];
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
        Replacement[]? replacements;
        lock (_lock)
            _registrations.TryGetValue(activityExecutionId, out replacements);
        if (replacements is null)
            return text;

        // The next occurrence of each form at or after the current position, or -1 when there is none. An index at or
        // after the position stays valid as the position advances, so only the forms it passed are searched again.
        var next = new int[replacements.Length];
        for (var form = 0; form < replacements.Length; form++)
            next[form] = text.IndexOf(replacements[form].Form, StringComparison.Ordinal);

        var match = Earliest(replacements, next);
        if (match < 0)
            return text;

        var masked = new StringBuilder(text.Length);
        var position = 0;
        while (match >= 0)
        {
            var replacement = replacements[match];
            masked.Append(text, position, next[match] - position).Append(replacement.Marker);
            position = next[match] + replacement.Form.Length;
            for (var form = 0; form < replacements.Length; form++)
            {
                if (next[form] >= 0 && next[form] < position)
                    next[form] = text.IndexOf(replacements[form].Form, position, StringComparison.Ordinal);
            }

            match = Earliest(replacements, next);
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
    /// The form whose next occurrence comes first, and of the forms occurring there, the longest; of equally long ones,
    /// the first registered. -1 when no form occurs again.
    /// </summary>
    private static int Earliest(Replacement[] replacements, int[] next)
    {
        var earliest = -1;
        for (var form = 0; form < replacements.Length; form++)
        {
            if (next[form] >= 0 && (earliest < 0
                    || next[form] < next[earliest]
                    || next[form] == next[earliest] && replacements[form].Form.Length > replacements[earliest].Form.Length))
                earliest = form;
        }

        return earliest;
    }

    /// <summary>One form of a registered value and the marker it is replaced by.</summary>
    private sealed record Replacement(string Form, string Marker)
    {
        /// <summary>Prints the marker only: a record's generated <see cref="object.ToString"/> would print the value.</summary>
        public override string ToString() => Marker;
    }
}
