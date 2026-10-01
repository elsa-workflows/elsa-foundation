namespace Elsa.Foundation.DataProtection;

/// <summary>A host's Data Protection settings cannot be composed as configured; the message names the key.</summary>
public sealed class DataProtectionConfigurationException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
