using Elsa.Http.Core.Contracts;
using Elsa.Http.Core.Models;
using Elsa.Http.Options;
using Elsa.Primitives.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO.Compression;

namespace Elsa.Http.Services
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ZipManager"/> class.
    /// </summary>
    internal sealed class ZipArchiveManager(ISystemClock clock, IZipFileCacheStorageProviders fileCacheStorageFactory, IOptions<HttpZipFileCacheOptions> fileCacheOptions, ILogger<ZipArchiveManager> logger)
        : IZipArchiveManager
    {
        public async Task<ZipFileArchive> CreateAsync(
            ICollection<Func<ValueTask<Downloadable>>> downloadables,
            bool cache,
            string? downloadCorrelationId,
            string downloadAsFilename,
            string contentType,
            CancellationToken cancellationToken)
        {
            // Create a temporary file.
            var tempFilePath = GetTempFilePath();

            // Create a zip archive from the downloadables.
            await CreateZipArchiveAsync(tempFilePath, downloadables, cancellationToken);

            // Create a blob with metadata for resuming the download.
            var zipBlob = CreateBlob(tempFilePath, downloadAsFilename, contentType);

            // If resumable downloads are enabled, cache the file.
            if (cache && !string.IsNullOrWhiteSpace(downloadCorrelationId))
                await CreateCachedZipBlobAsync(tempFilePath, downloadCorrelationId, downloadAsFilename, contentType, cancellationToken);

            var zipStream = File.OpenRead(tempFilePath);
            return new ZipFileArchive(downloadAsFilename, contentType, zipStream, () => Cleanup(tempFilePath));
        }

        /// <summary>
        /// Loads a cached zip blob for the specified download correlation ID.
        /// </summary>
        /// <param name="downloadCorrelationId">The download correlation ID.</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        /// <returns>A tuple containing the blob and the stream.</returns>
        public async Task<ZipFileArchive?> LoadAsync(string downloadCorrelationId, CancellationToken cancellationToken = default)
        {
            var fileCacheStorage = fileCacheStorageFactory.GetStorage();
            var fileCacheFilename = $"{downloadCorrelationId}.tmp";
            var blob = await fileCacheStorage.GetMetaData<InternalBlob>(fileCacheFilename, cancellationToken);

            if (blob == null)
                return null;

            // Check if the blob has expired.
            var expiresAt = blob.ExpiresAt;

            if (clock.UtcNow > expiresAt)
            {
                // File expired. Try to delete it.
                try
                {
                    await fileCacheStorage.Delete(blob.FilePath, cancellationToken);
                }
                catch (Exception e)
                {
                    logger.LogWarning(e, "Failed to delete expired file {FullPath}", blob.FilePath);
                }

                return null;
            }

            var stream = await fileCacheStorage.Read(blob.FilePath, cancellationToken);
            return new ZipFileArchive(blob.DownloadAsFileName, blob.ContentType, stream, null);
        }

        /// <summary>
        /// Creates a zip archive from the specified <see cref="Downloadable"/> instances.
        /// </summary>
        private static async Task CreateZipArchiveAsync(string filePath, IEnumerable<Func<ValueTask<Downloadable>>> downloadables, CancellationToken cancellationToken = default)
        {
            var currentFileIndex = 0;

            // Write the zip archive to the temporary file.
            await using var tempFileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);

            using var zipArchive = new ZipArchive(tempFileStream, ZipArchiveMode.Create, true);
            foreach (var downloadableFunc in downloadables)
            {
                var downloadable = await downloadableFunc();
                var entryName = !string.IsNullOrWhiteSpace(downloadable.Filename) ? downloadable.Filename : $"file-{currentFileIndex}.bin";
                var entry = zipArchive.CreateEntry(entryName);
                var fileStream = downloadable.Stream;
                await using var entryStream = entry.Open();
                await fileStream.CopyToAsync(entryStream, cancellationToken);
                await entryStream.FlushAsync(cancellationToken);
                entryStream.Close();
                currentFileIndex++;
            }
        }

        /// <summary>
        /// Creates a cached zip blob for the specified file.
        /// </summary>
        /// <param name="localPath">The full path of the file to upload.</param>
        /// <param name="downloadCorrelationId">The download correlation ID.</param>
        /// <param name="downloadAsFilename">The filename to use when downloading the file.</param>
        /// <param name="contentType">The content type of the file.</param>
        /// <param name="cancellationToken">An optional cancellation token.</param>
        private async Task CreateCachedZipBlobAsync(string localPath, string downloadCorrelationId, string downloadAsFilename, string contentType, CancellationToken cancellationToken)
        {
            var fileCacheStorage = fileCacheStorageFactory.GetStorage();
            var fileCacheFilename = $"{downloadCorrelationId}.tmp";
            var expiresAt = clock.UtcNow.Add(fileCacheOptions.Value.TimeToLive);
            var cachedBlob = CreateBlob(fileCacheFilename, downloadAsFilename, contentType, expiresAt);
            var localPathBytes = File.ReadAllBytes(localPath);
            var stream = new MemoryStream(localPathBytes);

            await fileCacheStorage.Write(fileCacheFilename, stream, cancellationToken);
            await fileCacheStorage.SetMetaData(fileCacheFilename, cachedBlob, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Creates a blob for the specified file.
        /// </summary>
        /// <param name="fullPath">The full path of the file.</param>
        /// <param name="downloadAsFilename">The filename to use when downloading the file.</param>
        /// <param name="contentType">The content type of the file.</param>
        /// <param name="expiresAt">The date and time at which the file expires.</param>
        /// <returns>The blob.</returns>
        private InternalBlob CreateBlob(string filePath, string downloadAsFilename, string contentType, DateTimeOffset? expiresAt = default)
        {
            var now = clock.UtcNow;

            return new InternalBlob(
                filePath, 
                downloadAsFilename, 
                contentType, 
                now, 
                now, 
                expiresAt
            );
        }

        private string GetTempFilePath()
        {
            var tempFileName = Path.GetRandomFileName();
            var tempFilePath = Path.Combine(fileCacheOptions.Value.LocalCacheDirectory, tempFileName);
            return tempFilePath;
        }

        private void Cleanup(string filePath)
        {
            try
            {
                File.Delete(filePath);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "Failed to delete temporary file {TempFilePath}", filePath);
            }
        }

        private sealed record InternalBlob(
            string FilePath,
            string DownloadAsFileName,
            string ContentType,
            DateTimeOffset CreatedAt,
            DateTimeOffset LastModificationTime,
            DateTimeOffset? ExpiresAt
        );
    }
}
