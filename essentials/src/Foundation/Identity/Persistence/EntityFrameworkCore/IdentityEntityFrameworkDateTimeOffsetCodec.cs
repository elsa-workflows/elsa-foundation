using System.Globalization;

namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

/// <summary>
/// Keeps the complete <see cref="DateTimeOffset"/> value provider-neutral. Relational timestamp
/// types do not agree on whether the original offset is retained, so credentials use an invariant
/// round-trip string rather than allowing a provider to silently normalize it.
/// </summary>
internal static class IdentityEntityFrameworkDateTimeOffsetCodec
{
    public static string? Encode(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture);

    public static DateTimeOffset? Decode(string? value) =>
        value is null
            ? null
            : DateTimeOffset.ParseExact(value, "O", CultureInfo.InvariantCulture, DateTimeStyles.None);
}
