namespace Kernel.Foundation.Contracts;

/// <summary>The committed ordinary Actor checkpoint used by a qualified workflow user task journal.</summary>
public sealed record CommittedActorCheckpoint(string? State, ActorStatus Status, bool Parked);
