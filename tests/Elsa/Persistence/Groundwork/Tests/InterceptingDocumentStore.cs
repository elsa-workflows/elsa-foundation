using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Tests;

internal sealed class InterceptingDocumentStore(IDocumentStore inner) : DelegatingDocumentStore(inner)
{
    public Func<SaveDocumentRequest, Task>? OnBeforeSave { get; set; }
    public Func<DeleteDocumentRequest, Task>? OnBeforeDelete { get; set; }
    public Func<DocumentCommitScope, Task>? OnBeforeBegin { get; set; }

    public override async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
    {
        await RunOnBeforeSaveAsync(request);
        return await base.SaveAsync(request, cancellationToken);
    }

    public override async Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
    {
        await RunOnBeforeDeleteAsync(request);
        return await base.DeleteAsync(request, cancellationToken);
    }

    public override async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default)
    {
        if (OnBeforeBegin is { } hook)
        {
            OnBeforeBegin = null;
            await hook(scope);
        }

        return new InterceptingDocumentUnitOfWork(this, await base.BeginAsync(scope, cancellationToken));
    }

    // Each hook is one-shot: it is cleared before it runs so a hook that itself writes cannot re-enter itself.
    private async Task RunOnBeforeSaveAsync(SaveDocumentRequest request)
    {
        if (OnBeforeSave is not { } hook)
            return;
        OnBeforeSave = null;
        await hook(request);
    }

    private async Task RunOnBeforeDeleteAsync(DeleteDocumentRequest request)
    {
        if (OnBeforeDelete is not { } hook)
            return;
        OnBeforeDelete = null;
        await hook(request);
    }

    private sealed class InterceptingDocumentUnitOfWork(
        InterceptingDocumentStore owner,
        IDocumentUnitOfWork innerUnitOfWork) : DelegatingDocumentUnitOfWork(innerUnitOfWork)
    {
        public override async Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            await owner.RunOnBeforeSaveAsync(request);
            return await base.SaveAsync(request, cancellationToken);
        }

        public override async Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
        {
            await owner.RunOnBeforeDeleteAsync(request);
            return await base.DeleteAsync(request, cancellationToken);
        }
    }
}
