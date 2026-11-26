namespace Elsa.Studio.Hosting;

/// <summary>A Studio hosting composition or lifecycle operation failed.</summary>
public sealed class StudioHostingException : InvalidOperationException
{
    public StudioHostingException(string message, Exception? innerException = null) : base(message, innerException) { }
}
