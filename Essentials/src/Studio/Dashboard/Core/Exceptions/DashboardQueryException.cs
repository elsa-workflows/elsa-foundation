namespace Elsa.Studio.Dashboard.Core.Exceptions;

/// <summary>A safe native query boundary failure; underlying infrastructure detail is retained only as its cause.</summary>
public sealed class DashboardQueryException(string code, string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string Code { get; } = code;
}
