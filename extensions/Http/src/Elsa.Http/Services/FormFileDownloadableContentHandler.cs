using Elsa.Http.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Http.Services;

/// <summary>
/// Handles content that represents a downloadable <see cref="IFormFile"/>.
/// </summary>
internal sealed class FormFileDownloadableContentHandler : TypedDownloadableContentHandler<IFormFile>
{
    /// <inheritdoc />
    protected override Downloadable Map(IFormFile content) => new(content.OpenReadStream(), Path.GetFileName(content.FileName), content.ContentType);
}
