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
        var current = accessor.Current;
        if (current.AccessPolicy != PersistenceAccessPolicy.Ordinary || current.Scope is null || current.AcrossScopes)
            throw new InvalidOperationException("Runtime EF persistence requires one explicit ordinary persistence scope.");
        var scope = current.Scope.Value;
        if (string.IsNullOrWhiteSpace(scope) || scope.Length > 256)
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
    public static string CompositeId(string scope, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);
        ArgumentNullException.ThrowIfNull(values);

        var identity = new StringBuilder();
        AppendLengthFramed(identity, scope);
        foreach (var value in values)
            AppendLengthFramed(identity, value);

        return Hash(identity.ToString());
    }

    private static void AppendLengthFramed(StringBuilder target, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        target.Append(value.Length).Append(':').Append(value);
    }

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
