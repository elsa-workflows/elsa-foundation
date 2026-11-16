using Kernel.Durable;

namespace Kernel.Workflows;

/// <summary>
/// The stable key of one attempt of one activity's external effect: what an activity hands the outside world so that
/// the outside world can recognise a repeat (round 5 C). It is derived from committed facts only: the instance (its
/// creation included, so a recreation under the same id shares nothing with its predecessor), the model's frame (the
/// activation of the activity) and the attempt. Anything the kernel replays after a lost commit, a redelivery or a
/// takeover re-derives the same key as long as the model re-derives the same frame and attempt; a new attempt (a retry
/// after a definite failure, a successor frame) gets a new key.
///
/// The kernel promises no more than that. A side effect runs at least once (the design sketch's decision 1 and the
/// owner's defaults of 12 November); whether a repeat is dropped is the receiving side's business, with this key.
/// The key is plain ASCII built from the ids it names, so it fits an HTTP header value and the local part of an RFC 5322
/// Message-ID when those ids do (the prototype's do).
/// </summary>
public static class EffectKey
{
    public static string For(InstanceInfo instance, string frame, int attempt) => For(instance.Ref.Id, instance.Creation, frame, attempt);

    public static string For(string instance, long creation, string frame, int attempt) =>
        FormattableString.Invariant($"{CreationProtocol.Origin(instance, creation)}/{frame}/{attempt}");
}
