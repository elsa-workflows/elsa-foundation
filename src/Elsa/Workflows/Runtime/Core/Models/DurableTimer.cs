using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// A durable, persisted intent to resume a suspended workflow execution at a wall-clock deadline.
/// </summary>
/// <remarks>
/// <para>
/// A timer is written by a suspending activity (e.g. <c>Delay</c>) strictly before the matching bookmark
/// is created, so the "bookmark committed but timer missing" hang is structurally excluded. The hosted
/// timer pump loads due timers and fires the bookmark identified by
/// (<see cref="StimulusType"/>, <see cref="StimulusHash"/>) through the existing bookmark-resume dispatcher.
/// </para>
/// <para>
/// The identity is (<see cref="WorkflowExecutionId"/>, <see cref="TimerId"/>). <see cref="TimerId"/> is
/// derived deterministically from the replay-stable activity execution id, so a crash-replay re-invoke of
/// the same activity execution upserts the same timer rather than creating a duplicate.
/// </para>
/// </remarks>
public sealed record DurableTimer(
    string TimerId,
    string WorkflowExecutionId,
    string StimulusType,
    string StimulusHash,
    DateTimeOffset DueTime,
    DateTimeOffset CreatedAt,
    JsonElement? Input = null,
    IReadOnlyDictionary<string, string>? Metadata = null);
