namespace Elsa.Api.Capabilities.Exceptions;

public sealed class ApiCapabilityConflictException(string message) : InvalidOperationException(message);
