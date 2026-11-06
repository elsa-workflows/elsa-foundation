using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Jobs.Core;
using Microsoft.AspNetCore.DataProtection;

namespace Elsa.Jobs.Api.Services;

internal sealed class JobsPageCursors(IDataProtectionProvider protection)
{
    internal const int MaximumTokenLength = 16384;
    internal const int MaximumProviderTokenLength = 8192;

    internal JobApiPageCursor Bind(JobApiActor actor, int limit, string collection, params string?[] filters)
    {
        if (limit is < 1 or > 100) throw new ArgumentException("The page request is outside the supported bounds.");
        // Unambiguous framing keeps every trusted address and exact query filter in the purpose.
        // Data Protection's host-owned key ring encrypts and authenticates the provider's cursor.
        string?[] context = [actor.Scope.PersistencePartition, actor.Scope.TenantId, actor.SubjectId, collection,
            limit.ToString(CultureInfo.InvariantCulture), .. filters];
        var purpose = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(context))));
        return new(protection.CreateProtector("Elsa.Jobs.Api.Cursor.v1", purpose), limit);
    }
}

internal sealed class JobApiPageCursor(IDataProtector protector, int limit)
{
    internal JobPageRequest Request(string? after)
    {
        if (after is null) return new(limit, null);
        if (after.Length is < 1 or > JobsPageCursors.MaximumTokenLength) throw Invalid();
        try
        {
            var providerToken = protector.Unprotect(after);
            if (providerToken.Length is < 1 or > JobsPageCursors.MaximumProviderTokenLength) throw Invalid();
            return new(limit, providerToken);
        }
        catch (Exception error) when (error is CryptographicException or FormatException or ArgumentException)
        { throw Invalid(); }
    }

    internal string? Next(string? providerToken)
    {
        if (providerToken is null) return null;
        if (providerToken.Length is < 1 or > JobsPageCursors.MaximumProviderTokenLength) throw Unavailable();
        var token = protector.Protect(providerToken);
        return token.Length <= JobsPageCursors.MaximumTokenLength ? token : throw Unavailable();
    }

    private static ArgumentException Invalid() => new("The Jobs cursor is malformed or belongs to a different request context.");
    private static JobsApiException Unavailable() => new(503, "jobs.cursor.unavailable", "The Jobs continuation is unavailable.");
}
