using System.Runtime.ExceptionServices;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Retains an access-bound Groundwork session for the lifetime of a document unit of work.
/// </summary>
public sealed class SessionUnitOfWork : IDocumentUnitOfWork
{
    private readonly IDocumentUnitOfWork _unitOfWork;
    private readonly GroundworkStoreSession _session;
    private int _disposed;

    public SessionUnitOfWork(
        IDocumentUnitOfWork unitOfWork,
        GroundworkStoreSession session)
    {
        _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
        _session = session ?? throw new ArgumentNullException(nameof(session));
    }

    public Task<DocumentStoreWriteResult> SaveAsync(
        SaveDocumentRequest request,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.SaveAsync(request, cancellationToken);

    public Task<DocumentStoreWriteResult> DeleteAsync(
        DeleteDocumentRequest request,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.DeleteAsync(request, cancellationToken);

    public Task<DocumentEnvelope?> LoadAsync(
        string documentKind,
        string id,
        CancellationToken cancellationToken = default) =>
        _unitOfWork.LoadAsync(documentKind, id, cancellationToken);

    public Task CommitAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.CommitAsync(cancellationToken);

    public Task RollbackAsync(CancellationToken cancellationToken = default) =>
        _unitOfWork.RollbackAsync(cancellationToken);

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        try
        {
            await _unitOfWork.DisposeAsync();
        }
        catch (Exception unitOfWorkFailure)
        {
            try
            {
                await _session.DisposeAsync();
            }
            catch (Exception sessionFailure)
            {
                GroundworkSessionFailure.Attach(unitOfWorkFailure, "SessionDisposalFailure", sessionFailure);
            }

            ExceptionDispatchInfo.Capture(unitOfWorkFailure).Throw();
        }

        await _session.DisposeAsync();
    }
}
