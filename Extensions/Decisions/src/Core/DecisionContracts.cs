using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Decisions.Core;

/// <summary>Compiles the explicitly supported decision-table profile without storing or publishing it.</summary>
public interface IDecisionCompiler
{
    DecisionCompilation Compile(string xml, CancellationToken cancellationToken = default);
}

/// <summary>Evaluates frozen, verified content without external effects or mutable design reads.</summary>
public interface IDecisionEvaluator
{
    DecisionEvaluation Evaluate(DecisionArtifact artifact, IReadOnlyDictionary<string, DecisionValue> inputs,
        int operationBudget = 1_000_000, CancellationToken cancellationToken = default);
}

/// <summary>A tagged scalar. Numbers are exact invariant decimal text; booleans are lowercase text.</summary>
public sealed record DecisionValue([property: JsonPropertyName("kind")] string Kind, [property: JsonPropertyName("value")] string? Value);

/// <summary>Safe diagnostics contain structural locations and codes, never business-value fragments.</summary>
public sealed record DecisionDiagnostic(string Code, string Message, string? DecisionId = null, string? RuleId = null, string? ClauseId = null);

/// <summary>Portable, untrusted-until-verified frozen content. The evaluator validates every identity and byte.</summary>
public sealed class DecisionArtifact
{
    private readonly byte[] executableBytes;

    [JsonConstructor]
    public DecisionArtifact(string decisionId, string sourceHash, string contentHash, string profileId, string semanticsId, byte[] executableBytes)
    {
        DecisionId = decisionId;
        SourceHash = sourceHash;
        ContentHash = contentHash;
        ProfileId = profileId;
        SemanticsId = semanticsId;
        this.executableBytes = executableBytes?.ToArray() ?? [];
    }

    public string DecisionId { get; }
    public string SourceHash { get; }
    public string ContentHash { get; }
    public string ProfileId { get; }
    public string SemanticsId { get; }
    public byte[] ExecutableBytes => executableBytes.ToArray();
}

public sealed class DecisionCompilation
{
    public DecisionCompilation(DecisionArtifact? artifact, IReadOnlyList<DecisionDiagnostic> diagnostics)
    {
        Artifact = artifact;
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
    }
    public DecisionArtifact? Artifact { get; }
    public IReadOnlyList<DecisionDiagnostic> Diagnostics { get; }
    public bool Success => Artifact is not null && Diagnostics.Count == 0;
}

public sealed record DecisionCellTrace(string InputId, string Truth);

public sealed class DecisionRuleTrace
{
    public DecisionRuleTrace(string ruleId, bool matched, bool selected, IReadOnlyList<DecisionCellTrace> cells)
    {
        RuleId = ruleId;
        Matched = matched;
        Selected = selected;
        Cells = Array.AsReadOnly(cells.ToArray());
    }
    public string RuleId { get; }
    public bool Matched { get; }
    public bool Selected { get; }
    public IReadOnlyList<DecisionCellTrace> Cells { get; }
}

public sealed class DecisionEvaluation
{
    public DecisionEvaluation(string status, string decisionId, string contentHash, string profileId, string semanticsId,
        JsonElement result, IReadOnlyList<DecisionRuleTrace> trace, IReadOnlyList<DecisionDiagnostic> diagnostics, int operations)
    {
        Status = status;
        DecisionId = decisionId;
        ContentHash = contentHash;
        ProfileId = profileId;
        SemanticsId = semanticsId;
        Result = result.ValueKind == JsonValueKind.Undefined ? JsonSerializer.SerializeToElement<object?>(null) : result.Clone();
        Trace = Array.AsReadOnly(trace.ToArray());
        Diagnostics = Array.AsReadOnly(diagnostics.ToArray());
        Operations = operations;
    }
    public string Status { get; }
    public string DecisionId { get; }
    public string ContentHash { get; }
    public string ProfileId { get; }
    public string SemanticsId { get; }
    public JsonElement Result { get; }
    public IReadOnlyList<DecisionRuleTrace> Trace { get; }
    public IReadOnlyList<DecisionDiagnostic> Diagnostics { get; }
    public int Operations { get; }
}
