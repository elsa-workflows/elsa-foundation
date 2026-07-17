using Elsa.Secrets.Core.Models;

namespace Elsa.Secrets.Core.Contracts;

public interface ISecretRepository
{
    ValueTask<Secret?> FindAsync(string normalizedName, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<Secret>> ListAsync(CancellationToken cancellationToken = default);
    ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default);
    ValueTask SaveAsync(Secret secret, CancellationToken cancellationToken = default);
}

public interface IRevisionAwareSecretRepository
{
    ValueTask<SecretRevisionedRecord?> FindWithRevisionAsync(string normalizedName, CancellationToken cancellationToken = default);
    ValueTask<SecretRevisionSaveResult> SaveWithRevisionAsync(Secret secret, string? expectedRevision, CancellationToken cancellationToken = default);
}

public interface IPagedSecretRepository
{
    ValueTask<SecretRepositoryPage> ListPageAsync(SecretRepositoryListRequest request, CancellationToken cancellationToken = default);
}

public sealed record SecretRevisionedRecord(Secret Secret, string Revision);

public sealed record SecretRevisionSaveResult(SecretRevisionSaveStatus Status, string? Revision = null);

public enum SecretRevisionSaveStatus
{
    Saved,
    Conflict,
    NotFound
}

public sealed record SecretRepositoryListRequest(int Skip = 0, int Take = 50)
{
    public int NormalizedSkip => Math.Max(Skip, 0);
    public int NormalizedTake => Math.Clamp(Take, 1, 250);
}

public sealed record SecretRepositoryPage(IReadOnlyCollection<Secret> Items, long TotalCount);
