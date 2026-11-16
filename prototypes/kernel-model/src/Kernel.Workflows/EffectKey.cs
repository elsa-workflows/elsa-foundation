using Kernel.Durable;

namespace Kernel.Workflows;

/// <summary>
/// The stable key of one attempt of one activity's external effect: what an activity hands the outside world so that
/// the outside world can recognise a repeat (round 5 C). Its shape is the owner's decision of 16 November:
/// <c>{instance origin}/{node}/{iteration path}/{attempt}</c>, from committed facts only.
///
/// - The instance origin is the instance's id with its creation (a recreation under the same id shares nothing with
///   its predecessor). The id is fixed at creation; the key never reads partitions or positions.
/// - The node is the activity's stable id in the workflow definition, the one the model already uses.
/// - The iteration path says which activation of that node this is within the instance: the iteration numbers of the
///   loops and other repeating scopes that enclose it, root first (a ForEach index, a While pass, a Flowchart round,
///   the merge and source a Flowchart token passed, a Flowchart join's firing ordinal in its round, a StateMachine
///   visit serial, a retry generation), each derived from the enclosing activity's committed state. Two activations of
///   one node always differ in it; one logical activation keeps it whatever a replay does to the agenda, and what it
///   carries as content may then differ from its first sending: the receiver keeps the first.
/// - The attempt is 0, or 1 for an External activity in Durable mode, whose attempt is committed before it runs.
///
/// Why no frame number (the owner's reason): in Balanced and Ephemeral mode a replayed burst mints its frame numbers
/// from the committed state in agenda order, and a replay over a longer inbox (a timer that became due meanwhile)
/// orders the agenda differently, so the same frame number can land on another activity, or on another iteration of
/// the same one. A key with the frame in it then names two different effects, and a receiver that deduplicates by it
/// drops a genuine effect as a repeat. The iteration path is independent of agenda order, so the same logical
/// activation re-derives the same key on whichever owner replays it, and a different one never shares it. No extra
/// commit is paid for this.
///
/// The kernel promises no more than that. A side effect runs at least once (the design sketch's decision 1 and the
/// owner's defaults of 12 November); whether a repeat is dropped is the receiving side's business, with this key.
/// The key is plain ASCII built from the ids it names, so it fits an HTTP header value and the local part of an RFC 5322
/// Message-ID when those ids do (the prototype's do).
/// </summary>
public static class EffectKey
{
    /// <summary>The iteration path of an activation that no repeating scope encloses.</summary>
    public const string RootPath = "-";

    public static string For(InstanceInfo instance, string node, string iterationPath, int attempt) => For(instance.Ref.Id, instance.Creation, node, iterationPath, attempt);

    public static string For(string instance, long creation, string node, string iterationPath, int attempt) =>
        FormattableString.Invariant($"{CreationProtocol.Origin(instance, creation)}/{node}/{(iterationPath.Length == 0 ? RootPath : iterationPath)}/{attempt}");
}
