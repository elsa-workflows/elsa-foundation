using Microsoft.AspNetCore.Http;

namespace Elsa.Http.Core.Contracts
{
    /// <summary>
    /// Provides a way to select a value from the HttpContext, such as a correlation ID.
    /// </summary>
    public interface IHttpContextValueSelector
    {
        /// <summary>
        /// The priority of this selector. The selector with the highest priority will be used.
        /// </summary>
        double Priority { get; }

        /// <summary>
        /// The name of the context value selector (a common example is CorrelationId)
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Returns the correlation ID for the specified HTTP context, or <c>null</c> if no correlation ID could be found.
        /// </summary>
        ValueTask<string?> GetValue(HttpContext httpContext, CancellationToken cancellationToken = default);
    }
}
