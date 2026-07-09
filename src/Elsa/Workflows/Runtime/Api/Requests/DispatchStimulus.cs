using System.Text.Json;
using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Runtime.Api.Models;

namespace Elsa.Workflows.Runtime.Api.Requests;

/// <summary>
/// Routes an external stimulus to workflows (W7): starts new instances from the published trigger index (E3-1)
/// and/or resumes every waiting instance across executions whose bookmark matches (E3-5), with no explicit
/// workflow execution id. The stimulus identity is the opaque <c>(StimulusType, StimulusHash)</c> routing pair
/// the engine already uses; this endpoint does not invent a second hashing scheme.
/// </summary>
/// <param name="StimulusType">The stimulus type (e.g. the event trigger's <c>Event</c> type).</param>
/// <param name="StimulusHash">The opaque stimulus hash the trigger index and bookmarks are keyed by.</param>
/// <param name="Input">Optional JSON payload delivered to resumed instances as their resume input, and to newly
/// started instances on the dedicated stimulus-input channel (spec 089 A) — never as a named workflow input.</param>
/// <param name="CorrelationId">Optional passive correlation value that scopes the resume fan-in (Condition B).</param>
/// <param name="Mode">Whether to start, resume, or both. Defaults to both.</param>
/// <param name="IdempotencyKey">
/// Optional at-least-once dedup key for the START path (Condition A). When present, a duplicate delivery does
/// not double-start; when absent, the start path is at-least-once and a duplicate MAY double-start.
/// </param>
public sealed record DispatchStimulus(
    string StimulusType,
    string StimulusHash,
    JsonElement? Input = null,
    string? CorrelationId = null,
    string? Mode = null,
    string? IdempotencyKey = null)
    : IRequest<DispatchStimulusResponse>;
