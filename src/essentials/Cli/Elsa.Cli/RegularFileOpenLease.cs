namespace Elsa.Cli;

/// <summary>Owns the directory identity retained while a regular-file stream is opened.</summary>
/// <remarks>
/// The callbacks make ownership explicit for native implementations and keep the lifetime
/// directly testable without exposing platform handles.
/// </remarks>
public sealed class RegularFileOpenLease : IDisposable
{
    private readonly Func<Stream> _openRead;
    private readonly Action _dispose;
    private readonly object _gate = new();
    private bool _opened;
    private bool _disposed;

    public RegularFileOpenLease(Func<Stream> openRead, Action dispose)
    {
        ArgumentNullException.ThrowIfNull(openRead);
        ArgumentNullException.ThrowIfNull(dispose);
        _openRead = openRead;
        _dispose = dispose;
    }

    /// <summary>Opens the retained regular file once, disposing the lease when opening fails.</summary>
    public Stream OpenRead()
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RegularFileOpenLease));
            if (_opened)
                throw new InvalidOperationException("A regular-file open lease can only open its stream once.");

            _opened = true;
            try
            {
                return _openRead() ?? throw new IOException();
            }
            catch
            {
                DisposeCore();
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
            DisposeCore();
    }

    private void DisposeCore()
    {
        if (_disposed)
            return;

        _disposed = true;
        _dispose();
    }
}
