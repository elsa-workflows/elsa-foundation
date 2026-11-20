namespace Elsa.Http.Core.Exceptions;

/// <summary>
/// Thrown by an <c>IHttpEndpointAuthorizationHandler</c> when authorization cannot be evaluated because of a host
/// misconfiguration (e.g. no authentication scheme is registered for the shell), as opposed to the caller simply
/// being unauthorized. The middleware maps this to <c>500</c> — a configuration fault the operator must fix —
/// rather than <c>401</c>, which would mislead an operator into thinking the endpoint is behaving correctly
/// (#592 item 11).
/// </summary>
public sealed class HttpEndpointAuthorizationConfigurationException(string message, Exception innerException)
    : Exception(message, innerException);
