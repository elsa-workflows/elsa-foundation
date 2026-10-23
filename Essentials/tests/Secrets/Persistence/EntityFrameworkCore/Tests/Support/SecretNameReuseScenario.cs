using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>
/// What every provider's migrated schema holds once a soft delete frees a name, deleting the way the manager does,
/// through the revision-aware path: the deleted secret keeps its row and every version beside the new secret that takes
/// the name, a writer still holding the deleted secret's revision cannot save over that successor, and the key itself,
/// not only the repository's check before it inserts, refuses a second row holding the name.
/// </summary>
internal static class SecretNameReuseScenario
{
    private const string TenantId = "tenant-reuse";
    private const string Name = "reused.name";

    /// <param name="context">A context on a database every Secrets migration has been applied to.</param>
    /// <param name="bypass">A second context on the same database, for the write that bypasses the repository.</param>
    public static async Task RunAsync(SecretsDbContext context, SecretsDbContext bypass)
    {
        var repository = new EfSecretRepository(context);
        var revisions = (IRevisionAwareSecretRepository)repository;
        var original = Secret("first", "second");
        Assert.True(await repository.TryAddAsync(original));
        var loaded = (await revisions.FindWithRevisionAsync(TenantId, Name))!;
        loaded.Secret.Status = SecretStatus.Deleted;
        Assert.Equal(SecretRevisionSaveStatus.Saved, (await revisions.SaveWithRevisionAsync(loaded.Secret, loaded.Revision)).Status);

        var successor = Secret("successor");
        Assert.True(await repository.TryAddAsync(successor));
        Assert.False(await repository.TryAddAsync(Secret("duplicate")));
        var holder = (await revisions.FindWithRevisionAsync(TenantId, Name))!;
        Assert.Equal(successor.Id, holder.Secret.Id);
        Assert.Equal("successor", Assert.Single(holder.Secret.Versions).Payload.Value);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, (await revisions.SaveWithRevisionAsync(loaded.Secret, loaded.Revision)).Status);
        var deleted = Assert.Single((await repository.ListPageAsync(
            TenantId,
            new SecretRepositoryListRequest(status: SecretStatus.Deleted))).Items);
        Assert.Equal(original.Id, deleted.Id);
        Assert.Equal(["first", "second"], deleted.Versions.Select(version => version.Payload.Value));

        bypass.Secrets.Add(SecretDocument.FromSecret(Secret("bypassing-the-repository")).ToRecord());
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => bypass.SaveChangesAsync());
        Assert.True(EfRelationalExceptionClassifier.IsUniqueConstraintViolation(refused));
        Assert.Equal(2, await context.Secrets.CountAsync(row => row.TenantId == TenantId && row.NormalizedName == Name));
    }

    private static Secret Secret(params string[] values) => new()
    {
        TenantId = TenantId,
        Name = Name,
        DisplayName = Name,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        Versions =
        [
            .. values.Select((value, index) => new SecretVersion
            {
                Version = index + 1,
                Status = SecretStatus.Active,
                Payload = SecretPayload.FromValue(value)
            })
        ]
    };
}
