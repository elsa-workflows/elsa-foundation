using System.Text.Json;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Exceptions;
using Elsa.Secrets.Core.Models;

namespace Elsa.Connections.Services;

/// <summary>
/// Writes grant material as a new secret version only when the current version still matches
/// <see cref="GrantWriteRequest.ExpectedSecretVersion"/>. A mismatch is <see cref="GrantWriteStatus.Conflict"/>.
/// </summary>
internal sealed class DefaultConnectionGrantWriter(ISecretManager secretManager, ISecretNameValidator names) : IConnectionGrantWriter
{
    public async ValueTask<GrantWriteResult> TryCompareAndSwapAsync(GrantWriteRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var normalized = names.Normalize(request.GrantSecretName);
        var current = await secretManager.FindAsync(request.TenantId, normalized, cancellationToken);
        if (current is null)
            return new GrantWriteResult(GrantWriteStatus.Missing, CurrentVersion: null);

        if (current.CurrentVersion != request.ExpectedSecretVersion)
            return new GrantWriteResult(GrantWriteStatus.Conflict, current.CurrentVersion);

        var payload = JsonSerializer.Serialize(new Dictionary<string, string?>
        {
            ["access_token"] = request.AccessToken,
            ["refresh_token"] = request.RefreshToken,
            ["expires_at"] = request.ExpiresAt?.ToString("O"),
            ["token_type"] = request.TokenType,
            ["scope"] = request.Scope
        });

        try
        {
            var rotated = await secretManager.RotateAsync(request.TenantId, normalized, new RotateSecretRequest
            {
                Value = payload,
                ExpectedVersion = request.ExpectedSecretVersion
            }, cancellationToken);

            return new GrantWriteResult(GrantWriteStatus.Written, rotated.CurrentVersion);
        }
        catch (SecretVersionConflictException conflict)
        {
            return new GrantWriteResult(GrantWriteStatus.Conflict, conflict.CurrentVersion);
        }
    }
}
