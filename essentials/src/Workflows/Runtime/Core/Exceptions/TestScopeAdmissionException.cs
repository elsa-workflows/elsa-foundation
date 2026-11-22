namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Signals that authoritative test-scope state rejected root or child admission.</summary>
public sealed class TestScopeAdmissionException(string message) : InvalidOperationException(message);
