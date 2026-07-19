namespace Elsa.Primitives.Exceptions;

public class ServiceUnavailableException(string message) : InvalidOperationException(message);
