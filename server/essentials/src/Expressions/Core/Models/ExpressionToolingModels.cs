namespace Elsa.Expressions.Core.Models;

/// <summary>Version negotiated by expression-tooling clients and providers.</summary>
public readonly record struct ExpressionToolingContractVersion(int Major, int Minor)
{
    public static readonly ExpressionToolingContractVersion V1 = new(1, 0);
    public bool IsCompatibleWith(ExpressionToolingContractVersion supported) => Major == supported.Major && Minor <= supported.Minor;
    public override string ToString() => $"{Major}.{Minor}";
}

public enum ExpressionToolingOutcomeState
{
    Success,
    SupportedEmpty,
    Unavailable,
    Unauthorized,
    Incompatible,
    Stale,
    Canceled
}

/// <summary>
/// Revision-bound, safe result envelope. Non-success outcomes intentionally never contain partial data.
/// </summary>
public sealed record ExpressionToolingOutcome<T>(
    ExpressionToolingOutcomeState State,
    ExpressionToolingContractVersion ContractVersion,
    string? DocumentRevision = null,
    string? ContextRevision = null,
    T? Payload = default,
    string? Code = null,
    string? Message = null)
{
    public bool IsSuccess => State is ExpressionToolingOutcomeState.Success or ExpressionToolingOutcomeState.SupportedEmpty;

    public static ExpressionToolingOutcome<T> Success(T payload, ExpressionToolingContractVersion version, string documentRevision, string contextRevision) =>
        new(ExpressionToolingOutcomeState.Success, version, documentRevision, contextRevision, payload);

    public static ExpressionToolingOutcome<T> SupportedEmpty(T payload, ExpressionToolingContractVersion version, string documentRevision, string contextRevision) =>
        new(ExpressionToolingOutcomeState.SupportedEmpty, version, documentRevision, contextRevision, payload);

    public static ExpressionToolingOutcome<T> Failure(ExpressionToolingOutcomeState state, ExpressionToolingContractVersion version, string? code = null, string? message = null, string? documentRevision = null, string? contextRevision = null)
    {
        if (state is ExpressionToolingOutcomeState.Success or ExpressionToolingOutcomeState.SupportedEmpty)
            throw new ArgumentOutOfRangeException(nameof(state), "Use a success factory for a payload-bearing outcome.");

        return new(state, version, documentRevision, contextRevision, default, code, message);
    }
}

public sealed record ExpressionAuthoringDocument(
    string DocumentId,
    string WorkflowDraftId,
    string NodeId,
    string PropertyKey,
    string ExpressionType,
    string DocumentRevision,
    string? ExpectedResultType = null,
    string? Source = null);

public readonly record struct ExpressionToolingPosition(int Line, int Character)
{
    public bool IsValid => Line >= 0 && Character >= 0;
}

public readonly record struct ExpressionToolingRange(ExpressionToolingPosition Start, ExpressionToolingPosition End);

public enum ExpressionValueKind { Unknown, Scalar, Object, Array, Map, Function, Reference }

public sealed record ExpressionValueMember(
    string Name,
    ExpressionValueShape Shape,
    string? Documentation = null);

public sealed record ExpressionValueShape(
    string? DisplayName = null,
    ExpressionValueKind Kind = ExpressionValueKind.Unknown,
    bool IsNullable = true,
    bool HasLazyChildren = false,
    ExpressionValueShape? Item = null,
    ExpressionValueShape? Key = null,
    ExpressionValueShape? Value = null,
    IReadOnlyList<ExpressionValueMember>? Members = null);

public enum ExpressionSymbolKind { Variable, WorkflowInput, ActivityResult, Function, Filter, Tag, Namespace, Member, Extension }

public sealed record ExpressionCallableSignature(string Display, IReadOnlyList<string>? Parameters = null, ExpressionValueShape? ReturnShape = null);

/// <summary>Permission-filtered metadata only. It never carries a value or an executable delegate.</summary>
public sealed record ExpressionSymbol(
    string SymbolId,
    string Name,
    ExpressionSymbolKind Kind,
    ExpressionValueShape? ValueShape = null,
    string? Documentation = null,
    IReadOnlyList<ExpressionCallableSignature>? Signatures = null,
    string? ChildrenLink = null);

public sealed record ExpressionToolingCapabilities(
    bool SupportsCompletions = true,
    bool SupportsHover = true,
    bool SupportsValidation = true,
    bool SupportsSymbolPaging = true,
    bool SupportsLazyMembers = false,
    int MaximumSymbols = 500);

/// <summary>Design-built context supplied to providers; the source is separate and never persisted here.</summary>
public sealed record ExpressionAuthoringContext(
    ExpressionToolingContractVersion ContractVersion,
    ExpressionAuthoringDocument Document,
    string ContextRevision,
    string SymbolCatalogRevision,
    IReadOnlyList<ExpressionSymbol> RootSymbols,
    ExpressionToolingCapabilities Capabilities,
    string? ExpectedResultType = null,
    ExpressionValueShape? ExpectedResultShape = null,
    string? PolicyFingerprint = null,
    string? PermissionRevision = null);

public sealed record ExpressionToolingRequestScope(
    ExpressionToolingContractVersion ContractVersion,
    ExpressionAuthoringDocument Document,
    ExpressionAuthoringContext Context);

public sealed record ExpressionCompletionRequest(ExpressionToolingRequestScope Scope, string Source, ExpressionToolingPosition Cursor);
public sealed record ExpressionHoverRequest(ExpressionToolingRequestScope Scope, string Source, ExpressionToolingPosition Position);
public sealed record ExpressionValidationRequest(ExpressionToolingRequestScope Scope, string Source);

public sealed record ExpressionToolingItem(string Label, string? Detail = null, string? Documentation = null, string? InsertText = null, ExpressionSymbolKind? Kind = null);
public sealed record ExpressionToolingItems(IReadOnlyList<ExpressionToolingItem> Items);
public sealed record ExpressionHover(string Contents, ExpressionToolingRange? Range = null);

public enum ExpressionDiagnosticSeverity { Error, Warning, Information, Hint }
public sealed record ExpressionDiagnostic(
    string Code,
    ExpressionDiagnosticSeverity Severity,
    string Message,
    string DocumentRevision,
    ExpressionToolingRange? Range = null,
    string? AuthoredPath = null,
    IReadOnlyList<string>? RelatedSymbols = null);
public sealed record ExpressionDiagnosticSet(IReadOnlyList<ExpressionDiagnostic> Diagnostics);
