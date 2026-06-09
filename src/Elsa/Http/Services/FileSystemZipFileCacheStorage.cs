using Elsa.Http.Core.Contracts;
using Elsa.Http.Options;
using Elsa.Serialization.Core;
using Microsoft.Extensions.Options;

namespace Elsa.Http.Services;

internal sealed class FileSystemZipFileCacheStorage(IOptions<HttpZipFileCacheOptions> options, IPayloadSerializer payloadSerializer) : IZipFileCacheStorage
{
    private const string metaDataFileSuffix = "_metadata.json";
    private readonly SemaphoreSlim metaDataLock = new(1, 1);
    private readonly SemaphoreSlim zipFileLock = new(1, 1);

    public async Task Delete(string relativeFilePath, CancellationToken cancellationToken = default)
    {
        var fullFilePath = GetFullFilePathInLocalDirectory(relativeFilePath);

        await zipFileLock.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(fullFilePath))
            {
                File.Delete(fullFilePath);
            }
        }
        finally
        {
            zipFileLock.Release();
        }
    }

    public async Task<Stream> Read(string relativeFilePath, CancellationToken cancellationToken = default)
    {
        var fullFilePath = GetFullFilePathInLocalDirectory(relativeFilePath);
        await zipFileLock.WaitAsync(cancellationToken);
        try
        {
            var result = new MemoryStream();

            using var fileStream = File.OpenRead(fullFilePath);
            await fileStream.CopyToAsync(result, cancellationToken);
            await fileStream.FlushAsync(cancellationToken);

            return result;
        }
        finally
        {
            zipFileLock.Release();
        }
    }

    public async Task Write(string relativeFilePath, Stream stream, CancellationToken cancellationToken = default)
    {
        var fullFilePath = GetFullFilePathInLocalDirectory(relativeFilePath);
        await zipFileLock.WaitAsync(cancellationToken);
        try
        {
            using var writeStream = File.OpenWrite(fullFilePath);
            await stream.CopyToAsync(writeStream, cancellationToken);
        }
        finally
        {
            await stream.DisposeAsync();
            zipFileLock.Release();
        }
    }

    public async Task SetMetaData<TMetaData>(string relativeFilePath, TMetaData metaData, CancellationToken cancellationToken = default)
        where TMetaData : class
    {
        var metaDataPath = GetMetaDataFilePath(relativeFilePath);

        await metaDataLock.WaitAsync(cancellationToken);
        try
        {
            var jsonContent = payloadSerializer.Serialize(metaData);
            File.WriteAllText(metaDataPath, jsonContent);
        }
        finally
        {
            metaDataLock.Release();
        }
    }
    public async Task<TMetaData?> GetMetaData<TMetaData>(string relativeFilePath, CancellationToken cancellationToken = default)
        where TMetaData : class
    {
        var metaDataPath = GetMetaDataFilePath(relativeFilePath);
        if (!File.Exists(metaDataPath))
        {
            return default;
        }

        await metaDataLock.WaitAsync(cancellationToken);
        try
        {
            var jsonContent = File.ReadAllText(metaDataPath);
            return payloadSerializer.Deserialize<TMetaData>(jsonContent);
        }
        finally
        {
            metaDataLock.Release();
        }
    }

    private string GetMetaDataFilePath(string relativeFilePath)
    {
        var fullFilePath = GetFullFilePathInLocalDirectory(relativeFilePath);
        var extension = Path.GetExtension(fullFilePath);

        var result = fullFilePath.Replace(extension, string.Empty);

        return string.Concat(
            result,
            metaDataFileSuffix
        );
    }

    private string GetFullFilePathInLocalDirectory(string relativeFilePath)
    {
        if (Path.IsPathFullyQualified(relativeFilePath))
        {
            throw new InvalidOperationException($"Path '{relativeFilePath}' is an absolute path; which is not allowed. Please specify only a relative path");
        }

        return Path.Combine(options.Value.LocalCacheDirectory, relativeFilePath);
    }
}