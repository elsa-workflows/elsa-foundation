using Elsa.Http.Core.Models;
using System.Text;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that represents a downloadable string file.
/// </summary>
internal sealed class StringDownloadableContentHandler : TypedDownloadableContentHandler<string>
{
    /// <inheritdoc />
    protected override Downloadable Map(string content) => new(new MemoryStream(Encoding.UTF8.GetBytes(content)), "file.txt", "text/plain");
}
