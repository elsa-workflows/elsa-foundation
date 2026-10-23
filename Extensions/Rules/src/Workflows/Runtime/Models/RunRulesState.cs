using System.Text.Json;
using Elsa.Rules.Core;

namespace Elsa.Rules.Workflows.Runtime.Models;

/// <summary>A bounded frozen invocation request; Rules facts and rounds remain in the Rules store.</summary>
public sealed record RunRulesState(int FormatVersion, JsonElement Invocation);

/// <summary>A terminal Rules result or a deterministic native dispatch refusal.</summary>
public sealed record RunRulesCompletion(int FormatVersion, RuleNativeResultAddress Destination,
    string DeliveryId, JsonElement? Result, string? InvalidCode);

internal sealed record RunRulesRequest(RuleScope Scope, string RunId, RuleWorkflowProgramPin Program,
    RuleFact[] InitialFacts, int MaxRounds, string WorkflowExecutionId, string ActivityExecutionId,
    string ExecutableId, string ExecutableHash, string NodeId, string ResumeTargetId);

internal sealed record RulesNativeIntent(int FormatVersion, RunRulesRequest Request, RuleNativeResultAddress Destination);
