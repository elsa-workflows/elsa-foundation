using Elsa.Http.Core.Models;

namespace Elsa.Http.Core.Contracts;

public interface IZipArchiveManager
{
    Task<ZipFileArchive> CreateAsync(
        ICollection<Func<ValueTask<Downloadable>>> downloadables,
        bool useCache,
        string? downloadCorrelationId,
        string downloadAsFilename = "download.zip",
        string contentType = System.Net.Mime.MediaTypeNames.Application.Zip,
        CancellationToken cancellationToken = default
    );

    Task<ZipFileArchive?> LoadAsync(
        string downloadCorrelationId,
        CancellationToken cancellationToken = default
    );
}