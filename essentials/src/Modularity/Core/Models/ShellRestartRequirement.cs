namespace Elsa.Modularity.Core.Models;

/// <summary>
/// A shell feature keeps process-owned state and requires the host to restart before replacing that shell generation.
/// </summary>
public sealed record ShellRestartRequirement(string FeatureName);
