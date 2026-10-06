using Elsa.Modularity.Planning.Bridge;

namespace Elsa.Cli;

/// <summary>Coordinates frozen portable intent files and one explicitly bound complete private bundle.</summary>
public sealed class PortableCompositionCapture : IDisposable
{
    private readonly object _lifecycleGate = new();
    private readonly CompositionInputSnapshot _inputs;
    private readonly CompositionFileReader _reader;
    private CompositionFileSource? _privateBundle;
    private bool _bindingAttempted;
    private bool _disposed;

    private PortableCompositionCapture(CompositionInputSnapshot inputs, CompositionFileReader reader)
    {
        _inputs = inputs;
        _reader = reader;
    }

    /// <summary>Captures each supplied public, review, catalog, profile, receipt or overlay file once.</summary>
    public static PortableCompositionCapture OpenInputs(IEnumerable<string> explicitInputPaths,
        CompositionFileReader? reader = null)
    {
        var candidateReader = reader ?? new CompositionFileReader();
        return new PortableCompositionCapture(
            CompositionInputSnapshot.OpenForCandidate(explicitInputPaths, candidateReader), candidateReader);
    }

    /// <summary>Returns a defensive copy of a file captured by <see cref="OpenInputs"/>.</summary>
    public byte[] ReadBytes(string path)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            return _inputs.ReadBytes(path);
        }
    }

    /// <summary>Reads captured bytes using the existing strict text decoder.</summary>
    public string ReadText(string path)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            return _inputs.ReadText(path);
        }
    }

    /// <summary>
    /// Captures a complete supported host bundle under the caller-validated fixed context exactly once.
    /// The receipt and public artifact must be read and correlated from this capture before this call.
    /// </summary>
    public SourceSnapshot BindPrivateBundle(string hostDirectory, string shellId, string environment)
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            if (_bindingAttempted)
                throw CliRefusal.Usage("portable-input-invalid", "The portable capture can bind only one private input bundle.");

            _bindingAttempted = true;
            _privateBundle = CompositionFileSource.OpenForCandidate(hostDirectory, shellId, environment, _reader);
            return _privateBundle.Snapshot;
        }
    }

    /// <summary>Gets the one bound bundle snapshot after its context has been validated by the caller.</summary>
    public SourceSnapshot PrivateBundleSnapshot
    {
        get
        {
            lock (_lifecycleGate)
            {
                ThrowIfDisposed();
                return _privateBundle?.Snapshot ??
                       throw CliRefusal.Usage("portable-input-invalid", "The portable capture has no bound private input bundle.");
            }
        }
    }

    /// <summary>Rechecks every explicitly supplied file and the complete supported bundle inventory.</summary>
    public void VerifyUnchanged()
    {
        lock (_lifecycleGate)
        {
            ThrowIfDisposed();
            if (_privateBundle is null)
                throw CliRefusal.Usage("portable-input-invalid", "The portable capture has no bound private input bundle.");

            try
            {
                _inputs.VerifyUnchanged();
                _privateBundle.VerifyUnchanged();
            }
            catch (CliRefusal refusal) when (refusal.Code is "composition-input-changed" or "bridge-source-changed")
            {
                throw Changed();
            }
        }
    }

    public void Dispose()
    {
        lock (_lifecycleGate)
        {
            if (_disposed)
                return;

            _disposed = true;
            _inputs.Dispose();
            _privateBundle = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw CliRefusal.Usage("candidate-capture-invalid", "The captured portable inputs are no longer available.");
    }

    private static CliRefusal Changed() =>
        CliRefusal.Resolution("bridge-source-changed", "A captured portable input changed after review.");
}
