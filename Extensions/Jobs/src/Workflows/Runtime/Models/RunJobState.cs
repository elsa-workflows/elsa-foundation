using System.Text.Json;
using Elsa.Jobs.Core;

namespace Elsa.Jobs.Workflows.Runtime.Models;

/// <summary>One materialized invocation seed, retained across fresh native resume activations.</summary>
public sealed record RunJobState(int FormatVersion, JsonElement Invocation);

/// <summary>Exact selected job result and previously granted model continuation; neither document grants authority itself.</summary>
public sealed record RunJobCompletion(int FormatVersion, string JobId, string RequestSha256,
    string ResultId, string ContinuationId, string ModelInputId, JsonElement Result, JsonElement Permission);

/// <summary>Useful selected result returned by the native activity without rerunning a handler.</summary>
public sealed record RunJobResult(int FormatVersion, string JobId, string ResultId,
    string ContinuationId, string ModelInputId, JobOutcome Outcome, JobPayload? Value,
    string ResultSha256, JobDiagnostic? Diagnostic);
