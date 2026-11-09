namespace Elsa.Studio.Core.Models;

/// <summary>An explicitly public, detached module configuration value under an owned namespace.</summary>
public sealed record StudioRuntimeConfigurationEntry(string Namespace, object? Value);
