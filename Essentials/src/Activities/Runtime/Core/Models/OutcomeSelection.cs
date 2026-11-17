using System.Text.Json.Serialization;

namespace Elsa.Activities.Runtime.Core.Models;

/// <summary>The three states of an <see cref="OutcomeSelection"/> (ADR 0045 §6, spec 1208 FR-001).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OutcomeSelectionState>))]
public enum OutcomeSelectionState
{
    /// <summary>The activity chose no branch: it completes with its default outcome, <see cref="OutcomeSelection.DefaultOutcome"/>.</summary>
    Unspecified,

    /// <summary>The activity selected one or more declared outcome names.</summary>
    Named,

    /// <summary>The activity selected no outgoing path at all.</summary>
    Empty
}

/// <summary>
/// The outcomes a successful completion takes: unspecified (<c>Done</c>), one or more named outcomes, or explicitly
/// none. Unspecified and empty stay distinct through completion, parent and join handling, persistence and recovery
/// (ADR 0045 §6, spec 1208 FR-001 to FR-006).
/// </summary>
public sealed class OutcomeSelection : IEquatable<OutcomeSelection>
{
    /// <summary>The outcome an unspecified selection resolves to: "completed without choosing a branch".</summary>
    public const string DefaultOutcome = "Done";

    private static readonly string[] DefaultOutcomeNames = [DefaultOutcome];

    private readonly string[] _names;

    private OutcomeSelection(OutcomeSelectionState state, string[] names)
    {
        State = state;
        _names = names;
    }

    /// <summary>Rehydrates a persisted selection, applying the same rules as the factories.</summary>
    [JsonConstructor]
    public OutcomeSelection(OutcomeSelectionState state, IReadOnlyList<string>? names)
    {
        if (!Enum.IsDefined(state))
            throw new ArgumentOutOfRangeException(nameof(state), state, "The outcome selection state is undefined.");

        switch (state)
        {
            case OutcomeSelectionState.Named:
                State = state;
                _names = Normalize(names ?? throw new ArgumentException("A named outcome selection must carry its names.", nameof(names)));
                break;
            default:
                if (names is { Count: > 0 })
                    throw new ArgumentException($"An {state.ToString().ToLowerInvariant()} outcome selection carries no names.", nameof(names));
                State = state;
                _names = [];
                break;
        }
    }

    /// <summary>No outcome chosen: the activity completes with <see cref="DefaultOutcome"/>.</summary>
    public static OutcomeSelection Unspecified { get; } = new(OutcomeSelectionState.Unspecified, []);

    /// <summary>An explicitly empty selection: no outgoing connection is followed.</summary>
    public static OutcomeSelection Empty { get; } = new(OutcomeSelectionState.Empty, []);

    /// <summary>One named outcome.</summary>
    public static OutcomeSelection Named(string name) => new(OutcomeSelectionState.Named, Normalize([name]));

    /// <summary>
    /// One or more named outcomes, in the order selected; a repeated name counts once, at its first occurrence. An empty
    /// list is refused: use <see cref="Empty"/> to select no path.
    /// </summary>
    public static OutcomeSelection Named(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return new(OutcomeSelectionState.Named, Normalize(names.ToArray()));
    }

    /// <inheritdoc cref="Named(IEnumerable{string})"/>
    public static OutcomeSelection Named(params string[] names) => Named((IEnumerable<string>)names);

    public OutcomeSelectionState State { get; }

    /// <summary>The selected names; empty unless <see cref="State"/> is <see cref="OutcomeSelectionState.Named"/>.</summary>
    public IReadOnlyList<string> Names => _names;

    [JsonIgnore]
    public bool IsUnspecified => State == OutcomeSelectionState.Unspecified;

    [JsonIgnore]
    public bool IsEmpty => State == OutcomeSelectionState.Empty;

    /// <summary>The names a parent that routes by outcomes follows: <c>[Done]</c>, the names, or none.</summary>
    public IReadOnlyList<string> ResolveNames() => State switch
    {
        OutcomeSelectionState.Unspecified => DefaultOutcomeNames,
        OutcomeSelectionState.Named => _names,
        _ => []
    };

    public bool Equals(OutcomeSelection? other) =>
        other is not null && State == other.State && _names.AsSpan().SequenceEqual(other._names);

    public override bool Equals(object? obj) => Equals(obj as OutcomeSelection);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(State);
        foreach (var name in _names)
            hash.Add(name, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    public override string ToString() => State switch
    {
        OutcomeSelectionState.Named => $"Named({string.Join(", ", _names)})",
        _ => State.ToString()
    };

    private static string[] Normalize(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            throw new ArgumentException("A named outcome selection must name at least one outcome; use OutcomeSelection.Empty to select no path.", nameof(names));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>(names.Count);
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An outcome name cannot be blank.", nameof(names));
            if (seen.Add(name))
                result.Add(name);
        }

        return [.. result];
    }
}
