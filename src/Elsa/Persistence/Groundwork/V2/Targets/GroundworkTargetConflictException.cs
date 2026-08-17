namespace Elsa.Persistence.Groundwork.Targets;

/// <summary>Raised when Groundwork target declarations cannot form one coherent host composition.</summary>
public sealed class GroundworkTargetConflictException(string message) : InvalidOperationException(message);
