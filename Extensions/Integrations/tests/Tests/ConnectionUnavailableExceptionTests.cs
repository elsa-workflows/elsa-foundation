using Elsa.Integrations.Sdk;

namespace Elsa.Integrations.Tests;

public sealed class ConnectionUnavailableExceptionTests
{
    [Fact]
    public void Fault_is_typed_and_not_retryable()
    {
        var exception = new ConnectionUnavailableException("missing", "demo");
        var fault = exception.ToFault();

        Assert.False(exception.IsRetryable);
        Assert.Equal(ConnectionUnavailableException.FaultCode, fault.Code);
        Assert.Equal(ConnectionUnavailableException.FaultTypeName, fault.FaultType);
        Assert.Equal(ConnectionUnavailableException.FaultCategory, fault.Category);
        Assert.False(fault.IsRetryable);
        Assert.Equal("missing", fault.Message);
    }
}
