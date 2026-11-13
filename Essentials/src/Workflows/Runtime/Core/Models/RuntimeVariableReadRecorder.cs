using Elsa.Primitives.Models;
namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Records which committed variable values an input materialization actually read (spec 1221 FR-002, FR-004): a
/// variable-read binding, a declared variable expression parameter, or an ambient variable an expression references.
/// A visible variable nobody read is never recorded. The recorder holds the committed envelope instances it was handed;
/// the materializer maps them back to their frame and key to pin the version seen.
/// </summary>
public sealed class RuntimeVariableReadRecorder
{
    private readonly List<ValueEnvelope> _reads = [];

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
