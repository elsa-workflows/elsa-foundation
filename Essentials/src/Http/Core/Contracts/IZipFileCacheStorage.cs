namespace Elsa.Http.Core.Contracts;

public interface IZipFileCacheStorage
{
    Task<Stream> Read(string relativeFilePath, CancellationToken cancellationToken = default);

    Task Delete(string relativeFilePath, CancellationToken cancellationToken = default);

    Task Write(string relativeFilePath, Stream stream, CancellationToken cancellationToken = default);

    Task SetMetaData<TMetaData>(string relativeFilePath, TMetaData metaData, CancellationToken cancellationToken = default)
        where TMetaData : class;

    Task<TMetaData?> GetMetaData<TMetaData>(string relativeFilePath, CancellationToken cancellationToken = default)
        where TMetaData : class;
}