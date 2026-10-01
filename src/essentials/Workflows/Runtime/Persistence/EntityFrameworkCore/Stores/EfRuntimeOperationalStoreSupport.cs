using System.Text;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

internal static class EfRuntimeOperationalStoreSupport
{
    public static string RequireScope(IPersistenceAccessContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        var scope = accessor.Current.RequireScope().Value;
        if (scope.Length > 256)
            throw new ArgumentException("Runtime persistence scope cannot exceed 256 UTF-16 code units.", nameof(accessor));
        return scope;
    }

    public static void ValidateIdentity(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length > RuntimeOperationalStateEfModule.IdentityMaximumLength)
            throw new ArgumentException($"Runtime identity cannot exceed {RuntimeOperationalStateEfModule.IdentityMaximumLength} UTF-16 code units.", parameterName);
    }

    public static string Encode(string value) => EfRelationalIdentity.Encode(value);
    public static string Decode(string value) => EfRelationalIdentity.Decode(value);
    public static string Hash(string value) => EfRelationalIdentity.Hash(value);
    public static string Order(string value) => Convert.ToHexString(EfRelationalIdentity.CreateOrderKey(value, RuntimeOperationalStateEfModule.IdentityMaximumLength));

    /// <summary>
    /// An order key over the first <see cref="RuntimeOperationalStateEfModule.IdentityMaximumLength"/> code
    /// units of a composed identity. The runtime composes work-item and commit identities well past that, and
    /// a full-width key would exceed the index-key budget of SQL Server and MySQL. Every query that orders by
    /// such a key carries the identity hash as its tie-break, so paging stays total and stable.
    /// </summary>
    public static string OrderPrefix(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return Order(value.Length <= RuntimeOperationalStateEfModule.IdentityMaximumLength
            ? value
            : value[..RuntimeOperationalStateEfModule.IdentityMaximumLength]);
    }
    public static string CompositeId(string scope, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(values);

        return EfRelationalIdentity.HashLengthFramed([scope, .. values]);
    }

    /// <summary>The offset of <paramref name="value"/> in whole minutes, as the <c>*OffsetMinutes</c> columns store it.</summary>
    public static int OffsetMinutes(DateTimeOffset value) => checked((int)value.Offset.TotalMinutes);

    /// <summary>Rebuilds an instant from its <c>*UtcTicks</c> and <c>*OffsetMinutes</c> columns.</summary>
    public static DateTimeOffset FromUtcTicks(long utcTicks, int offsetMinutes) =>
        new DateTimeOffset(new DateTime(utcTicks, DateTimeKind.Utc)).ToOffset(TimeSpan.FromMinutes(offsetMinutes));

    /// <summary>
    /// An optional instant as its <c>*UtcTicks</c> and <c>*OffsetMinutes</c> column pair, both null when it is absent, so a
    /// claim column pair is always written together: <c>(row.VisibleAfterUtcTicks, row.VisibleAfterOffsetMinutes) = TimestampColumns(value)</c>.
    /// </summary>
    public static (long? UtcTicks, int? OffsetMinutes) TimestampColumns(DateTimeOffset? value) =>
        value is { } instant ? (instant.UtcTicks, OffsetMinutes(instant)) : (null, null);

    public static void EnsureScope(string actual, string expected) {
        if (!StringComparer.Ordinal.Equals(actual, expected))
            throw new InvalidDataException("The persisted runtime row belongs to another persistence scope.");
    }

    public static string Cursor(IRuntimeRecoveryContinuationCodec codec, string purpose, string scope, string workflow, string last)
    {
        var payload = Encoding.UTF8.GetBytes(string.Join("\u001f", Encode(scope), Encode(workflow), Encode(last)));
        return codec.Encode(purpose, payload);
    }

    public static (string Scope, string Workflow, string Last) DecodeCursor(IRuntimeRecoveryContinuationCodec codec, string purpose, string token)
    {
        try
        {
            var values = Encoding.UTF8.GetString(codec.Decode(purpose, token)).Split('\u001f');
            if (values.Length != 3)
                throw new FormatException();
            return (EfRelationalIdentity.Decode(values[0]), EfRelationalIdentity.Decode(values[1]), EfRelationalIdentity.Decode(values[2]));
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException or InvalidOperationException)
        {
            throw new ArgumentException("The runtime operational-state continuation is invalid.", nameof(token), exception);
        }
    }

    public static void ValidateCursor(RuntimeStorePageRequest request, string scope, string workflow, (string Scope, string Workflow, string Last) cursor)
    {
        if (!StringComparer.Ordinal.Equals(cursor.Scope, scope) || !StringComparer.Ordinal.Equals(cursor.Workflow, workflow))
            throw new ArgumentException("The runtime operational-state continuation belongs to another query.", nameof(request));
        ValidateIdentity(cursor.Last, nameof(request));
    }
}
