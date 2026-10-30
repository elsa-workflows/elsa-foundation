namespace Elsa.Activities.Runtime.Services;

/// <summary>A deterministic background contract/configuration failure, distinct from provider or ownership loss.</summary>
internal sealed class BackgroundActivityValidationException(string message) : InvalidOperationException(message);
