using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Services;

/// <summary>Normalizes typed owner fields at their actual callback boundary; opaque domain JSON stays with its owner.</summary>
internal static class WeaverModuleCodes
{
    internal const string UnsupportedCode = "module.code.unsupported";
    internal const string UnavailableCode = "module.owner.unavailable";
    internal const string UnsupportedMessage = "The module returned an unsupported outcome code.";

    internal static void Validate(IReadOnlyCollection<string>? codes)
    {
        if (codes is null) return;
        var unique = new HashSet<string>(StringComparer.Ordinal);
        if (codes.Any(code => !ValidCode(code) || !unique.Add(code)))
            throw new WeaverContributionException("Module codes must be unique bounded dotted ASCII identifiers outside the reserved engine namespace.");
    }

    internal static WeaverError Error(WeaverError value, IReadOnlyCollection<string>? codes, bool ownerOrigin)
        => Error(value, codes, ownerOrigin, out _);

    internal static WeaverError Error(WeaverError value, IReadOnlyCollection<string>? codes, bool ownerOrigin, out bool replaced)
    {
        var unavailable = ownerOrigin && value.OwnerUnavailable && (codes is not null || LegacyOwnerCode(value.Code));
        var code = Code(value.Code, codes, unavailable, out replaced);
        var normalized = value with
        {
            Code = code,
            Message = unavailable ? WeaverToolAccess.OwnerUnavailableMessage : replaced || value.OwnerUnavailable && !ownerOrigin ? UnsupportedMessage : value.Message,
            OwnerUnavailable = unavailable,
            // Limit belongs to the engine's disclosure protocol, not to an owner's arbitrary metadata.
            Limit = codes is null || value.Limit is null or "snapshots" or "bytes" or "snapshot-too-large" or "snapshot-does-not-fit"
                ? value.Limit : null
        };
        replaced |= normalized.Message != value.Message || normalized.OwnerUnavailable != value.OwnerUnavailable || normalized.Limit != value.Limit;
        return normalized == value ? value : normalized;
    }

    internal static WeaverViolation Violation(WeaverViolation value, IReadOnlyCollection<string>? codes, bool ownerOrigin)
    {
        var unavailable = ownerOrigin && value.OwnerUnavailable && (codes is not null || LegacyOwnerCode(value.Code));
        var code = Code(value.Code, codes, unavailable, out var replaced);
        return value with
        {
            Code = code,
            Message = unavailable ? WeaverToolAccess.OwnerUnavailableMessage : replaced || value.OwnerUnavailable && !ownerOrigin ? UnsupportedMessage : value.Message,
            OwnerUnavailable = unavailable
        };
    }

    internal static WeaverContributionDiagnostic Diagnostic(WeaverContributionDiagnostic value, IReadOnlyCollection<string>? codes, bool ownerOrigin)
    {
        var unavailable = ownerOrigin && value.OwnerUnavailable && (codes is not null || LegacyOwnerCode(value.Code));
        var code = Code(value.Code, codes, unavailable, out var replaced);
        return value with
        {
            Code = code,
            Message = unavailable ? WeaverToolAccess.OwnerUnavailableMessage : replaced || value.OwnerUnavailable && !ownerOrigin ? UnsupportedMessage : value.Message,
            OwnerUnavailable = unavailable
        };
    }

    internal static (WeaverToolResult Value, bool Replaced) Result(WeaverToolResult value, IReadOnlyCollection<string>? codes, bool ownerOrigin = false)
    {
        Validate(codes);
        var result = value;
        var changed = false;
        if (value.Error is { } error)
        {
            var normalized = Error(error, codes, ownerOrigin, out changed);
            result = result with { Error = normalized, Content = normalized.Message };
            changed |= result.Content != value.Content;
        }
        if (value.Receipt is not { ModuleCode: { } original } receipt) return (result, changed);
        var code = Code(original, codes, false, out var replaced);
        return !replaced ? (result, changed)
            : (result with { Content = UnsupportedMessage, Receipt = receipt with { ModuleCode = code } }, true);
    }

    private static string Code(string value, IReadOnlyCollection<string>? codes, bool unavailable, out bool replaced)
    {
        Validate(codes);
        // Null is the explicit undeclared compatibility path; [] is a declared set accepting no code.
        replaced = codes is not null && !(ValidCode(value) && codes.Contains(value, StringComparer.Ordinal));
        if (!replaced) return value;
        return unavailable ? UnavailableCode : UnsupportedCode;
    }

    private static bool ValidCode(string? code) => LegacyOwnerCode(code) && code![0] != '.' && code[^1] != '.'
        && !code.Contains("..", StringComparison.Ordinal);

    internal static bool LegacyOwnerCode(string? code) => code is { Length: > 0 and <= 128 } && code.Contains('.')
        && !code.StartsWith("weaver.", StringComparison.OrdinalIgnoreCase)
        && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
