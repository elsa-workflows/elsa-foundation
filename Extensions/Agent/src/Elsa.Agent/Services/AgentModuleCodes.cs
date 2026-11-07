using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Services;

/// <summary>Normalizes typed owner fields at their actual callback boundary; opaque domain JSON stays with its owner.</summary>
internal static class AgentModuleCodes
{
    internal const string UnsupportedCode = "module.code.unsupported";
    internal const string UnavailableCode = "module.owner.unavailable";
    internal const string UnsupportedMessage = "The module returned an unsupported outcome code.";

    internal static void Validate(IReadOnlyCollection<string>? codes)
    {
        if (codes is null) return;
        var unique = new HashSet<string>(StringComparer.Ordinal);
        if (codes.Any(code => !ValidCode(code) || !unique.Add(code)))
            throw new AgentContributionException("Module codes must be unique bounded dotted ASCII identifiers outside the reserved engine namespace.");
    }

    internal static AgentError Error(AgentError value, IReadOnlyCollection<string>? codes, bool ownerOrigin)
        => Error(value, codes, ownerOrigin, out _);

    internal static AgentError Error(AgentError value, IReadOnlyCollection<string>? codes, bool ownerOrigin, out bool replaced)
    {
        var unavailable = ownerOrigin && value.OwnerUnavailable && (codes is not null || LegacyOwnerCode(value.Code));
        var code = Code(value.Code, codes, unavailable, out replaced);
        var normalized = value with
        {
            Code = code,
            Message = unavailable ? AgentToolAccess.OwnerUnavailableMessage : replaced || value.OwnerUnavailable && !ownerOrigin ? UnsupportedMessage : value.Message,
            OwnerUnavailable = unavailable
        };
        replaced |= normalized.Message != value.Message || normalized.OwnerUnavailable != value.OwnerUnavailable;
        return normalized == value ? value : normalized;
    }

    internal static AgentViolation Violation(AgentViolation value, IReadOnlyCollection<string>? codes, bool ownerOrigin)
    {
        var unavailable = ownerOrigin && value.OwnerUnavailable && (codes is not null || LegacyOwnerCode(value.Code));
        var code = Code(value.Code, codes, unavailable, out var replaced);
        return value with
        {
            Code = code,
            Message = unavailable ? AgentToolAccess.OwnerUnavailableMessage : replaced || value.OwnerUnavailable && !ownerOrigin ? UnsupportedMessage : value.Message,
            OwnerUnavailable = unavailable
        };
    }

    internal static AgentContributionDiagnostic Diagnostic(AgentContributionDiagnostic value, IReadOnlyCollection<string>? codes, bool ownerOrigin)
    {
        var unavailable = ownerOrigin && value.OwnerUnavailable && (codes is not null || LegacyOwnerCode(value.Code));
        var code = Code(value.Code, codes, unavailable, out var replaced);
        return value with
        {
            Code = code,
            Message = unavailable ? AgentToolAccess.OwnerUnavailableMessage : replaced || value.OwnerUnavailable && !ownerOrigin ? UnsupportedMessage : value.Message,
            OwnerUnavailable = unavailable
        };
    }

    internal static (AgentToolResult Value, bool Replaced) Result(AgentToolResult value, IReadOnlyCollection<string>? codes)
    {
        Validate(codes);
        if (value.Receipt is not { ModuleCode: { } original } receipt) return (value, false);
        var code = Code(original, codes, false, out var replaced);
        return !replaced ? (value, false)
            : (value with { Content = UnsupportedMessage, Receipt = receipt with { ModuleCode = code } }, true);
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
        && !code.StartsWith("agent.", StringComparison.OrdinalIgnoreCase)
        && code.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-');
}
