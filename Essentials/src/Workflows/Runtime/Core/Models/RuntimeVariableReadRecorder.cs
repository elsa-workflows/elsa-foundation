using System.Text.Json.Serialization;
using Elsa.Primitives.Models;
namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Records which committed variable values an input materialization actually read (spec 1221 FR-002, FR-004): a
/// variable-read binding, a declared variable expression parameter (pinned by publish, spec 1222), or a variable an
/// expression read by a computed name through the run-time lookup.
/// A visible variable nobody read is never recorded. The recorder holds the committed envelope instances it was handed;
/// the materializer maps them back to their frame and key to pin the version seen.
/// </summary>
public sealed class RuntimeVariableReadRecorder
{
    private readonly List<ValueEnvelope> _reads = [];
    private readonly List<RuntimeVariableComputedRead> _computed = [];

    /// <summary>
    /// Records one read a script made by a computed or opaque name (spec 1222 FR-010, FR-011), present or absent. A
    /// present read is also recorded with <see cref="Record"/>, so it joins the input read set.
    /// </summary>
    public void RecordComputed(RuntimeVariableComputedRead read)
    {
        ArgumentNullException.ThrowIfNull(read);
        if (!_computed.Any(existing => StringComparer.Ordinal.Equals(existing.Name, read.Name) &&
                                       ReferenceEquals(existing.Variable, read.Variable)))
            _computed.Add(read);
    }

    /// <summary>The computed reads, in first-read order.</summary>
    public IReadOnlyList<RuntimeVariableComputedRead> ComputedReads => _computed;

    /// <summary>
    /// The recorded computed reads for the input snapshot, each present one with the version it read from its frame.
    /// Null when no script read a variable by a computed name.
    /// </summary>
    public IReadOnlyList<VariableComputedRead>? ToComputedReads(IReadOnlyList<VariableFrameState> visibleFrames)
    {
        ArgumentNullException.ThrowIfNull(visibleFrames);
        if (_computed.Count == 0)
            return null;

        return _computed.Select(read =>
        {
            if (read.Variable is not { } variable)
                return VariableComputedRead.Absent(read.Name);

            long? version = null;
            for (var index = visibleFrames.Count - 1; index >= 0; index--)
            {
                var frame = visibleFrames[index];
                if (frame.Values.TryGetValue(variable.VariableKey, out var envelope) && ReferenceEquals(envelope, variable.Envelope))
                {
                    version = frame.VersionOf(variable.VariableKey).Version;
                    break;
                }
            }

            return new VariableComputedRead(read.Name, variable.ScopeId, variable.VariableKey, version, present: true);
        }).ToArray();
    }

    public void Record(ValueEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        foreach (var read in _reads)
        {
            if (ReferenceEquals(read, envelope))
                return;
        }

        _reads.Add(envelope);
    }

    /// <summary>The envelopes read, in first-read order, each once.</summary>
    public IReadOnlyList<ValueEnvelope> Reads => _reads;

    /// <summary>
    /// The input read set these reads give, against the frames they came from: each read envelope is matched by identity
    /// to its frame and key, innermost frame first. Under <see cref="LostUpdateMode.Fault"/> each entry also keeps the
    /// stored digest seen; in the default mode no digest is kept.
    /// </summary>
    public VariableInputReadSet ToInputReadSet(IReadOnlyList<VariableFrameState> visibleFrames, LostUpdateMode mode)
    {
        ArgumentNullException.ThrowIfNull(visibleFrames);
        var entries = new List<VariableReadSetEntry>();
        foreach (var read in _reads)
        {
            for (var index = visibleFrames.Count - 1; index >= 0; index--)
            {
                var frame = visibleFrames[index];
                var key = frame.Values.FirstOrDefault(pair => ReferenceEquals(pair.Value, read)).Key;
                if (key is null)
                    continue;

                var version = frame.VersionOf(key);
                entries.Add(new VariableReadSetEntry(
                    frame.ScopeId,
                    key,
                    version.Version,
                    mode == LostUpdateMode.Fault ? frame.CurrentDigestOf(key) : null));
                break;
            }
        }

        return new VariableInputReadSet(entries, mode);
    }
}

/// <summary>One computed read as the evaluator served it: the name asked for and the visible variable it resolved to, or none.</summary>
public sealed record RuntimeVariableComputedRead(string Name, RuntimeReadableVariable? Variable);

/// <summary>
/// One variable read a script made by a computed or opaque name, recorded in the input snapshot (spec 1222 FR-010,
/// FR-011, data model "Recorded read"). An absent read matched no visible variable: the script read <c>undefined</c>
/// (nil in Liquid), and <see cref="Warning"/> says so. It never carries a value, so a secret is never in it (FR-019).
/// </summary>
public sealed record VariableComputedRead
{
    public const string MissingNameWarning = "VF-READ-001";

    [JsonConstructor]
    public VariableComputedRead(string name, string? scopeId, string? variableKey, long? version, bool present, string? warning = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        ScopeId = scopeId;
        VariableKey = variableKey;
        Version = version;
        Present = present;
        Warning = warning;
    }

    public static VariableComputedRead Absent(string name) =>
        new(name, null, null, null, present: false,
            $"{MissingNameWarning}: a script read variable '{name}' by a computed name, and no visible variable has that name; it read undefined (nil in Liquid).");

    public string Name { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ScopeId { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? VariableKey { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Version { get; }

    public bool Present { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Warning { get; }
}
