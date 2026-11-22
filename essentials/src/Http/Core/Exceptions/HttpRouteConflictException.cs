namespace Elsa.Http.Core.Exceptions;

/// <summary>Raised when a candidate route manifest contains an ambiguous owner/method pair.</summary>
public sealed class HttpRouteConflictException : InvalidOperationException
{
    public HttpRouteConflictException(
        string firstRoute,
        string secondRoute,
        string overlappingMethod,
        string firstOwner,
        string secondOwner)
        : base($"HTTP route conflict for method '{overlappingMethod}' between '{firstRoute}' owned by {firstOwner} and '{secondRoute}' owned by {secondOwner}.")
    {
        FirstRoute = firstRoute;
        SecondRoute = secondRoute;
        OverlappingMethod = overlappingMethod;
        FirstOwner = firstOwner;
        SecondOwner = secondOwner;
    }

    public string FirstRoute { get; }
    public string SecondRoute { get; }
    public string OverlappingMethod { get; }
    public string FirstOwner { get; }
    public string SecondOwner { get; }
}
